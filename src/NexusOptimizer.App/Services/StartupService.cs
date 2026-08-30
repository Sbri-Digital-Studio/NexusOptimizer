using System.Diagnostics;
using System.IO;
using System.Management;
using Microsoft.Win32;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

public sealed record StartupEntry(
    string Id,
    string Name,
    string Command,
    string Publisher,
    string Source,
    string RegistryView,
    string KeyPath,
    string ValueKind,
    bool IsEnabled,
    bool CanModify)
{
    /// <summary>
    /// Le viste 32/64 bit e WMI possono descrivere la stessa app. La riga mostrata
    /// all'utente conserva qui le sorgenti originali così le azioni restano coerenti.
    /// </summary>
    public IReadOnlyList<StartupEntry> Variants { get; init; } = [];
}

/// <summary>
/// Gestore Run/RunOnce. Le voci utente si possono disabilitare solo dopo averne
/// salvato una copia in config; le voci macchina restano informative senza admin.
/// </summary>
public sealed class StartupService
{
    private static readonly string[] KeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
    ];
    private const string StartupApprovedRoot =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly FileLogService _log;

    public StartupService(AppConfig config, ConfigStore store, FileLogService log)
    {
        _config = config;
        _store = store;
        _log = log;
    }

    public IReadOnlyList<StartupEntry> Collect()
    {
        var result = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        ReadHive(result, RegistryHive.CurrentUser, RegistryView.Registry64, canModify: true, "Utente");
        ReadHive(result, RegistryHive.CurrentUser, RegistryView.Registry32, canModify: true, "Utente");
        ReadHive(result, RegistryHive.LocalMachine, RegistryView.Registry64, canModify: false, "Sistema");
        ReadHive(result, RegistryHive.LocalMachine, RegistryView.Registry32, canModify: false, "Sistema");
        ReadStartupTasks(result);
        ReadWmiStartupCommands(result);

        foreach (var disabled in _config.DisabledStartupEntries)
        {
            var id = BuildId(disabled.RegistryView, disabled.KeyPath, disabled.Name);
            if (result.TryGetValue(id, out var existing))
            {
                result[id] = existing with
                {
                    IsEnabled = false,
                    Source = existing.Source.Contains("disabilitata", StringComparison.CurrentCultureIgnoreCase)
                        ? existing.Source
                        : $"{existing.Source} · disabilitata",
                };
            }
            else
            {
                result[id] = BuildEntry(id, disabled.Name, disabled.Command,
                    $"Utente · disabilitata il {disabled.DisabledAtUtc.ToLocalTime():g}",
                    disabled.RegistryView, disabled.KeyPath, disabled.ValueKind,
                    isEnabled: false, canModify: true);
            }
        }

        return CollapseDuplicates(result.Values)
            .OrderByDescending(entry => entry.IsEnabled)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Disable(StartupEntry entry)
    {
        var variants = VariantsFor(entry)
            .Where(item => item.CanModify && item.IsEnabled)
            .ToList();
        if (variants.Count == 0)
            throw new InvalidOperationException(Locale.T("startup.err.nodisable"));

        foreach (var variant in variants)
            DisableSingle(variant);
    }

    private void DisableSingle(StartupEntry entry)
    {

        var view = ParseView(entry.RegistryView);
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = baseKey.OpenSubKey(entry.KeyPath, writable: true)
                        ?? throw new InvalidOperationException(Locale.T("startup.err.missing"));
        var current = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        if (!string.Equals(current, entry.Command, StringComparison.Ordinal))
            throw new InvalidOperationException(Locale.T("startup.err.changed"));

        var backup = new DisabledStartupEntry
        {
            RegistryView = entry.RegistryView,
            KeyPath = entry.KeyPath,
            Name = entry.Name,
            Command = entry.Command,
            ValueKind = entry.ValueKind,
            DisabledAtUtc = DateTime.UtcNow,
        };
        _config.DisabledStartupEntries.RemoveAll(item => Same(item, backup));
        _config.DisabledStartupEntries.Add(backup);
        _store.Save(_config);

        try
        {
            // Windows mantiene il comando e registra lo stato in StartupApproved.
            // In questo modo Gestione attività e Nexus mostrano la stessa voce.
            SetStartupApprovedState(view, entry.Name, enabled: false);
            _log.Info($"Voce avvio disabilitata con backup: {entry.Name}");
        }
        catch
        {
            _config.DisabledStartupEntries.RemoveAll(item => Same(item, backup));
            _store.Save(_config);
            throw;
        }
    }

    public void Enable(StartupEntry entry)
    {
        var variants = VariantsFor(entry)
            .Where(item => item.CanModify && !item.IsEnabled)
            .ToList();
        if (variants.Count == 0)
            throw new InvalidOperationException(Locale.T("startup.err.noenable"));

        foreach (var variant in variants)
            EnableSingle(variant);
    }

    private void EnableSingle(StartupEntry entry)
    {

        var backup = _config.DisabledStartupEntries.FirstOrDefault(item =>
            BuildId(item.RegistryView, item.KeyPath, item.Name).Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
            ;
        var registryView = backup?.RegistryView ?? entry.RegistryView;
        var keyPath = backup?.KeyPath ?? entry.KeyPath;
        var name = backup?.Name ?? entry.Name;
        var command = backup?.Command ?? entry.Command;
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException(Locale.T("startup.err.nocommand"));
        var view = ParseView(registryView);
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = baseKey.CreateSubKey(keyPath, writable: true)
                        ?? throw new InvalidOperationException(Locale.T("startup.err.registry"));
        var existing = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        if (existing is null)
            key.SetValue(name, command, ParseValueKind(backup?.ValueKind ?? entry.ValueKind));
        else if (backup is not null && !string.Equals(existing, command, StringComparison.Ordinal))
            throw new InvalidOperationException(Locale.T("startup.err.changed"));
        SetStartupApprovedState(view, name, enabled: true);
        if (backup is not null) _config.DisabledStartupEntries.Remove(backup);
        _store.Save(_config);
        _log.Info($"Voce avvio ripristinata: {entry.Name}");
    }

    public static string ExtractExecutablePath(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command ?? "").Trim();
        if (expanded.Length == 0) return "";
        if (expanded[0] == '"')
        {
            var closing = expanded.IndexOf('"', 1);
            return closing > 1 ? expanded[1..closing] : expanded.Trim('"');
        }
        var exeEnd = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0) return expanded[..(exeEnd + 4)].Trim();
        return expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    private void ReadHive(Dictionary<string, StartupEntry> target, RegistryHive hive,
        RegistryView view, bool canModify, string scope)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            foreach (var keyPath in KeyPaths)
            {
                using var key = baseKey.OpenSubKey(keyPath, writable: false);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw is not string command || string.IsNullOrWhiteSpace(command)) continue;
                    var valueKind = key.GetValueKind(name).ToString();
                    var viewName = view.ToString();
                    var id = BuildId(viewName, keyPath, name);
                    // Task Manager può salvare StartupApproved in un hive/view
                    // diverso da quello del comando (caso comune per app 32 bit).
                    // Consideriamo tutte le copie: una disabilitazione esplicita
                    // (byte 3) prevale sempre sul fallback attivo.
                    var approved = ReadAnyStartupApprovedState(name);
                    target.TryAdd(id, BuildEntry(id, name, command,
                        $"{scope} · Registro {(view == RegistryView.Registry64 ? "64" : "32")} bit",
                        viewName, keyPath, valueKind, isEnabled: approved ?? true, canModify));
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Lettura startup {scope}/{view} parziale: {ex.Message}");
        }
    }

    private void ReadWmiStartupCommands(Dictionary<string, StartupEntry> target)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Command, Location, User FROM Win32_StartupCommand");
            foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
            {
                var name = item["Name"]?.ToString()?.Trim();
                var command = item["Command"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command)) continue;
                var location = item["Location"]?.ToString()?.Trim() ?? "Startup command";
                if (target.Values.Any(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && existing.Command.Equals(command, StringComparison.Ordinal))) continue;

                var approved = ReadAnyStartupApprovedState(name);
                var isUser = location.Contains("HKCU", StringComparison.OrdinalIgnoreCase)
                    || location.Contains("Startup", StringComparison.OrdinalIgnoreCase)
                    && item["User"]?.ToString()?.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) == true;
                var id = $"WMI|{location}|{name}";
                target.TryAdd(id, BuildEntry(id, name, command,
                    $"{(isUser ? "Utente" : "Sistema")} · {location}",
                    "WMI", location, RegistryValueKind.String.ToString(),
                    isEnabled: approved ?? true, canModify: false));
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Lettura startup WMI parziale: {ex.Message}");
        }
    }

    /// <summary>
    /// Le app Store/UWP (Phone Link, To Do, Teams, ecc.) non usano sempre Run:
    /// Gestione attività le registra come StartupTask sotto StartupApproved.
    /// Le mostriamo con lo stato reale, lasciandole informative finché non
    /// abbiamo un comando ripristinabile per quel pacchetto.
    /// </summary>
    private static void ReadStartupTasks(Dictionary<string, StartupEntry> target)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey($"{StartupApprovedRoot}\\StartupTask", writable: false);
                    if (key is null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        var state = ReadStartupApprovedState(hive, view,
                            $"{StartupApprovedRoot}\\StartupTask", name);
                        var scope = hive == RegistryHive.CurrentUser ? "Utente" : "Sistema";
                        var id = $"StartupTask|{hive}|{view}|{name}";
                        var displayName = FriendlyStartupTaskName(name);
                        target.TryAdd(id, new StartupEntry(
                            id, displayName, $"StartupTask: {name}", "—",
                            $"{scope} · StartupTask", view.ToString(),
                            $"{StartupApprovedRoot}\\StartupTask", "Binary",
                            state ?? true, CanModify: false));
                    }
                }
                catch (Exception) { /* registro opzionale: non bloccare la pagina */ }
            }
        }
    }

    private static string FriendlyStartupTaskName(string value)
    {
        var separator = value.IndexOf('!');
        if (separator > 0 && separator < value.Length - 1)
        {
            var task = value[(separator + 1)..];
            if (!task.Equals("App", StringComparison.OrdinalIgnoreCase)) return task;
            return value[..separator];
        }
        return value;
    }

    private static bool? ReadAnyStartupApprovedState(string name)
    {
        bool? result = null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                var state = ReadStartupApprovedState(hive, view, name);
                if (state == false) return false;
                if (state == true) result = true;
            }
        }
        return result;
    }

    private static bool? ReadStartupApprovedState(RegistryHive hive, RegistryView view, string name)
        => ReadStartupApprovedState(hive, view, ApprovedPath(view), name);

    private static bool? ReadStartupApprovedState(RegistryHive hive, RegistryView view,
        string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not byte[] bytes
                || bytes.Length < 4) return null;
            return bytes[0] == 3 ? false : bytes[0] == 2 ? true : null;
        }
        catch (Exception) { return null; }
    }

    private static void SetStartupApprovedState(RegistryView view, string name, bool enabled)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = baseKey.CreateSubKey(ApprovedPath(view), writable: true)
                        ?? throw new InvalidOperationException("Impossibile aprire StartupApproved.");
        var current = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as byte[];
        var bytes = current is { Length: >= 12 } ? (byte[])current.Clone() : new byte[12];
        bytes[0] = enabled ? (byte)2 : (byte)3;
        bytes[1] = bytes[2] = bytes[3] = 0;
        key.SetValue(name, bytes, RegistryValueKind.Binary);
    }

    private static string ApprovedPath(RegistryView view)
        => $"{StartupApprovedRoot}\\{(view == RegistryView.Registry32 ? "Run32" : "Run")}";

    private static StartupEntry BuildEntry(string id, string name, string command, string source,
        string view, string keyPath, string valueKind, bool isEnabled, bool canModify)
    {
        var executable = ExtractExecutablePath(command);
        var publisher = "—";
        if (File.Exists(executable))
        {
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(executable).CompanyName;
                if (string.IsNullOrWhiteSpace(publisher)) publisher = "—";
            }
            catch (Exception) { publisher = "—"; }
        }
        return new StartupEntry(id, name, command, publisher, source, view, keyPath,
            valueKind, isEnabled, canModify);
    }

    private static string BuildId(string view, string keyPath, string name) => $"{view}|{keyPath}|{name}";
    private static RegistryView ParseView(string view)
        => view.Equals(nameof(RegistryView.Registry32), StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32 : RegistryView.Registry64;
    private static RegistryValueKind ParseValueKind(string valueKind)
        => valueKind.Equals(nameof(RegistryValueKind.ExpandString), StringComparison.OrdinalIgnoreCase)
            ? RegistryValueKind.ExpandString : RegistryValueKind.String;
    private static IReadOnlyList<StartupEntry> CollapseDuplicates(IEnumerable<StartupEntry> entries)
    {
        return entries
            .GroupBy(LogicalKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var variants = group.ToList();
                if (variants.Count == 1) return variants[0];

                // Preferiamo una voce utente modificabile; a parità scegliamo la
                // 64 bit e infine una voce con stato attivo (l'avvio effettivo è
                // attivo se almeno una delle sorgenti lo è).
                var representative = variants
                    .OrderByDescending(item => item.CanModify)
                    .ThenByDescending(item => item.IsEnabled)
                    .ThenByDescending(item => item.RegistryView.Equals(nameof(RegistryView.Registry64), StringComparison.OrdinalIgnoreCase))
                    .ThenBy(item => SourceRank(item.Source))
                    .First();

                return representative with
                {
                    IsEnabled = variants.Any(item => item.IsEnabled),
                    CanModify = variants.Any(item => item.CanModify),
                    Source = MergeSource(variants),
                    Variants = variants,
                };
            })
            .ToList();
    }

    private static string LogicalKey(StartupEntry entry)
    {
        var scope = entry.Source.StartsWith("Sistema", StringComparison.OrdinalIgnoreCase)
            ? "system"
            : "user";
        return $"{scope}|{entry.Name.Trim()}";
    }

    private static IReadOnlyList<StartupEntry> VariantsFor(StartupEntry entry)
        => entry.Variants.Count == 0 ? [entry] : entry.Variants;

    private static int SourceRank(string source)
        => source.Contains("Registro", StringComparison.OrdinalIgnoreCase) ? 0
            : source.Contains("StartupTask", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

    private static string MergeSource(IReadOnlyList<StartupEntry> variants)
    {
        var scope = variants.Any(item => item.Source.StartsWith("Sistema", StringComparison.OrdinalIgnoreCase))
            ? "Sistema"
            : "Utente";
        var registryBits = variants
            .Where(item => item.Source.Contains("Registro", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RegistryView.Equals(nameof(RegistryView.Registry32), StringComparison.OrdinalIgnoreCase) ? "32" : "64")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parts = new List<string>();
        if (registryBits.Count > 0)
            parts.Add($"Registro {string.Join("/", registryBits)} bit");
        if (variants.Any(item => item.Source.Contains("StartupTask", StringComparison.OrdinalIgnoreCase)))
            parts.Add("StartupTask");
        if (variants.Any(item => item.Source.StartsWith("WMI", StringComparison.OrdinalIgnoreCase)
            || item.Source.Contains("WMI", StringComparison.OrdinalIgnoreCase)))
            parts.Add("WMI");
        return parts.Count == 0 ? variants[0].Source : $"{scope} · {string.Join(" + ", parts)}";
    }

    private static bool Same(DisabledStartupEntry left, DisabledStartupEntry right)
        => BuildId(left.RegistryView, left.KeyPath, left.Name)
            .Equals(BuildId(right.RegistryView, right.KeyPath, right.Name), StringComparison.OrdinalIgnoreCase);
}
