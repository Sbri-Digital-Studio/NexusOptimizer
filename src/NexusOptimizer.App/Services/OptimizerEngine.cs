using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>Stato corrente di un'ottimizzazione, misurato sul sistema reale.</summary>
public sealed record OptimizerInspection(
    bool IsApplied,
    string StateText,
    string MeasureText,
    bool CanApply,
    bool IsTargetState = false,
    bool HasAnyTargetState = false);

/// <summary>Esito di un'applicazione o di un annullamento.</summary>
public sealed record OptimizerOutcome(bool Changed, string Message);

/// <summary>Entita' del beneficio atteso o del rischio di un'ottimizzazione.</summary>
public enum OptimizerImpact
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Una singola ottimizzazione. Il contratto è quello del resto del programma:
/// si può ispezionare senza toccare nulla, si applica solo su richiesta esplicita
/// e — quando la natura dell'operazione lo consente — si annulla riportando il
/// sistema allo stato precedente, salvato prima della modifica.
/// </summary>
public abstract class OptimizerAction
{
    protected OptimizerAction(string id, OptimizerImpact benefit, OptimizerImpact risk,
                              string iconKind, string targetId, bool reversible,
                              AppModeLevel minimumLevel = AppModeLevel.Safe)
    {
        Id = id;
        Benefit = benefit;
        Risk = risk;
        IconKind = iconKind;
        TargetId = targetId;
        IsReversible = reversible;
        MinimumLevel = minimumLevel;
    }

    public string Id { get; }

    /// <summary>
    /// Identificatore usato nel registro di ripristino. Normalmente coincide con
    /// <see cref="Id"/>; alcune integrazioni di sistema usano una chiave tecnica
    /// distinta, mantenuta qui per evitare euristiche nella UI.
    /// </summary>
    public virtual string TrackingId => Id;

    /// <summary>
    /// Titolo e descrizione vivono nel dizionario di localizzazione, sotto una
    /// chiave derivata dall'identificatore: il catalogo resta una tabella di
    /// fatti, non un contenitore di frasi in una sola lingua.
    /// </summary>
    public string Title => Locale.T("opt.item." + Id + ".title");

    public string Detail => Locale.T("opt.item." + Id + ".detail");

    /// <summary>Impatto atteso e rischio: valori confrontabili, non etichette tradotte.</summary>
    public OptimizerImpact Benefit { get; }

    public OptimizerImpact Risk { get; }

    public string BenefitText => Locale.T(ImpactKey(Benefit));

    public string RiskText => Locale.T(ImpactKey(Risk));

    private static string ImpactKey(OptimizerImpact impact) => impact switch
    {
        OptimizerImpact.High => "opt.level.high",
        OptimizerImpact.Medium => "opt.level.medium",
        _ => "opt.level.low",
    };

    public string IconKind { get; }

    /// <summary>Pagina di approfondimento aperta dalla freccia della riga.</summary>
    public string TargetId { get; }

    public bool IsReversible { get; }

    /// <summary>
    /// Livello operativo richiesto. SAFE contiene solo azioni che non scrivono
    /// preferenze di sistema; da BALANCED in su si sbloccano le modifiche al
    /// registro dell'utente, in EXPERT quelle che cambiano il comportamento
    /// dell'intero PC anche fuori da Nexus.
    /// </summary>
    public AppModeLevel MinimumLevel { get; }

    public abstract Task<OptimizerInspection> InspectAsync();
    public abstract Task<OptimizerOutcome> ApplyAsync();

    public virtual Task<OptimizerOutcome> RevertAsync()
        => Task.FromResult(new OptimizerOutcome(false, Locale.T("opt.norevert")));

    /// <summary>
    /// Alcune configurazioni note possono essere riportate ai valori consigliati
    /// di Windows anche quando Nexus non possiede uno snapshot precedente.
    /// L'operazione resta separata dall'undo esatto e richiede conferma esplicita.
    /// </summary>
    public virtual bool CanResetToRecommendedDefaults => false;

    public virtual Task<OptimizerOutcome> ResetToRecommendedDefaultsAsync()
        => Task.FromResult(new OptimizerOutcome(false, Locale.T("restore.recommended.unsupported")));
}

/// <summary>
/// Registro delle ottimizzazioni e memoria dello stato precedente. Ogni valore
/// modificato viene salvato in config.json prima della scrittura: l'annullamento
/// funziona anche dopo un riavvio dell'applicazione.
/// </summary>
public sealed class OptimizerEngine
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly FileLogService _log;
    private readonly AppModeService _mode;

    public OptimizerEngine(AppConfig config, ConfigStore store, FileLogService log,
                           StartupService startup, Core.Safety.SafetyEngine safety,
                           AppModeService mode, IMemoryOptimizationService memory)
    {
        _config = config;
        _store = store;
        _log = log;
        _mode = mode;

        Actions =
        [
            new StartupCleanupAction(this, startup, log),
            new CacheCleanupAction(this, config, log, safety),
            new WorkingSetTrimAction(memory),
            new WindowsExperienceAction(this),
            new VisualEffectsAction(this),
            new PowerPlanAction(this),
        ];
    }

    /// <summary>Costruttore mantenuto per test e strumenti che compongono il motore a mano.</summary>
    public OptimizerEngine(AppConfig config, ConfigStore store, FileLogService log,
                           StartupService startup, Core.Safety.SafetyEngine safety,
                           AppModeService mode)
        : this(config, store, log, startup, safety, mode, new MemoryOptimizationService())
    {
    }

    public IReadOnlyList<OptimizerAction> Actions { get; }

    /// <summary>Livello operativo corrente, condiviso con la Modalità Gaming.</summary>
    public AppModeLevel Level => _mode.Level;

    /// <summary>Notificato al cambio livello: le viste riaggiornano i blocchi.</summary>
    public event Action? LevelChanged
    {
        add => _mode.Changed += value;
        remove => _mode.Changed -= value;
    }

    /// <summary>
    /// Un'azione è disponibile solo se il livello scelto la comprende. Il blocco
    /// è verificato anche qui, non solo nell'interfaccia: nessuna scorciatoia può
    /// applicare un'ottimizzazione fuori dal perimetro dichiarato.
    /// </summary>
    public bool IsUnlocked(OptimizerAction action) => _mode.Level >= action.MinimumLevel;

    // ------------------------------------------------------- memoria di stato

    internal void Remember(string actionId, string keyPath, string valueName, object? previous, string valueKind)
    {
        var entry = _config.OptimizerState.FirstOrDefault(
            e => e.ActionId == actionId && e.KeyPath == keyPath && e.ValueName == valueName);
        if (entry is not null) return; // il primo stato salvato è quello originale

        _config.OptimizerState.Add(new OptimizerRestoreEntry
        {
            ActionId = actionId,
            KeyPath = keyPath,
            ValueName = valueName,
            PreviousValue = previous?.ToString(),
            ValueKind = valueKind,
            AppliedAtUtc = DateTime.UtcNow,
        });
        Persist();
    }

    internal IReadOnlyList<OptimizerRestoreEntry> StateOf(string actionId)
        => _config.OptimizerState.Where(e => e.ActionId == actionId).ToArray();

    internal void Forget(string actionId)
    {
        if (_config.OptimizerState.RemoveAll(e => e.ActionId == actionId) > 0) Persist();
    }

    internal void ForgetValues(string actionId, IEnumerable<string> valueNames)
    {
        var names = valueNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return;
        if (_config.OptimizerState.RemoveAll(
                entry => entry.ActionId == actionId && names.Contains(entry.ValueName)) > 0)
            Persist();
    }

    internal DateTime? AppliedAt(string actionId)
        => _config.OptimizerState.Where(e => e.ActionId == actionId)
                                 .Select(e => (DateTime?)e.AppliedAtUtc)
                                 .FirstOrDefault();

    internal void Persist()
    {
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio stato Optimizer fallito", ex); }
    }

    internal void LogInfo(string message) => _log.Info(message);

    // ------------------------------------------------- helper registro utente

    /// <summary>Scrive una preferenza utente ricordando il valore precedente.</summary>
    internal bool WriteUserValue(string actionId, string subKey, string name, object value, RegistryValueKind kind)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            if (key is null) return false;
            Remember(actionId, subKey, name, key.GetValue(name), kind.ToString());
            key.SetValue(name, value, kind);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Preferenza {subKey}\\{name} non modificata: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Scrive un valore raccomandato senza creare un falso snapshot Nexus. Usato
    /// solo dal ripristino guidato di impostazioni trovate già modificate.
    /// </summary>
    internal bool WriteRecommendedUserValue(string subKey, string name, object value, RegistryValueKind kind)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            if (key is null) return false;
            key.SetValue(name, value, kind);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Valore consigliato {subKey}\\{name} non applicato: {ex.Message}");
            return false;
        }
    }

    internal bool DeleteRecommendedUserValue(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
            if (key is null) return true;
            key.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Valore personalizzato {subKey}\\{name} non rimosso: {ex.Message}");
            return false;
        }
    }

    /// <summary>Ripristina tutti i valori salvati per un'azione.</summary>
    internal int RestoreUserValues(string actionId)
    {
        var restored = 0;
        var restoredEntries = new List<OptimizerRestoreEntry>();
        foreach (var entry in StateOf(actionId))
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(entry.KeyPath, writable: true);
                if (key is null) continue;
                if (entry.PreviousValue is null)
                {
                    key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
                }
                else if (string.Equals(entry.ValueKind, "DWord", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(entry.PreviousValue, NumberStyles.Integer,
                                         CultureInfo.InvariantCulture, out var number))
                {
                    key.SetValue(entry.ValueName, number, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue(entry.ValueName, entry.PreviousValue, RegistryValueKind.String);
                }
                restored++;
                restoredEntries.Add(entry);
            }
            catch (Exception ex)
            {
                _log.Warning($"Ripristino {entry.KeyPath}\\{entry.ValueName} non riuscito: {ex.Message}");
            }
        }
        // Conserva gli snapshot che non è stato possibile ripristinare: un errore
        // parziale non deve far sparire l'unica strada per un tentativo successivo.
        if (restoredEntries.Count > 0
            && _config.OptimizerState.RemoveAll(restoredEntries.Contains) > 0)
            Persist();
        return restored;
    }

    /// <summary>Legge una preferenza utente come intero (null se assente).</summary>
    internal static int? ReadUserInt(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            var value = key?.GetValue(name);
            return value switch
            {
                int number => number,
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }
}

// ===========================================================================
//  AZIONI
// ===========================================================================

/// <summary>
/// Disattiva gli avvii automatici delle app catalogate come non essenziali
/// (updater, suite RGB, agenti di sincronizzazione). Usa lo stesso meccanismo
/// reversibile di Startup Manager: il comando originale resta salvato in config.
/// </summary>
internal sealed class StartupCleanupAction : OptimizerAction
{
    private readonly OptimizerEngine _engine;
    private readonly StartupService _startup;
    private readonly FileLogService _log;

    public StartupCleanupAction(OptimizerEngine engine, StartupService startup, FileLogService log)
        : base("startup", OptimizerImpact.High, OptimizerImpact.Low,
               "rocket", "nav.startup", reversible: true)
    {
        _engine = engine;
        _startup = startup;
        _log = log;
    }

    public override Task<OptimizerInspection> InspectAsync()
        => Task.Run(() =>
        {
            var disabled = _engine.StateOf(Id).Count;
            var candidates = Candidates().ToArray();
            if (disabled > 0)
                return new OptimizerInspection(true,
                    Locale.T("opt.state.applied") + " · "
                    + Locale.P(disabled, "opt.startup.off.one", "opt.startup.off.many"),
                    Locale.P(candidates.Length, "opt.startup.on.one", "opt.startup.on.many"),
                    candidates.Length > 0,
                    IsTargetState: true,
                    HasAnyTargetState: true);

            return new OptimizerInspection(false,
                candidates.Length == 0 ? Locale.T("opt.startup.none") : Locale.T("opt.state.notapplied"),
                candidates.Length == 0
                    ? Locale.T("opt.startup.clean")
                    : Locale.P(candidates.Length, "opt.startup.candidate.one", "opt.startup.candidate.many"),
                candidates.Length > 0);
        });

    public override Task<OptimizerOutcome> ApplyAsync()
        => Task.Run(() =>
        {
            var disabled = 0;
            foreach (var entry in Candidates())
            {
                try
                {
                    _startup.Disable(entry);
                    _engine.Remember(Id, entry.KeyPath, entry.Id, entry.Name, "Startup");
                    disabled++;
                }
                catch (Exception ex) { _log.Warning($"Avvio '{entry.Name}' non disattivato: {ex.Message}"); }
            }

            return disabled == 0
                ? new OptimizerOutcome(false, Locale.T("opt.startup.nothing"))
                : new OptimizerOutcome(true,
                    Locale.P(disabled, "opt.startup.done.one", "opt.startup.done.many")
                    + Locale.T("opt.startup.done.suffix"));
        });

    public override Task<OptimizerOutcome> RevertAsync()
        => Task.Run(() =>
        {
            var names = _engine.StateOf(Id).Select(e => e.ValueName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return new OptimizerOutcome(false, Locale.T("opt.startup.norevert"));

            var restored = 0;
            var restoredNames = new List<string>();
            foreach (var entry in _startup.Collect().Where(e => names.Contains(e.Id) && !e.IsEnabled))
            {
                try
                {
                    _startup.Enable(entry);
                    restored++;
                    restoredNames.Add(entry.Id);
                }
                catch (Exception ex) { _log.Warning($"Avvio '{entry.Name}' non riattivato: {ex.Message}"); }
            }
            _engine.ForgetValues(Id, restoredNames);
            return new OptimizerOutcome(restored > 0,
                Locale.P(restored, "opt.startup.back.one", "opt.startup.back.many") + ".");
        });

    /// <summary>Solo voci utente modificabili e presenti nel catalogo delle app non essenziali.</summary>
    private IEnumerable<StartupEntry> Candidates()
    {
        IReadOnlyList<StartupEntry> entries;
        try { entries = _startup.Collect(); }
        catch (Exception) { return []; }

        return entries.Where(entry =>
            entry.IsEnabled
            && entry.CanModify
            && BackgroundAppCatalog.Find(ExecutableName(entry)) is not null);
    }

    private static string ExecutableName(StartupEntry entry)
    {
        try
        {
            var path = StartupService.ExtractExecutablePath(entry.Command);
            return path.Length == 0
                ? entry.Name
                : System.IO.Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception) { return entry.Name; }
    }
}

/// <summary>
/// Pulisce cache e file temporanei sicuri. L'analisi è sempre eseguita prima:
/// l'utente vede quanto verrebbe liberato e l'eliminazione passa dal Cestino.
/// </summary>
internal sealed class CacheCleanupAction : OptimizerAction
{
    private static readonly string[] SafeCategories =
    ["user_temp", "thumbnail_cache", "dx_shader_cache", "edge_cache", "chrome_cache", "firefox_cache"];

    private readonly OptimizerEngine _engine;
    private readonly AppConfig _config;
    private readonly FileLogService _log;
    private readonly Core.Safety.SafetyEngine _safety;
    private ScanResult? _lastScan;

    public CacheCleanupAction(OptimizerEngine engine, AppConfig config, FileLogService log, Core.Safety.SafetyEngine safety)
        : base("cache", OptimizerImpact.High, OptimizerImpact.Low,
               "broom", "nav.cleancat", reversible: false)
    {
        _engine = engine;
        _config = config;
        _log = log;
        _safety = safety;
    }

    public override async Task<OptimizerInspection> InspectAsync()
    {
        try
        {
            var categories = CleanCatalog.Categories.Where(c => SafeCategories.Contains(c.Id)).ToArray();
            _lastScan = await new CleanScanner(_config.Exclusions)
                .ScanAsync(categories, progress: null, CancellationToken.None);
            var bytes = _lastScan.TotalBytes;
            return new OptimizerInspection(false,
                bytes > 0 ? Locale.T("opt.state.ready") : Locale.T("opt.cache.nothing"),
                Locale.F("opt.cache.recoverable", [bytes > 0 ? Formatter.Bytes(bytes) : "0 B"]),
                bytes > 0);
        }
        catch (Exception ex)
        {
            _log.Error("Analisi cache non riuscita", ex);
            return new OptimizerInspection(false, Locale.T("opt.cache.scanfailed"), Formatter.Unavailable, false);
        }
    }

    public override async Task<OptimizerOutcome> ApplyAsync()
    {
        if (_lastScan is null || _lastScan.TotalBytes <= 0)
            return new OptimizerOutcome(false, Locale.T("opt.cache.needscan"));

        try
        {
            var options = new CleanOptions
            {
                DryRun = false,
                UseRecycleBin = true,       // recuperabile dal Cestino
                UseQuarantine = false,
                Exclusions = _config.Exclusions,
            };
            var report = await new CleanExecutor(_config.Exclusions, _safety)
                .RunAsync(_lastScan, options, progress: null, CancellationToken.None);
            _engine.LogInfo($"Optimizer: pulizia cache completata, {report.BytesFreed} byte liberati.");
            _lastScan = null;
            return new OptimizerOutcome(report.ItemsRemoved > 0,
                Locale.F("opt.cache.applied",
                    [Formatter.Bytes(report.BytesFreed),
                     Locale.P(report.ItemsRemoved, "opt.cache.item.one", "opt.cache.item.many")]));
        }
        catch (Exception ex)
        {
            _log.Error("Pulizia cache non riuscita", ex);
            return new OptimizerOutcome(false, Locale.T("opt.cache.failed"));
        }
    }
}

/// <summary>
/// Preferenze utente di Windows che pesano su reattività e distrazioni. Solo
/// chiavi HKEY_CURRENT_USER, nessuna policy di sistema, tutte reversibili.
/// </summary>
internal sealed class WindowsExperienceAction : OptimizerAction
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ContentKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string SearchKey = @"Software\Policies\Microsoft\Windows\Explorer";

    private readonly OptimizerEngine _engine;

    public WindowsExperienceAction(OptimizerEngine engine)
        : base("windows", OptimizerImpact.Medium, OptimizerImpact.Low,
               "monitor", "nav.tools", reversible: true,
               minimumLevel: AppModeLevel.Balanced)
        => _engine = engine;

    public override Task<OptimizerInspection> InspectAsync()
        => Task.Run(() =>
        {
            var transparency = OptimizerEngine.ReadUserInt(PersonalizeKey, "EnableTransparency") ?? 1;
            var suggestions = OptimizerEngine.ReadUserInt(ContentKey, "SubscribedContent-338389Enabled") ?? 1;
            var systemSuggestions = OptimizerEngine.ReadUserInt(ContentKey, "SystemPaneSuggestionsEnabled") ?? 1;
            var silentApps = OptimizerEngine.ReadUserInt(ContentKey, "SilentInstalledAppsEnabled") ?? 1;
            var searchSuggestions = OptimizerEngine.ReadUserInt(SearchKey, "DisableSearchBoxSuggestions") ?? 0;

            var pending = new List<string>();
            if (transparency != 0) pending.Add(Locale.T("opt.windows.item.transparency"));
            if (suggestions != 0 || systemSuggestions != 0) pending.Add(Locale.T("opt.windows.item.tips"));
            if (silentApps != 0) pending.Add(Locale.T("opt.windows.item.silentapps"));
            if (searchSuggestions != 1) pending.Add(Locale.T("opt.windows.item.search"));

            var optimizedValues = 0;
            if (transparency == 0) optimizedValues++;
            if (suggestions == 0) optimizedValues++;
            if (systemSuggestions == 0) optimizedValues++;
            if (silentApps == 0) optimizedValues++;
            if (searchSuggestions == 1) optimizedValues++;

            var applied = _engine.StateOf(Id).Count > 0;
            return new OptimizerInspection(applied,
                applied ? Locale.T("opt.state.applied")
                        : pending.Count == 0 ? Locale.T("opt.state.optimized") : Locale.T("opt.state.notapplied"),
                pending.Count == 0 ? Locale.T("opt.windows.nothing") : string.Join(" · ", pending),
                pending.Count > 0,
                IsTargetState: pending.Count == 0,
                HasAnyTargetState: optimizedValues > 0);
        });

    public override Task<OptimizerOutcome> ApplyAsync()
        => Task.Run(() =>
        {
            var changed = 0;
            if (_engine.WriteUserValue(Id, PersonalizeKey, "EnableTransparency", 0, RegistryValueKind.DWord)) changed++;
            if (_engine.WriteUserValue(Id, ContentKey, "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord)) changed++;
            if (_engine.WriteUserValue(Id, ContentKey, "SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord)) changed++;
            if (_engine.WriteUserValue(Id, ContentKey, "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord)) changed++;
            if (_engine.WriteUserValue(Id, SearchKey, "DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord)) changed++;

            return changed == 0
                ? new OptimizerOutcome(false, Locale.T("opt.windows.nochange"))
                : new OptimizerOutcome(true,
                    Locale.P(changed, "opt.windows.done.one", "opt.windows.done.many")
                    + Locale.T("opt.windows.done.suffix"));
        });

    public override Task<OptimizerOutcome> RevertAsync()
        => Task.Run(() =>
        {
            var restored = _engine.RestoreUserValues(Id);
            return new OptimizerOutcome(restored > 0,
                Locale.P(restored, "opt.windows.back.one", "opt.windows.back.many")
                + Locale.T("opt.windows.back.suffix"));
        });

    public override bool CanResetToRecommendedDefaults => true;

    public override Task<OptimizerOutcome> ResetToRecommendedDefaultsAsync()
        => Task.Run(() =>
        {
            var restored = 0;
            if (_engine.WriteRecommendedUserValue(PersonalizeKey, "EnableTransparency", 1, RegistryValueKind.DWord)) restored++;
            if (_engine.WriteRecommendedUserValue(ContentKey, "SubscribedContent-338389Enabled", 1, RegistryValueKind.DWord)) restored++;
            if (_engine.WriteRecommendedUserValue(ContentKey, "SystemPaneSuggestionsEnabled", 1, RegistryValueKind.DWord)) restored++;
            if (_engine.WriteRecommendedUserValue(ContentKey, "SilentInstalledAppsEnabled", 1, RegistryValueKind.DWord)) restored++;
            if (_engine.DeleteRecommendedUserValue(SearchKey, "DisableSearchBoxSuggestions")) restored++;
            return new OptimizerOutcome(restored > 0,
                Locale.F("restore.recommended.windows.done", [restored.ToString(CultureInfo.CurrentCulture)]));
        });
}

/// <summary>
/// Compatta il working set delle app utente: la memoria fisica torna disponibile
/// e Windows ricarica le pagine solo quando servono. Operazione una tantum, non
/// distruttiva e senza stato da ripristinare.
/// </summary>
internal sealed class WorkingSetTrimAction : OptimizerAction
{
    private readonly IMemoryOptimizationService _memory;

    public WorkingSetTrimAction(IMemoryOptimizationService memory)
        : base("memory", OptimizerImpact.Medium, OptimizerImpact.Low,
               "memory", "nav.rammanager", reversible: false)
    {
        _memory = memory;
    }

    public override Task<OptimizerInspection> InspectAsync()
        => Task.Run(() =>
        {
            var available = _memory.AvailableMemoryBytes();
            return new OptimizerInspection(false, Locale.T("opt.state.ready"),
                available is long bytes
                    ? Locale.F("opt.memory.freenow", [Formatter.Bytes(bytes)])
                    : Formatter.Unavailable,
                true);
        });

    public override Task<OptimizerOutcome> ApplyAsync()
        => Task.Run(() =>
        {
            var result = _memory.OptimizeRam();
            var freed = result.RecoveredBytes;

            return freed > 0
                ? new OptimizerOutcome(true, Locale.F("opt.memory.freed", [Formatter.Bytes(freed)]))
                : new OptimizerOutcome(false, Locale.T("opt.memory.nothing"));
        });
}

/// <summary>
/// Riduce gli effetti visivi non necessari. Le animazioni delle finestre vengono
/// applicate subito tramite SystemParametersInfo, senza riavviare la sessione.
/// </summary>
internal sealed class VisualEffectsAction : OptimizerAction
{
    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpifSendChange = 0x02;

    private readonly OptimizerEngine _engine;

    public VisualEffectsAction(OptimizerEngine engine)
        : base("visual", OptimizerImpact.Medium, OptimizerImpact.Low,
               "chart", "nav.performance", reversible: true,
               minimumLevel: AppModeLevel.Balanced)
        => _engine = engine;

    public override Task<OptimizerInspection> InspectAsync()
        => Task.Run(() =>
        {
            var applied = _engine.StateOf(Id).Count > 0;
            var setting = OptimizerEngine.ReadUserInt(VisualEffectsKey, "VisualFXSetting");
            var animations = ReadAnimationEnabled();
            var pending = animations || setting is not 2;

            return new OptimizerInspection(applied,
                applied ? Locale.T("opt.state.applied")
                        : pending ? Locale.T("opt.state.notapplied") : Locale.T("opt.state.optimized"),
                Locale.T(animations ? "opt.visual.on" : "opt.visual.off"),
                pending,
                IsTargetState: !pending,
                HasAnyTargetState: !animations || setting is 2);
        });

    public override Task<OptimizerOutcome> ApplyAsync()
        => Task.Run(() =>
        {
            var changed = 0;
            if (_engine.WriteUserValue(Id, VisualEffectsKey, "VisualFXSetting", 2, RegistryValueKind.DWord)) changed++;
            if (_engine.WriteUserValue(Id, WindowMetricsKey, "MinAnimate", "0", RegistryValueKind.String)) changed++;
            var applied = SetAnimation(false);

            return changed == 0 && !applied
                ? new OptimizerOutcome(false, Locale.T("opt.visual.nochange"))
                : new OptimizerOutcome(true, Locale.T(applied ? "opt.visual.done.now" : "opt.visual.done.later"));
        });

    public override Task<OptimizerOutcome> RevertAsync()
        => Task.Run(() =>
        {
            var restored = _engine.RestoreUserValues(Id);
            SetAnimation(true);
            return new OptimizerOutcome(restored > 0, Locale.T("opt.visual.back"));
        });

    public override bool CanResetToRecommendedDefaults => true;

    public override Task<OptimizerOutcome> ResetToRecommendedDefaultsAsync()
        => Task.Run(() =>
        {
            var setting = _engine.WriteRecommendedUserValue(
                VisualEffectsKey, "VisualFXSetting", 0, RegistryValueKind.DWord);
            var registryAnimation = _engine.WriteRecommendedUserValue(
                WindowMetricsKey, "MinAnimate", "1", RegistryValueKind.String);
            var liveAnimation = SetAnimation(true);
            return new OptimizerOutcome(setting || registryAnimation || liveAnimation,
                Locale.T("restore.recommended.visual.done"));
        });

    private static bool ReadAnimationEnabled()
    {
        try
        {
            var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
            return SystemParametersInfo(0x0048 /* SPI_GETANIMATION */, info.Size, ref info, 0)
                   && info.MinAnimate != 0;
        }
        catch (Exception) { return true; }
    }

    private static bool SetAnimation(bool enabled)
    {
        try
        {
            var info = new AnimationInfo
            {
                Size = (uint)Marshal.SizeOf<AnimationInfo>(),
                MinAnimate = enabled ? 1 : 0,
            };
            return SystemParametersInfo(SpiSetAnimation, info.Size, ref info, SpifSendChange);
        }
        catch (Exception) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo info, uint update);
}

/// <summary>
/// Porta Windows su un piano energetico prestazionale già presente sul PC e ce
/// lo lascia. È l'unica ottimizzazione che cambia il comportamento della macchina
/// anche quando Nexus è chiuso: per questo vive in EXPERT. Il GUID del piano
/// precedente viene salvato prima del cambio e ripristinato dall'annullamento.
/// </summary>
internal sealed class PowerPlanAction : OptimizerAction
{
    private const string StateKey = "powercfg";
    private readonly OptimizerEngine _engine;

    public PowerPlanAction(OptimizerEngine engine)
        : base("power", OptimizerImpact.High, OptimizerImpact.Medium,
               "bolt", "nav.gaming", reversible: true,
               minimumLevel: AppModeLevel.Expert)
        => _engine = engine;

    public override string TrackingId => StateKey;

    public override Task<OptimizerInspection> InspectAsync()
        => Task.Run(() =>
        {
            var current = PowerPlanService.ReadActive();
            var target = PowerPlanService.FindPerformancePlan();
            if (current is null)
                return new OptimizerInspection(false, Locale.T("opt.power.unreadable"), Formatter.Unavailable, false);
            if (target is null)
                return new OptimizerInspection(false, Locale.T("opt.power.noplan"),
                    Locale.F("opt.power.inuse", [current.Name]), false);

            var already = string.Equals(current.SchemeId, target.SchemeId, StringComparison.OrdinalIgnoreCase);
            var applied = _engine.StateOf(StateKey).Count > 0;
            return new OptimizerInspection(applied || already,
                already ? Locale.T("opt.power.active")
                        : applied ? Locale.T("opt.state.applied") : Locale.T("opt.state.notapplied"),
                Locale.F("opt.power.inuse", [current.Name]), !already,
                IsTargetState: already,
                HasAnyTargetState: already);
        });

    public override Task<OptimizerOutcome> ApplyAsync()
        => Task.Run(() =>
        {
            var current = PowerPlanService.ReadActive();
            var target = PowerPlanService.FindPerformancePlan();
            if (current is null || target is null)
                return new OptimizerOutcome(false, Locale.T("opt.power.unavailable"));
            if (string.Equals(current.SchemeId, target.SchemeId, StringComparison.OrdinalIgnoreCase))
                return new OptimizerOutcome(false, Locale.F("opt.power.already", [target.Name]));

            _engine.Remember(StateKey, "powercfg", "scheme", current.SchemeId, "PowerScheme");
            if (!PowerPlanService.Activate(target.SchemeId))
                return new OptimizerOutcome(false, Locale.T("opt.power.refused"));

            _engine.LogInfo($"Optimizer: piano energetico impostato su {target.Name}.");
            return new OptimizerOutcome(true,
                Locale.F("opt.power.applied", [target.Name, current.Name]));
        });

    public override Task<OptimizerOutcome> RevertAsync()
        => Task.Run(() =>
        {
            var state = _engine.StateOf(StateKey);
            var previous = state.Count > 0 ? state[0].PreviousValue : null;
            if (previous is null || previous.Length == 0)
                return new OptimizerOutcome(false, Locale.T("opt.power.noprevious"));

            var restored = PowerPlanService.Activate(previous);
            if (restored) _engine.Forget(StateKey);
            return new OptimizerOutcome(restored,
                Locale.T(restored ? "opt.power.back" : "opt.power.backfailed"));
        });

    public override bool CanResetToRecommendedDefaults => true;

    public override Task<OptimizerOutcome> ResetToRecommendedDefaultsAsync()
        => Task.Run(() =>
        {
            var restored = PowerPlanService.Activate(PowerPlanService.BalancedGuid);
            return new OptimizerOutcome(restored,
                Locale.T(restored ? "restore.recommended.power.done" : "restore.recommended.power.failed"));
        });
}
