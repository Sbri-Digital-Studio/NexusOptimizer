using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>Applicazione in background rilevata come candidata alla chiusura.</summary>
public sealed record BackgroundAppInfo(
    int Pid,
    string ProcessName,
    string DisplayName,
    BackgroundAppCategory Category,
    long WorkingSetBytes,
    string ExecutablePath,
    string Note,
    bool RecommendedByDefault);

/// <summary>Esito di una singola azione del boost: nessun testo generico, solo fatti.</summary>
public sealed record GamingActionOutcome(string Title, string Detail, bool Applied);

/// <summary>Risultato misurato dell'attivazione.</summary>
public sealed record GamingActivationReport(
    int ClosedCount,
    int NotClosedCount,
    long MemoryFreedBytes,
    long TrimmedBytes,
    IReadOnlyList<GamingActionOutcome> Actions,
    IReadOnlyList<ClosedAppRecord> ClosedApps);

/// <summary>App chiusa dal boost, con il percorso necessario per riaprirla.</summary>
public sealed record ClosedAppRecord(string DisplayName, string ExecutablePath);

/// <summary>Opzioni del boost; ognuna corrisponde a un'azione reale e reversibile.</summary>
public sealed class GamingBoostOptions
{
    public bool CloseBackgroundApps { get; set; } = true;
    public bool HighPerformancePowerPlan { get; set; } = true;
    public bool DisableGameDvr { get; set; } = true;
    public bool EnableWindowsGameMode { get; set; } = true;
    public bool TrimBackgroundMemory { get; set; } = true;
    public bool PrioritizeForegroundGame { get; set; } = true;
    public bool SuspendIndexingServices { get; set; }
    public bool AllowForceClose { get; set; }
}

/// <summary>
/// Motore della Modalità Gaming. Ogni azione è (1) reale e misurata, (2) reversibile
/// alla disattivazione, (3) limitata al perimetro utente: nessuna scrittura in HKLM,
/// nessun servizio arrestato senza privilegi già disponibili, nessun processo di
/// sistema o sicurezza toccato. Lo stato precedente viene memorizzato prima di
/// qualsiasi modifica e ripristinato in <see cref="DeactivateAsync"/>.
/// </summary>
public sealed class GamingModeService
{
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string GameBarKey = @"Software\Microsoft\GameBar";

    /// <summary>Servizi ad alto I/O sospesi solo se l'app è già elevata.</summary>
    private static readonly string[] IndexingServices = ["SysMain", "WSearch"];

    private readonly FileLogService _log;
    private readonly IMemoryOptimizationService _memory;

    // --- Stato da ripristinare (popolato solo se l'azione è stata realmente applicata) ---
    private string? _previousPowerScheme;
    private object? _previousGameDvr;
    private object? _previousGameMode;
    private readonly List<ClosedAppRecord> _closedApps = [];
    private readonly List<string> _stoppedServices = [];
    private (int Pid, ProcessPriorityClass Priority)? _boostedProcess;
    private ProcessPriorityClass? _ownPreviousPriority;

    public GamingModeService(FileLogService log, IMemoryOptimizationService memory)
    {
        _log = log;
        _memory = memory;
    }

    /// <summary>Costruttore mantenuto per i test isolati e gli strumenti locali.</summary>
    public GamingModeService(FileLogService log)
        : this(log, new MemoryOptimizationService())
    {
    }

    public bool IsActive { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public IReadOnlyList<ClosedAppRecord> ClosedApps => _closedApps;

    /// <summary>Il boost è amministratore? Determina se i servizi possono essere sospesi.</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }
    }

    // ------------------------------------------------------------------ scan

    /// <summary>
    /// Elenca le app in background realmente presenti. Il livello modalità decide
    /// solo quali voci risultano pre-selezionate e se le app non catalogate vengono
    /// mostrate: la scelta finale resta sempre dell'utente.
    /// </summary>
    public Task<IReadOnlyList<BackgroundAppInfo>> ScanAsync(AppModeLevel level)
        => Task.Run<IReadOnlyList<BackgroundAppInfo>>(() => Scan(level));

    private IReadOnlyList<BackgroundAppInfo> Scan(AppModeLevel level)
    {
        var found = new Dictionary<string, BackgroundAppInfo>(StringComparer.OrdinalIgnoreCase);
        var ownId = Environment.ProcessId;
        var ownSession = CurrentSessionId();
        var foreground = ForegroundProcessId();

        foreach (var process in SafeGetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (process.Id == ownId || name.Length == 0) continue;
                    if (ProtectedProcesses.IsProtected(name)) continue;
                    if (process.Id == foreground) continue;      // l'app in primo piano non è "background"
                    if (process.SessionId != ownSession) continue; // solo la sessione dell'utente corrente

                    var entry = BackgroundAppCatalog.Find(name);
                    var workingSet = Math.Max(0, process.WorkingSet64);

                    if (entry is null)
                    {
                        // Non catalogata: proposta solo in EXPERT, solo se pesa davvero
                        // e solo se ha una finestra (quindi è un'app utente, non un helper).
                        if (level != AppModeLevel.Expert) continue;
                        if (workingSet < 60L * 1024 * 1024) continue;
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        entry = new BackgroundAppEntry(name, FriendlyName(process, name),
                            BackgroundAppCategory.Altro, AppModeLevel.Expert,
                            "App non catalogata: verifica di non avere lavoro non salvato.");
                    }

                    // Più istanze dello stesso eseguibile (browser, Electron) vengono
                    // aggregate su un'unica riga: la chiusura agisce sull'intero gruppo.
                    if (found.TryGetValue(name, out var existing))
                    {
                        found[name] = existing with { WorkingSetBytes = existing.WorkingSetBytes + workingSet };
                        continue;
                    }

                    found[name] = new BackgroundAppInfo(
                        process.Id,
                        name,
                        entry.DisplayName,
                        entry.Category,
                        workingSet,
                        TryGetPath(process),
                        entry.Note,
                        level >= entry.DefaultFromLevel);
                }
                catch (Exception)
                {
                    // Un processo può terminare o negare l'accesso durante la lettura.
                }
            }
        }

        return found.Values
            .OrderByDescending(app => app.RecommendedByDefault)
            .ThenByDescending(app => app.WorkingSetBytes)
            .ToList();
    }

    // -------------------------------------------------------------- activate

    public Task<GamingActivationReport> ActivateAsync(GamingBoostOptions options,
                                                      IReadOnlyList<BackgroundAppInfo> selected)
        => Task.Run(() => Activate(options, selected));

    private GamingActivationReport Activate(GamingBoostOptions options, IReadOnlyList<BackgroundAppInfo> selected)
    {
        var actions = new List<GamingActionOutcome>();
        var availableBefore = _memory.AvailableMemoryBytes();
        _closedApps.Clear();
        var notClosed = 0;
        long trimmed = 0;

        if (options.CloseBackgroundApps && selected.Count > 0)
        {
            var (closed, failed) = CloseApps(selected, options.AllowForceClose);
            notClosed = failed;
            actions.Add(new GamingActionOutcome(
                Locale.T("gam.rep.apps"),
                failed == 0
                    ? Locale.F("gam.rep.apps.ok", [Text(closed)])
                    : Locale.F("gam.rep.apps.partial", [Text(closed), Text(failed)]),
                closed > 0));
        }

        if (options.HighPerformancePowerPlan)
        {
            var applied = ApplyPowerPlan(out var planName);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.power"),
                applied ? Locale.F("gam.rep.power.ok", [planName])
                        : Locale.T("gam.rep.power.none"), applied));
        }

        if (options.DisableGameDvr)
        {
            var applied = SetGameDvr(false);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.dvr"),
                Locale.T(applied ? "gam.rep.dvr.ok" : "gam.rep.key.locked"), applied));
        }

        if (options.EnableWindowsGameMode)
        {
            var applied = SetWindowsGameMode(true);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.gamemode"),
                Locale.T(applied ? "gam.rep.gamemode.ok" : "gam.rep.key.locked"), applied));
        }

        if (options.PrioritizeForegroundGame)
        {
            var boosted = BoostForegroundProcess(out var gameName);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.priority"),
                boosted ? Locale.F("gam.rep.priority.ok", [gameName])
                        : Locale.T("gam.rep.priority.none"), boosted));
        }

        if (options.TrimBackgroundMemory)
        {
            trimmed = TrimBackgroundWorkingSets();
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.memory"),
                trimmed > 0
                    ? Locale.F("gam.rep.memory.ok", [FormatBytes(trimmed)])
                    : Locale.T("gam.rep.memory.none"), trimmed > 0));
        }

        if (options.SuspendIndexingServices)
        {
            var stopped = StopIndexingServices();
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.services"),
                IsElevated
                    ? stopped > 0 ? Locale.F("gam.rep.services.ok", [Text(stopped)])
                                  : Locale.T("gam.rep.services.none")
                    : Locale.T("gam.rep.services.admin"),
                stopped > 0));
        }

        // La misura è la differenza reale di memoria fisica disponibile, non una stima.
        var availableAfter = _memory.AvailableMemoryBytes();
        var freed = availableBefore is long before && availableAfter is long after
            ? Math.Max(0, after - before)
            : 0;

        IsActive = true;
        ActivatedAtUtc = DateTime.UtcNow;
        _log.Info($"Modalità Gaming attivata: {_closedApps.Count} app chiuse, "
                  + $"{freed} byte di RAM tornati disponibili, {trimmed} byte compattati.");

        return new GamingActivationReport(_closedApps.Count, notClosed, freed, trimmed, actions, _closedApps.ToArray());
    }

    // ------------------------------------------------------------ deactivate

    public Task<IReadOnlyList<GamingActionOutcome>> DeactivateAsync()
        => Task.Run<IReadOnlyList<GamingActionOutcome>>(Deactivate);

    private IReadOnlyList<GamingActionOutcome> Deactivate()
    {
        var actions = new List<GamingActionOutcome>();

        if (_previousPowerScheme is { Length: > 0 } scheme)
        {
            var ok = PowerPlanService.Activate(scheme);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.power"),
                Locale.T(ok ? "gam.rep.power.back" : "gam.rep.power.backfailed"), ok));
            _previousPowerScheme = null;
        }

        if (_previousGameDvr is not null || _previousGameMode is not null)
        {
            RestoreValue(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", ref _previousGameDvr);
            RestoreValue(Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled", ref _previousGameMode);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.prefs"), Locale.T("gam.rep.prefs.back"), true));
        }

        if (_boostedProcess is { } boosted)
        {
            TrySetPriority(boosted.Pid, boosted.Priority);
            _boostedProcess = null;
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.priority"), Locale.T("gam.rep.priority.back"), true));
        }

        if (_ownPreviousPriority is { } own)
        {
            TrySetPriority(Environment.ProcessId, own);
            _ownPreviousPriority = null;
        }

        if (_stoppedServices.Count > 0)
        {
            var restarted = StartServices(_stoppedServices);
            actions.Add(new GamingActionOutcome(Locale.T("gam.rep.services"),
                Locale.F("gam.rep.services.back", [Text(restarted)]), restarted > 0));
            _stoppedServices.Clear();
        }

        IsActive = false;
        ActivatedAtUtc = null;
        _log.Info("Modalità Gaming disattivata: stato di sistema ripristinato.");
        return actions;
    }

    /// <summary>Riapre le app chiuse dal boost usando il percorso registrato.</summary>
    public Task<int> RestoreClosedAppsAsync()
        => Task.Run(() =>
        {
            var restored = 0;
            foreach (var app in _closedApps.ToArray())
            {
                if (app.ExecutablePath.Length == 0 || !File.Exists(app.ExecutablePath)) continue;
                try
                {
                    Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true });
                    restored++;
                }
                catch (Exception ex) { _log.Warning($"Riavvio di {app.DisplayName} non riuscito: {ex.Message}"); }
            }
            if (restored > 0) _closedApps.Clear();
            return restored;
        });

    // ----------------------------------------------------------------- azioni

    private (int Closed, int Failed) CloseApps(IReadOnlyList<BackgroundAppInfo> selected, bool allowForce)
    {
        var targets = new List<Process>();
        var names = selected.Select(s => s.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labels = selected.ToDictionary(s => s.ProcessName, s => s.DisplayName, StringComparer.OrdinalIgnoreCase);
        var ownSession = CurrentSessionId();

        foreach (var process in SafeGetProcesses())
        {
            var keep = false;
            try
            {
                if (!names.Contains(process.ProcessName)) continue;
                if (ProtectedProcesses.IsProtected(process.ProcessName)) continue;
                if (process.SessionId != ownSession) continue;
                if (process.Id == Environment.ProcessId) continue;
                targets.Add(process);
                keep = true;
            }
            catch (Exception) { }
            finally { if (!keep) process.Dispose(); }
        }

        // 1) chiusura ordinata: l'app salva stato e sessione come farebbe con la X.
        foreach (var process in targets)
        {
            try
            {
                var path = TryGetPath(process);
                var label = labels.GetValueOrDefault(process.ProcessName, process.ProcessName);
                if (path.Length > 0 && !_closedApps.Any(a => string.Equals(a.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
                    _closedApps.Add(new ClosedAppRecord(label, path));
                if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
            }
            catch (Exception) { }
        }

        WaitForExit(targets, TimeSpan.FromSeconds(4));

        // 2) solo in EXPERT e solo se l'utente lo ha concesso: chiusura forzata.
        if (allowForce)
        {
            foreach (var process in targets)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception) { }
            }
            WaitForExit(targets, TimeSpan.FromSeconds(2));
        }

        var closed = 0;
        var failed = 0;
        foreach (var process in targets)
        {
            try { if (process.HasExited) closed++; else failed++; }
            catch (Exception) { failed++; }
            finally { process.Dispose(); }
        }

        // Le app rimaste vive non devono comparire tra quelle da riaprire.
        if (failed > 0)
        {
            var alive = SafeGetProcesses().Select(p => { var n = SafeName(p); p.Dispose(); return n; })
                                          .Where(n => n.Length > 0)
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _closedApps.RemoveAll(a => alive.Contains(Path.GetFileNameWithoutExtension(a.ExecutablePath)));
        }

        return (closed, failed);
    }

    private static void WaitForExit(List<Process> processes, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var pending = false;
            foreach (var process in processes)
            {
                try { if (!process.HasExited) { pending = true; break; } }
                catch (Exception) { }
            }
            if (!pending) return;
            Thread.Sleep(120);
        }
    }

    private bool ApplyPowerPlan(out string planName)
    {
        planName = "Prestazioni elevate";
        try
        {
            var current = PowerPlanService.ReadActive();
            var target = PowerPlanService.FindPerformancePlan();
            if (current is null || target is null) return false;

            planName = target.Name;
            if (string.Equals(current.SchemeId, target.SchemeId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!PowerPlanService.Activate(target.SchemeId)) return false;

            _previousPowerScheme = current.SchemeId;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Piano energetico non modificato: {ex.Message}");
            return false;
        }
    }

    private bool SetGameDvr(bool enabled)
        => WriteUserDword(GameConfigStoreKey, "GameDVR_Enabled", enabled ? 1 : 0, ref _previousGameDvr);

    private bool SetWindowsGameMode(bool enabled)
        => WriteUserDword(GameBarKey, "AutoGameModeEnabled", enabled ? 1 : 0, ref _previousGameMode);

    /// <summary>
    /// Scrive una preferenza utente salvando il valore precedente per il ripristino.
    /// Visibile ai test: e' il punto in cui la Modalita' Gaming tocca davvero il sistema.
    /// </summary>
    internal bool WriteUserDword(string subKey, string name, int value, ref object? previous)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            if (key is null) return false;
            previous ??= key.GetValue(name) ?? "<assente>";
            if (key.GetValue(name) is int existing && existing == value) return true;
            key.SetValue(name, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Preferenza {subKey}\\{name} non modificata: {ex.Message}");
            return false;
        }
    }

    internal void RestoreValue(RegistryKey root, string subKey, string name, ref object? previous)
    {
        if (previous is null) return;
        try
        {
            using var key = root.CreateSubKey(subKey, writable: true);
            if (key is null) return;
            if (previous is string marker && marker == "<assente>") key.DeleteValue(name, throwOnMissingValue: false);
            else key.SetValue(name, previous, RegistryValueKind.DWord);
        }
        catch (Exception ex) { _log.Warning($"Ripristino {subKey}\\{name} non riuscito: {ex.Message}"); }
        finally { previous = null; }
    }

    private bool BoostForegroundProcess(out string gameName)
    {
        gameName = string.Empty;
        var pid = ForegroundProcessId();
        if (pid <= 0 || pid == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (ProtectedProcesses.IsProtected(process.ProcessName)) return false;
            gameName = FriendlyName(process, process.ProcessName);
            _boostedProcess = (pid, process.PriorityClass);
            process.PriorityClass = ProcessPriorityClass.High;

            using var own = Process.GetCurrentProcess();
            _ownPreviousPriority = own.PriorityClass;
            own.PriorityClass = ProcessPriorityClass.BelowNormal;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Priorità non modificata: {ex.Message}");
            _boostedProcess = null;
            return false;
        }
    }

    private static void TrySetPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.PriorityClass = priority;
        }
        catch (Exception) { /* il processo può essere già chiuso */ }
    }

    /// <summary>
    /// EmptyWorkingSet sulle app utente residue: la memoria fisica torna disponibile
    /// e Windows ricarica le pagine solo quando servono davvero. Operazione non
    /// distruttiva e limitata alla sessione dell'utente corrente.
    /// </summary>
    private long TrimBackgroundWorkingSets() => _memory.OptimizeRam().TrimmedWorkingSetBytes;

    private int StopIndexingServices()
    {
        if (!IsElevated) return 0;
        var stopped = 0;
        foreach (var name in IndexingServices)
        {
            try
            {
                using var service = new ServiceController(name);
                if (service.Status != ServiceControllerStatus.Running) continue;
                if (!service.CanStop) continue;
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6));
                _stoppedServices.Add(name);
                stopped++;
            }
            catch (Exception ex) { _log.Warning($"Servizio {name} non sospeso: {ex.Message}"); }
        }
        return stopped;
    }

    private int StartServices(IEnumerable<string> names)
    {
        var started = 0;
        foreach (var name in names)
        {
            try
            {
                using var service = new ServiceController(name);
                if (service.Status == ServiceControllerStatus.Running) { started++; continue; }
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                started++;
            }
            catch (Exception ex) { _log.Warning($"Servizio {name} non riavviato: {ex.Message}"); }
        }
        return started;
    }

    // ---------------------------------------------------------------- helper

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try { return Process.GetProcesses(); }
        catch (Exception) { return []; }
    }

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch (Exception) { return string.Empty; }
    }

    private static string TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    private static string FriendlyName(Process process, string fallback)
    {
        try
        {
            var path = TryGetPath(process);
            if (path.Length == 0) return fallback;
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = info.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? fallback : description.Trim();
        }
        catch (Exception) { return fallback; }
    }

    private static int CurrentSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception) { return 1; }
    }

    private static int ForegroundProcessId()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return 0;
            _ = GetWindowThreadProcessId(handle, out var pid);
            return (int)pid;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>Conteggio come testo per i segnaposto dei messaggi localizzati.</summary>
    private static string Text(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatBytes(long bytes)
    {
        const double K = 1024;
        if (bytes >= K * K * K) return (bytes / (K * K * K)).ToString("N1", CultureInfo.CurrentCulture) + " GB";
        if (bytes >= K * K) return (bytes / (K * K)).ToString("N0", CultureInfo.CurrentCulture) + " MB";
        return bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";
    }

    // ------------------------------------------------------------- P/Invoke

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
