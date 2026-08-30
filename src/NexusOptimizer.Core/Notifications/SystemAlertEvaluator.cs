using System.Globalization;
using NexusOptimizer.Core.Configuration;

namespace NexusOptimizer.Core.Notifications;

/// <summary>Spazio di un volume fisso, gia' misurato dal chiamante.</summary>
public sealed record DriveSpaceReading(string Name, double FreePercent, long FreeBytes);

/// <summary>
/// Letture periodiche (ogni pochi minuti): dischi, spazio recuperabile e voci di
/// avvio comparse dopo la baseline. Campi vuoti = misura non disponibile, mai zero.
/// </summary>
public sealed record PeriodicReadings
{
    public IReadOnlyList<DriveSpaceReading> Drives { get; init; } = [];
    public long? RecoverableBytes { get; init; }
    public IReadOnlyList<string> NewStartupEntries { get; init; } = [];
}

/// <summary>
/// Traduce misure reali in avvisi, con isteresi e cooldown perché un avviso non
/// diventi un assillo. Nessuna dipendenza da WPF o da Windows: è la parte
/// verificabile dai test, dove il tempo viene passato dal chiamante.
///
/// Ogni regola è governata dal rispettivo interruttore in <see cref="AppConfig"/>:
/// con l'interruttore spento la regola non viene nemmeno valutata.
/// </summary>
public sealed class SystemAlertEvaluator
{
    // --- soglie (documentate in docs/NOTIFICHE.md) ---
    public const double LowDiskCriticalPercent = 5;
    public const double DiskRearmMarginPercent = 3;
    public const double CpuWarningCelsius = 85;
    public const double CpuCriticalCelsius = 95;
    public const double GpuWarningCelsius = 87;
    public const double GpuCriticalCelsius = 96;
    public const double TemperatureRearmMarginCelsius = 6;

    /// <summary>
    /// Campioni consecutivi oltre soglia prima di avvisare: a 1 Hz sono circa 15
    /// secondi. Un picco isolato durante una compilazione non è un problema termico.
    /// </summary>
    public const int SustainedSamples = 15;

    /// <summary>Sotto i 2 GiB nel Cestino l'avviso non varrebbe l'interruzione.</summary>
    public const long RecoverableThresholdBytes = 2L * 1024 * 1024 * 1024;

    public static readonly TimeSpan DiskCooldown = TimeSpan.FromHours(6);
    public static readonly TimeSpan TemperatureCooldown = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RecoverableCooldown = TimeSpan.FromDays(1);

    private sealed class RuleState
    {
        public DateTime? LastFiredUtc;
        public bool Armed = true;
        public int Streak;
    }

    private readonly AppConfig _config;
    private readonly Dictionary<string, RuleState> _rules = new(StringComparer.Ordinal);

    public SystemAlertEvaluator(AppConfig config) => _config = config;

    /// <summary>
    /// Valutazione a ogni campione del monitor (1 Hz): solo le temperature, le
    /// uniche metriche in cui conta la persistenza nel tempo.
    /// </summary>
    public IReadOnlyList<NotificationRecord> EvaluateTemperatures(
        double? cpuCelsius, double? gpuCelsius, DateTime utcNow)
    {
        if (!_config.TemperatureAlerts) return [];

        var alerts = new List<NotificationRecord>(2);
        AddTemperatureAlert(alerts, "cpu", cpuCelsius, CpuWarningCelsius, CpuCriticalCelsius,
            "notif.temp.cpu.title", "notif.temp.cpu.msg", utcNow);
        AddTemperatureAlert(alerts, "gpu", gpuCelsius, GpuWarningCelsius, GpuCriticalCelsius,
            "notif.temp.gpu.title", "notif.temp.gpu.msg", utcNow);
        return alerts;
    }

    /// <summary>Valutazione periodica: dischi, spazio recuperabile, nuove voci di avvio.</summary>
    public IReadOnlyList<NotificationRecord> EvaluatePeriodic(PeriodicReadings readings, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var alerts = new List<NotificationRecord>();

        if (_config.NotifyLowDisk)
        {
            foreach (var drive in readings.Drives)
                AddDiskAlert(alerts, drive, utcNow);
        }

        if (_config.NotifyRecoverableSpace && readings.RecoverableBytes is long bytes)
            AddRecoverableAlert(alerts, bytes, utcNow);

        if (_config.StartupMonitoring)
        {
            foreach (var entry in readings.NewStartupEntries)
                AddStartupAlert(alerts, entry, utcNow);
        }

        return alerts;
    }

    // ------------------------------------------------------------------ regole

    private void AddTemperatureAlert(List<NotificationRecord> alerts, string sensor, double? celsius,
                                     double warning, double critical,
                                     string titleKey, string messageKey, DateTime utcNow)
    {
        // Sensore assente su questa macchina: nessun avviso inventato.
        if (celsius is not double value) return;

        var warnKey = "temp." + sensor + ".warn";
        var critKey = "temp." + sensor + ".crit";

        // Il rientro sotto soglia (meno il margine) riarma la regola: senza isteresi
        // un valore che oscilla intorno al limite genererebbe avvisi a raffica.
        if (value < warning - TemperatureRearmMarginCelsius) Rearm(warnKey);
        if (value < critical - TemperatureRearmMarginCelsius) Rearm(critKey);

        var level = value >= critical ? critKey : value >= warning ? warnKey : null;
        ResetStreakExcept(level, warnKey, critKey);
        if (level is null) return;

        var state = State(level);
        state.Streak++;
        if (state.Streak < SustainedSamples) return;
        if (!CanFire(state, utcNow, TemperatureCooldown)) return;

        MarkFired(state, utcNow);
        alerts.Add(new NotificationRecord
        {
            Key = level,
            TitleKey = titleKey,
            MessageKey = messageKey,
            MessageArgs = [Round(value)],
            Severity = level == critKey ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            CreatedUtc = utcNow,
            TargetSectionId = "nav.performance",
        });
    }

    private void AddDiskAlert(List<NotificationRecord> alerts, DriveSpaceReading drive, DateTime utcNow)
    {
        var threshold = _config.NotifyLowDiskPercent;
        var warnKey = "disk.low:" + drive.Name;
        var critKey = "disk.crit:" + drive.Name;

        if (drive.FreePercent > threshold + DiskRearmMarginPercent) Rearm(warnKey);
        if (drive.FreePercent > LowDiskCriticalPercent + DiskRearmMarginPercent) Rearm(critKey);

        var critical = drive.FreePercent <= LowDiskCriticalPercent;
        if (!critical && drive.FreePercent > threshold) return;

        var key = critical ? critKey : warnKey;
        var state = State(key);
        if (!CanFire(state, utcNow, DiskCooldown)) return;

        MarkFired(state, utcNow);
        alerts.Add(new NotificationRecord
        {
            Key = key,
            TitleKey = "notif.disk.title",
            MessageKey = critical ? "notif.disk.crit.msg" : "notif.disk.low.msg",
            MessageArgs = [drive.Name, Round(drive.FreePercent), FormatBytes(drive.FreeBytes)],
            Severity = critical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            CreatedUtc = utcNow,
            TargetSectionId = "nav.diskmanager",
        });
    }

    private void AddRecoverableAlert(List<NotificationRecord> alerts, long bytes, DateTime utcNow)
    {
        const string key = "recoverable.space";
        if (bytes < RecoverableThresholdBytes)
        {
            Rearm(key);
            return;
        }

        var state = State(key);
        if (!CanFire(state, utcNow, RecoverableCooldown)) return;

        MarkFired(state, utcNow);
        alerts.Add(new NotificationRecord
        {
            Key = key,
            TitleKey = "notif.recover.title",
            MessageKey = "notif.recover.msg",
            MessageArgs = [FormatBytes(bytes)],
            Severity = NotificationSeverity.Info,
            CreatedUtc = utcNow,
            TargetSectionId = "nav.cleancat",
        });
    }

    private void AddStartupAlert(List<NotificationRecord> alerts, string entryName, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return;
        var key = "startup.new:" + entryName;
        var state = State(key);
        // Una voce nuova si annuncia una volta sola: il cooldown e' la vita del processo.
        if (state.LastFiredUtc is not null) return;

        MarkFired(state, utcNow);
        alerts.Add(new NotificationRecord
        {
            Key = key,
            TitleKey = "notif.startup.title",
            MessageKey = "notif.startup.msg",
            MessageArgs = [entryName],
            Severity = NotificationSeverity.Warning,
            CreatedUtc = utcNow,
            TargetSectionId = "nav.startup",
        });
    }

    // ------------------------------------------------------------------ stato

    private RuleState State(string key)
    {
        if (!_rules.TryGetValue(key, out var state))
        {
            state = new RuleState();
            _rules[key] = state;
        }
        return state;
    }

    private static bool CanFire(RuleState state, DateTime utcNow, TimeSpan cooldown)
        => state.Armed || state.LastFiredUtc is not DateTime last || utcNow - last >= cooldown;

    private static void MarkFired(RuleState state, DateTime utcNow)
    {
        state.LastFiredUtc = utcNow;
        state.Armed = false;
    }

    private void Rearm(string key)
    {
        var state = State(key);
        state.Armed = true;
        state.Streak = 0;
    }

    /// <summary>Azzera la serie consecutiva dei livelli non piu' validi.</summary>
    private void ResetStreakExcept(string? activeKey, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.Equals(key, activeKey, StringComparison.Ordinal)) continue;
            State(key).Streak = 0;
        }
    }

    private static string Round(double value)
        => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formattazione locale dei byte: il Core non puo' usare il Formatter della UI,
    /// ma il testo dell'avviso deve restare leggibile quanto il resto dell'app.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var decimals = unit >= 3 && value < 100 ? 1 : 0;
        return Math.Round(value, decimals).ToString(CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
