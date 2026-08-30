using System.Diagnostics;
using System.Globalization;
using System.Management;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>Driver di periferica come dichiarato da Windows (Win32_PnPSignedDriver).</summary>
public sealed record DeviceDriver(
    string Device,
    string DeviceClass,
    string Provider,
    string Version,
    DateTime? Date,
    bool IsSigned,
    string HardwareId,
    string RawClass)
{
    /// <summary>Codice di errore di Gestione dispositivi, quando la periferica non funziona.</summary>
    public int ProblemCode { get; init; }

    public bool HasProblem => ProblemCode != 0;
}

/// <summary>Aggiornamento driver annunciato da Windows Update.</summary>
public sealed record DriverUpdate(string Title, string Provider, long SizeBytes);

public enum DriverSearchStatus
{
    /// <summary>Ricerca non avviata: la funzione è disattivata nelle impostazioni.</summary>
    Disabled,

    UpToDate,
    UpdatesAvailable,

    /// <summary>Windows Update non ha risposto: nessuna ipotesi al posto suo.</summary>
    Failed,
}

public sealed record DriverSearchResult(DriverSearchStatus Status, IReadOnlyList<DriverUpdate> Updates)
{
    public static readonly DriverSearchResult Disabled = new(DriverSearchStatus.Disabled, []);
}

/// <summary>
/// Inventario dei driver installati e ricerca aggiornamenti.
///
/// La ricerca **non usa un catalogo di terze parti**: interroga Windows Update,
/// che conosce l'hardware e la matrice di compatibilità meglio di qualunque
/// classifica di versioni. Nexus non scarica e non installa driver: mostra cosa
/// Windows propone e apre Windows Update, che resta l'unico a decidere e a poter
/// annullare l'operazione. Installare un driver sbagliato è uno dei pochi modi
/// per rendere un PC non avviabile, e non è un rischio che vale la pena correre
/// per qualche numero di versione.
/// </summary>
public sealed class DriverService(FileLogService log)
{
    private readonly FileLogService _log = log;

    /// <summary>Elenco completo dei driver di periferica, con eventuali errori.</summary>
    public IReadOnlyList<DeviceDriver> Collect()
    {
        var problems = ReadProblemDevices();
        var drivers = new List<DeviceDriver>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate, " +
                "IsSigned, HardWareID, DeviceID FROM Win32_PnPSignedDriver");
            foreach (var mo in searcher.Get().Cast<ManagementBaseObject>())
            {
                using (mo)
                {
                    var device = Str(mo, "DeviceName");
                    if (device.Length == 0) continue;
                    var deviceId = Str(mo, "DeviceID");
                    drivers.Add(new DeviceDriver(
                        device,
                        Describe(Str(mo, "DeviceClass")),
                        Str(mo, "DriverProviderName"),
                        Str(mo, "DriverVersion"),
                        ParseWmiDate(Str(mo, "DriverDate")),
                        Str(mo, "IsSigned").Equals("True", StringComparison.OrdinalIgnoreCase),
                        Str(mo, "HardWareID"),
                        Str(mo, "DeviceClass").ToUpperInvariant())
                    {
                        ProblemCode = problems.GetValueOrDefault(deviceId),
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Inventario driver non riuscito", ex);
        }

        return [.. drivers
            .OrderByDescending(d => d.HasProblem)
            .ThenBy(d => d.DeviceClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Periferiche con un codice di errore in Gestione dispositivi.</summary>
    private Dictionary<string, int> ReadProblemDevices()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            foreach (var mo in searcher.Get().Cast<ManagementBaseObject>())
            {
                using (mo)
                {
                    var id = Str(mo, "DeviceID");
                    if (id.Length == 0) continue;
                    if (mo["ConfigManagerErrorCode"] is not null
                        && int.TryParse(mo["ConfigManagerErrorCode"].ToString(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var code))
                        result[id] = code;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Lettura periferiche con problemi non riuscita", ex);
        }
        return result;
    }

    /// <summary>
    /// Chiede a Windows Update quali driver sono disponibili per questo PC.
    /// È una chiamata di rete: parte solo su richiesta esplicita o con il
    /// controllo automatico attivato dall'utente. Non scarica e non installa nulla.
    /// </summary>
    public Task<DriverSearchResult> SearchUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try
            {
                var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (sessionType is null) return new DriverSearchResult(DriverSearchStatus.Failed, []);

                dynamic session = Activator.CreateInstance(sessionType)!;
                dynamic searcher = session.CreateUpdateSearcher();
                searcher.Online = true;
                // Sintassi documentata dell'agente Windows Update: solo driver non installati.
                dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");

                var updates = new List<DriverUpdate>();
                foreach (dynamic update in result.Updates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string title = update.Title ?? "";
                    if (title.Length == 0) continue;
                    long size = 0;
                    try { size = (long)update.MaxDownloadSize; } catch (Exception) { }
                    var provider = "";
                    try { provider = update.DriverProvider ?? ""; } catch (Exception) { }
                    updates.Add(new DriverUpdate(title, provider, size));
                }

                _log.Info($"Ricerca driver su Windows Update: {updates.Count} disponibili.");
                return new DriverSearchResult(
                    updates.Count == 0 ? DriverSearchStatus.UpToDate : DriverSearchStatus.UpdatesAvailable,
                    updates);
            }
            catch (OperationCanceledException)
            {
                return new DriverSearchResult(DriverSearchStatus.Failed, []);
            }
            catch (Exception ex)
            {
                // Servizio disattivato, criteri aziendali, rete assente: si dichiara
                // il fallimento invece di far credere che sia tutto aggiornato.
                _log.Error("Ricerca driver su Windows Update non riuscita", ex);
                return new DriverSearchResult(DriverSearchStatus.Failed, []);
            }
        }, cancellationToken);

    public static void OpenDeviceManager() => Launch("devmgmt.msc");

    public static void OpenWindowsUpdate() => Launch("ms-settings:windowsupdate");

    /// <summary>
    /// Pagina ufficiale del produttore per i driver, quando il fornitore è
    /// riconoscibile. È la strada consigliata dal progetto: la fonte originale,
    /// non un archivio di terze parti.
    /// </summary>
    public static string? VendorPageFor(DeviceDriver driver)
    {
        var text = (driver.Provider + " " + driver.Device).ToLowerInvariant();
        if (text.Contains("nvidia", StringComparison.Ordinal)) return "https://www.nvidia.com/download/index.aspx";
        if (text.Contains("advanced micro devices", StringComparison.Ordinal)
            || text.Contains("amd", StringComparison.Ordinal)) return "https://www.amd.com/support";
        if (text.Contains("intel", StringComparison.Ordinal)) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (text.Contains("realtek", StringComparison.Ordinal)) return "https://www.realtek.com/downloads";
        if (text.Contains("logitech", StringComparison.Ordinal)) return "https://support.logi.com/hc/articles/360025298053";
        if (text.Contains("qualcomm", StringComparison.Ordinal)) return "https://www.qualcomm.com/support";
        if (text.Contains("mediatek", StringComparison.Ordinal)) return "https://www.mediatek.com/support";
        return null;
    }

    internal static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception) { /* la shell puo' rifiutare: nessun danno collaterale */ }
    }

    /// <summary>Classi PnP con un nome leggibile; le altre restano come le dichiara Windows.</summary>
    private static string Describe(string deviceClass) => deviceClass.ToUpperInvariant() switch
    {
        "DISPLAY" => Locale.T("drv.class.display"),
        "NET" => Locale.T("drv.class.net"),
        "MEDIA" => Locale.T("drv.class.audio"),
        "SYSTEM" => Locale.T("drv.class.system"),
        "USB" => "USB",
        "HIDCLASS" or "HID" => Locale.T("drv.class.input"),
        "KEYBOARD" => Locale.T("drv.class.keyboard"),
        "MOUSE" => Locale.T("drv.class.mouse"),
        "PRINTER" => Locale.T("drv.class.printer"),
        "DISKDRIVE" or "SCSIADAPTER" or "HDC" => Locale.T("drv.class.storage"),
        "BLUETOOTH" => "Bluetooth",
        "MONITOR" => Locale.T("drv.class.monitor"),
        "BATTERY" => Locale.T("drv.class.battery"),
        "PROCESSOR" => Locale.T("drv.class.cpu"),
        "FIRMWARE" => "Firmware",
        "" => Locale.T("drv.class.other"),
        _ => deviceClass,
    };

    /// <summary>
    /// Icona e colore per classe di periferica: l'occhio riconosce prima una
    /// scheda video da un'icona che da una parola in mezzo a duecento righe.
    /// </summary>
    public static (string Kind, string Color) VisualFor(string rawClass) => rawClass switch
    {
        "DISPLAY" => ("gpu", "#6BD93D"),
        "NET" => ("globe", "#57C7FF"),
        "MEDIA" or "AUDIOENDPOINT" => ("music", "#B79CFF"),
        "MONITOR" => ("monitor", "#57C7FF"),
        "DISKDRIVE" or "SCSIADAPTER" or "HDC" or "VOLUME" => ("disk", "#36D4A8"),
        "KEYBOARD" or "HIDCLASS" or "HID" or "MOUSE" => ("keyboard", "#FFB454"),
        "USB" => ("chip", "#FF78A5"),
        "PROCESSOR" => ("cpuMini", "#FF9E64"),
        "BATTERY" => ("bolt", "#E6A23C"),
        "SYSTEM" or "COMPUTER" or "FIRMWARE" => ("motherboard", "#A8B5C7"),
        "PRINTER" or "PRINTQUEUE" => ("toolbox", "#D0A3FF"),
        "BLUETOOTH" => ("cloud", "#74B7FF"),
        "SOFTWARECOMPONENT" or "SOFTWAREDEVICE" => ("apps", "#9AA6B2"),
        _ => ("chip", "#9AA6B2"),
    };

    private static string Str(ManagementBaseObject mo, string name)
    {
        try { return mo[name]?.ToString()?.Trim() ?? ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>Data WMI (yyyyMMdd...) senza dipendere dal fuso dichiarato.</summary>
    private static DateTime? ParseWmiDate(string value)
    {
        if (value.Length < 8) return null;
        return DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;
    }
}
