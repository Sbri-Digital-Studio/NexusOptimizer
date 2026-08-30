using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>Programma installato, come lo dichiara Windows nel Registro.</summary>
public sealed record InstalledApp(
    string Id,
    string Name,
    string Publisher,
    string Version,
    DateTime? InstalledOn,
    long? SizeBytes,
    string InstallLocation,
    string UninstallCommand,
    string QuietUninstallCommand,
    string IconSource,
    bool IsUserScope,
    bool Is64Bit)
{
    /// <summary>Una voce senza comando di disinstallazione non è azionabile da qui.</summary>
    public bool CanUninstall => UninstallCommand.Length > 0 || QuietUninstallCommand.Length > 0;
}

/// <summary>
/// Inventario dei programmi installati letto dalle chiavi Uninstall del Registro
/// (le stesse usate da "App e funzionalità"), nelle viste a 64 e 32 bit e sia per
/// la macchina sia per l'utente corrente.
///
/// La disinstallazione **non viene eseguita da Nexus**: si avvia il programma di
/// disinstallazione fornito dall'autore del software, esattamente come farebbe
/// Windows. Nessun file viene rimosso da noi, nessuna chiave viene ripulita a
/// mano: cancellare i resti di un'installazione senza conoscerla è il modo più
/// rapido per rompere un programma che funzionava.
/// </summary>
public sealed class InstalledAppsService(FileLogService log)
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Voci che Windows stesso nasconde in "App e funzionalità".</summary>
    private static readonly string[] IgnoredReleaseTypes =
        ["Security Update", "Update Rollup", "Hotfix", "ServicePack", "Update"];

    private readonly FileLogService _log = log;

    public IReadOnlyList<InstalledApp> Collect()
    {
        var found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        Read(found, RegistryHive.LocalMachine, RegistryView.Registry64, userScope: false, is64: true);
        Read(found, RegistryHive.LocalMachine, RegistryView.Registry32, userScope: false, is64: false);
        Read(found, RegistryHive.CurrentUser, RegistryView.Registry64, userScope: true, is64: true);
        Read(found, RegistryHive.CurrentUser, RegistryView.Registry32, userScope: true, is64: false);

        return [.. found.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private void Read(Dictionary<string, InstalledApp> result, RegistryHive hive, RegistryView view,
                      bool userScope, bool is64)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null) return;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry is null) continue;
                    var app = Parse(entry, name, userScope, is64);
                    if (app is not null) result.TryAdd(KeyOf(app), app);
                }
                catch (Exception)
                {
                    // Una voce illeggibile non deve interrompere l'inventario.
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Lettura programmi installati non riuscita ({hive}/{view})", ex);
        }
    }

    /// <summary>Stesso nome e stessa versione in due viste = stesso programma.</summary>
    private static string KeyOf(InstalledApp app) => app.Name + "|" + app.Version;

    private static InstalledApp? Parse(RegistryKey entry, string id, bool userScope, bool is64)
    {
        var name = Str(entry, "DisplayName");
        if (name.Length == 0) return null;

        // Componenti di sistema, aggiornamenti e voci figlie: Windows stesso non
        // le elenca fra le app, e non sono disinstallabili singolarmente.
        if (Int(entry, "SystemComponent") == 1) return null;
        if (Str(entry, "ParentKeyName").Length > 0) return null;
        var releaseType = Str(entry, "ReleaseType");
        if (IgnoredReleaseTypes.Contains(releaseType, StringComparer.OrdinalIgnoreCase)) return null;

        var sizeKb = Int(entry, "EstimatedSize");
        return new InstalledApp(
            id,
            name,
            Str(entry, "Publisher"),
            Str(entry, "DisplayVersion"),
            ParseInstallDate(Str(entry, "InstallDate")),
            sizeKb > 0 ? sizeKb * 1024L : null,
            Str(entry, "InstallLocation"),
            Str(entry, "UninstallString"),
            Str(entry, "QuietUninstallString"),
            Str(entry, "DisplayIcon"),
            userScope,
            is64);
    }

    private static DateTime? ParseInstallDate(string value)
        => DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;

    private static string Str(RegistryKey key, string name)
    {
        try { return key.GetValue(name)?.ToString()?.Trim() ?? ""; }
        catch (Exception) { return ""; }
    }

    private static int Int(RegistryKey key, string name)
    {
        try { return key.GetValue(name) is int value ? value : 0; }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// Avvia il disinstallatore del programma. Restituisce false quando la voce
    /// non ne dichiara uno: in quel caso l'unica strada onesta è rimandare a
    /// "App e funzionalità" di Windows, non improvvisare una rimozione.
    /// </summary>
    public bool TryStartUninstall(InstalledApp app, out string failure)
    {
        failure = "";
        var command = app.UninstallCommand.Length > 0 ? app.UninstallCommand : app.QuietUninstallCommand;
        if (command.Length == 0)
        {
            failure = "no-command";
            return false;
        }

        try
        {
            var (file, arguments) = SplitCommand(command);
            if (file.Length == 0)
            {
                failure = "no-command";
                return false;
            }

            _log.Info($"Disinstallazione avviata: {app.Name} ({file})");
            Process.Start(new ProcessStartInfo(file, arguments)
            {
                // ShellExecute: l'eventuale richiesta di elevazione la fa il
                // disinstallatore stesso, con il suo prompt UAC.
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Disinstallazione non avviata: {app.Name}", ex);
            failure = "start-failed";
            return false;
        }
    }

    /// <summary>
    /// Separa eseguibile e argomenti di un UninstallString. Il caso MSI viene
    /// normalizzato: "/I{GUID}" indica il prodotto, ma per rimuoverlo serve "/X".
    /// </summary>
    internal static (string File, string Arguments) SplitCommand(string command)
    {
        var text = command.Trim();
        if (text.Length == 0) return ("", "");

        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 0)
                return (text[1..end], text[(end + 1)..].Trim());
        }

        // MsiExec.exe /I{GUID} oppure /X{GUID}, con o senza spazio.
        var msi = text.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
        if (msi >= 0)
        {
            var rest = text[(msi + "msiexec".Length)..].TrimStart();
            if (rest.StartsWith(".exe", StringComparison.OrdinalIgnoreCase)) rest = rest[4..].TrimStart();
            if (rest.StartsWith("/I", StringComparison.OrdinalIgnoreCase))
                rest = "/X" + rest[2..];
            return ("msiexec.exe", rest.Trim());
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0 && exe + 4 <= text.Length)
            return (text[..(exe + 4)], text[(exe + 4)..].Trim());

        return (text, "");
    }

    /// <summary>Apre la cartella di installazione quando il percorso esiste davvero.</summary>
    public static bool TryOpenLocation(InstalledApp app)
    {
        try
        {
            if (app.InstallLocation.Length == 0 || !Directory.Exists(app.InstallLocation)) return false;
            Process.Start(new ProcessStartInfo(app.InstallLocation) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Pagina "App installate" delle Impostazioni di Windows.</summary>
    public static void OpenWindowsAppsPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
        }
        catch (Exception) { /* la shell puo' non gestire l'URI: nessun danno */ }
    }
}
