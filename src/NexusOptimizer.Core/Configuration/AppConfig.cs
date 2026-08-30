using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusOptimizer.Core.Configuration;

/// <summary>
/// Configurazione applicativa persistente (%LOCALAPPDATA%\NexusOptimizer\config.json).
/// Telemetria disattivata di default; nessun dato personale viene raccolto.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = "auto";          // auto | dark | light

    /// <summary>Livello operativo: safe | balanced | expert (vedi AppModeLevel).</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = AppModeLevels.SafeId;

    [JsonPropertyName("accentColor")] public string AccentColor { get; set; } = "#4F8CFF";
    [JsonPropertyName("language")] public string Language { get; set; } = "it";      // it | en
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;

    [JsonPropertyName("startWithWindows")] public bool StartWithWindows { get; set; }
    [JsonPropertyName("minimizeToTray")] public bool MinimizeToTray { get; set; } = true;
    [JsonPropertyName("checkForUpdates")] public bool CheckForUpdates { get; set; }

    // --- Toggle rapidi dashboard (FASE 1) ---
    [JsonPropertyName("liveMonitoring")] public bool LiveMonitoring { get; set; } = true;
    [JsonPropertyName("quietMode")] public bool QuietMode { get; set; }
    [JsonPropertyName("gamingMode")] public bool GamingMode { get; set; }
    [JsonPropertyName("temperatureAlerts")] public bool TemperatureAlerts { get; set; } = true;
    [JsonPropertyName("startupMonitoring")] public bool StartupMonitoring { get; set; } = true;

    [JsonPropertyName("monitorIntervalMs")] public int MonitorIntervalMs { get; set; } = 1000;
    [JsonPropertyName("enableGpuMonitoring")] public bool EnableGpuMonitoring { get; set; } = true;

    [JsonPropertyName("autoCleanEnabled")] public bool AutoCleanEnabled { get; set; }
    [JsonPropertyName("autoCleanTempFiles")] public bool AutoCleanTempFiles { get; set; } = true;
    [JsonPropertyName("autoCleanRecycleBin")] public bool AutoCleanRecycleBin { get; set; }
    [JsonPropertyName("autoCleanSafeCaches")] public bool AutoCleanSafeCaches { get; set; } = true;
    [JsonPropertyName("autoCleanIntervalDays")] public int AutoCleanIntervalDays { get; set; } = 7;
    [JsonPropertyName("lastAutoCleanUtc")] public DateTime? LastAutoCleanUtc { get; set; }

    [JsonPropertyName("notifyLowDisk")] public bool NotifyLowDisk { get; set; } = true;
    [JsonPropertyName("notifyLowDiskPercent")] public double NotifyLowDiskPercent { get; set; } = 10;
    [JsonPropertyName("notifyRecoverableSpace")] public bool NotifyRecoverableSpace { get; set; }

    /// <summary>URL HTTPS del manifest di aggiornamento. Vuoto = nessun controllo, nessuna chiamata.</summary>
    [JsonPropertyName("updateFeedUrl")] public string UpdateFeedUrl { get; set; } = "";

    [JsonPropertyName("lastUpdateCheckUtc")] public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Ultima versione annunciata dal canale: evita di riavvisare per la stessa release.</summary>
    [JsonPropertyName("lastSeenUpdateVersion")] public string? LastSeenUpdateVersion { get; set; }

    /// <summary>
    /// Fotografia delle voci di avvio gia' note. Serve al monitoraggio avvio: le
    /// voci comparse dopo questa fotografia vengono segnalate una volta sola.
    /// </summary>
    [JsonPropertyName("startupBaseline")] public List<string> StartupBaseline { get; set; } = new();

    [JsonPropertyName("startupBaselineUpdatedUtc")] public DateTime? StartupBaselineUpdatedUtc { get; set; }

    /// <summary>
    /// Controllo periodico degli aggiornamenti di driver e programmi. Come il
    /// controllo della versione di Nexus e' spento di default: quando e' attivo
    /// interroga Windows Update e winget, che sono chiamate di rete.
    /// </summary>
    [JsonPropertyName("driverUpdateCheck")] public bool DriverUpdateCheck { get; set; }

    [JsonPropertyName("softwareUpdateCheck")] public bool SoftwareUpdateCheck { get; set; }

    [JsonPropertyName("lastDriverCheckUtc")] public DateTime? LastDriverCheckUtc { get; set; }

    [JsonPropertyName("lastSoftwareCheckUtc")] public DateTime? LastSoftwareCheckUtc { get; set; }

    /// <summary>Versioni gia' annunciate: lo stesso aggiornamento non avvisa due volte.</summary>
    [JsonPropertyName("announcedPackageUpdates")] public List<string> AnnouncedPackageUpdates { get; set; } = new();

    [JsonPropertyName("telemetryEnabled")] public bool TelemetryEnabled { get; set; }

    /// <summary>L'onboarding First Run è stato completato.</summary>
    [JsonPropertyName("onboardingDone")] public bool OnboardingDone { get; set; } = false;

    /// <summary>Versione dell'onboarding vista dall'utente (per mostrare una UX nuova una sola volta).</summary>
    [JsonPropertyName("onboardingVersion")] public int OnboardingVersion { get; set; }

    /// <summary>Percorsi esclusi dalla pulizia (whitelist utente).</summary>
    [JsonPropertyName("exclusions")] public List<string> Exclusions { get; set; } = new();

    /// <summary>ID categorie disattivate manualmente dall'utente.</summary>
    [JsonPropertyName("disabledCategories")] public List<string> DisabledCategories { get; set; } = new();

    /// <summary>
    /// Copia reversibile delle voci Run disabilitate dall'utente. Il comando
    /// originale viene salvato prima di rimuovere il valore dal Registro.
    /// </summary>
    [JsonPropertyName("disabledStartupEntries")]
    public List<DisabledStartupEntry> DisabledStartupEntries { get; set; } = new();

    /// <summary>
    /// Stato precedente delle ottimizzazioni applicate: senza questa memoria
    /// l'annullamento non sopravvivrebbe a un riavvio dell'applicazione.
    /// </summary>
    [JsonPropertyName("optimizerState")]
    public List<OptimizerRestoreEntry> OptimizerState { get; set; } = new();

    [JsonIgnore]
    public static AppConfig Default => new();
}

/// <summary>Valore di sistema salvato prima di un'ottimizzazione, per il ripristino.</summary>
public sealed class OptimizerRestoreEntry
{
    [JsonPropertyName("actionId")] public string ActionId { get; set; } = "";
    [JsonPropertyName("keyPath")] public string KeyPath { get; set; } = "";
    [JsonPropertyName("valueName")] public string ValueName { get; set; } = "";

    /// <summary>Valore precedente; null significa "la voce non esisteva".</summary>
    [JsonPropertyName("previousValue")] public string? PreviousValue { get; set; }

    [JsonPropertyName("valueKind")] public string ValueKind { get; set; } = "DWord";
    [JsonPropertyName("appliedAtUtc")] public DateTime AppliedAtUtc { get; set; }
}

public sealed class DisabledStartupEntry
{
    [JsonPropertyName("registryView")] public string RegistryView { get; set; } = "Registry64";
    [JsonPropertyName("keyPath")] public string KeyPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("valueKind")] public string ValueKind { get; set; } = "String";
    [JsonPropertyName("disabledAtUtc")] public DateTime DisabledAtUtc { get; set; }
}

/// <summary>Salvataggio/caricamento atomico della configurazione.</summary>
public sealed class ConfigStore
{
    private readonly string _filePath;
    private readonly string? _legacyFilePath;
    private readonly object _lock = new();

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusOptimizer");

    private static string LegacyConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusOptimizer", "config.json");

    public ConfigStore(string? directory = null)
    {
        var usesDefaultDirectory = directory is null;
        directory ??= AppDataDirectory;
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "config.json");
        _legacyFilePath = usesDefaultDirectory ? LegacyConfigPath : null;
    }

    public string FilePath => _filePath;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath) && _legacyFilePath is not null && File.Exists(_legacyFilePath))
            {
                var migrated = Read(_legacyFilePath);
                if (migrated is not null)
                {
                    Save(migrated);
                    return migrated;
                }
            }
            if (!File.Exists(_filePath)) return AppConfig.Default;
            lock (_lock)
            {
                return Read(_filePath) ?? AppConfig.Default;
            }
        }
        catch (Exception)
        {
            return AppConfig.Default;
        }
    }

    private static AppConfig? Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AppConfig>(stream);
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public void Save(AppConfig config)
    {
        lock (_lock)
        {
            var tmp = _filePath + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, config, WriteOptions);
            }
            File.Move(tmp, _filePath, overwrite: true);
        }
    }
}
