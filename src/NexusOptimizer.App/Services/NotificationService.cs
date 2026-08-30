using System.IO;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Notifications;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Collega le misure reali del PC alle regole di avviso: temperature dal
/// <see cref="SystemMonitor"/> (a ogni campione) e dischi, spazio recuperabile e
/// nuove voci di avvio su un timer lento. Nessun avviso viene inventato: se una
/// metrica non e' disponibile la regola corrispondente non viene valutata.
/// </summary>
public sealed class NotificationService : IDisposable
{
    /// <summary>Primo controllo dopo l'avvio: la finestra deve aprirsi senza concorrenza.</summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(45);

    /// <summary>Cadenza a regime: disco e cestino non cambiano al secondo.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    private readonly SystemMonitor _monitor;
    private readonly NotificationCenter _center;
    private readonly SystemAlertEvaluator _evaluator;
    private readonly StartupService _startup;
    private readonly WingetService _packages;
    private readonly DriverService _drivers;
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly FileLogService _log;
    private readonly System.Timers.Timer _timer;
    private int _checkInFlight;
    private bool _started;

    public NotificationService(SystemMonitor monitor, NotificationCenter center,
                               SystemAlertEvaluator evaluator, StartupService startup,
                               WingetService packages, DriverService drivers,
                               AppConfig config, ConfigStore store, FileLogService log)
    {
        _packages = packages;
        _drivers = drivers;
        _monitor = monitor;
        _center = center;
        _evaluator = evaluator;
        _startup = startup;
        _config = config;
        _store = store;
        _log = log;
        _timer = new System.Timers.Timer(FirstCheckDelay.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += (_, _) => RunPeriodicCheck();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _monitor.Snapshot += OnSnapshot;
        _timer.Start();
    }

    // ------------------------------------------------------------- temperature

    private void OnSnapshot(SystemSnapshot snapshot)
    {
        try
        {
            var alerts = _evaluator.EvaluateTemperatures(
                snapshot.CpuTemperatureCelsius, snapshot.GpuTemperatureCelsius, DateTime.UtcNow);
            foreach (var alert in alerts) _center.Publish(alert);
        }
        catch (Exception ex)
        {
            _log.Error("Valutazione avvisi temperatura non riuscita", ex);
        }
    }

    // ------------------------------------------------------------- controllo lento

    private void RunPeriodicCheck()
    {
        // Dalla seconda esecuzione si passa alla cadenza a regime.
        if (Math.Abs(_timer.Interval - CheckInterval.TotalMilliseconds) > 1)
            _timer.Interval = CheckInterval.TotalMilliseconds;

        if (Interlocked.Exchange(ref _checkInFlight, 1) == 1) return;
        try
        {
            var readings = new PeriodicReadings
            {
                Drives = ReadDrives(),
                RecoverableBytes = _config.NotifyRecoverableSpace ? ReadRecoverableBytes() : null,
                NewStartupEntries = _config.StartupMonitoring ? CollectNewStartupEntries() : [],
            };

            foreach (var alert in _evaluator.EvaluatePeriodic(readings, DateTime.UtcNow))
                _center.Publish(alert);

            // Aggiornamenti di programmi e driver: solo se l'utente li ha attivati,
            // al massimo una volta al giorno, e senza installare nulla.
            _ = CheckUpdatesIfDueAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Controllo periodico avvisi non riuscito", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _checkInFlight, 0);
        }
    }

    /// <summary>Volumi fissi pronti: gli unici per cui "poco spazio" significa qualcosa.</summary>
    private IReadOnlyList<DriveSpaceReading> ReadDrives()
    {
        if (!_config.NotifyLowDisk) return [];
        var drives = new List<DriveSpaceReading>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    if (drive.TotalSize <= 0) continue;
                    var freePercent = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
                    drives.Add(new DriveSpaceReading(drive.Name.TrimEnd('\\'), freePercent, drive.AvailableFreeSpace));
                }
                catch (Exception) { /* volume sparito o non interrogabile: viene ignorato */ }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Lettura volumi per gli avvisi non riuscita", ex);
        }
        return drives;
    }

    private long? ReadRecoverableBytes()
    {
        try { return RecycleBinHelper.Query()?.Bytes; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Voci di avvio comparse dopo l'ultima fotografia. Al primissimo controllo la
    /// baseline viene solo registrata: quello che c'era prima di Nexus non e' una
    /// novita' e segnalarlo sarebbe rumore.
    /// </summary>
    private IReadOnlyList<string> CollectNewStartupEntries()
    {
        try
        {
            var entries = _startup.Collect();
            var current = entries.Select(entry => entry.Id).ToList();
            var known = new HashSet<string>(_config.StartupBaseline, StringComparer.OrdinalIgnoreCase);

            if (_config.StartupBaselineUpdatedUtc is null || known.Count == 0)
            {
                SaveBaseline(current);
                return [];
            }

            var added = entries.Where(entry => !known.Contains(entry.Id)).ToList();
            if (added.Count == 0) return [];

            SaveBaseline(current);
            return added.Select(entry => entry.Name).ToList();
        }
        catch (Exception ex)
        {
            _log.Error("Monitoraggio avvio non riuscito", ex);
            return [];
        }
    }


    // ---------------------------------------------- aggiornamenti annunciati

    /// <summary>Intervallo minimo fra due controlli automatici degli aggiornamenti.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private int _updateCheckInFlight;

    private static bool IsDue(DateTime? last)
        => last is not DateTime moment || DateTime.UtcNow - moment >= UpdateCheckInterval;

    /// <summary>
    /// Controllo periodico di programmi (winget) e driver (Windows Update). Sono
    /// chiamate di rete: partono solo con l'interruttore acceso. Il risultato è
    /// un avviso, mai un'installazione.
    /// </summary>
    private async Task CheckUpdatesIfDueAsync()
    {
        if (!_config.SoftwareUpdateCheck && !_config.DriverUpdateCheck) return;
        if (Interlocked.Exchange(ref _updateCheckInFlight, 1) == 1) return;
        try
        {
            if (_config.SoftwareUpdateCheck && IsDue(_config.LastSoftwareCheckUtc))
                await AnnouncePackageUpdatesAsync();

            if (_config.DriverUpdateCheck && IsDue(_config.LastDriverCheckUtc))
                await AnnounceDriverUpdatesAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Controllo automatico aggiornamenti non riuscito", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInFlight, 0);
        }
    }

    private async Task AnnouncePackageUpdatesAsync()
    {
        var result = await _packages.ScanAsync();
        _config.LastSoftwareCheckUtc = DateTime.UtcNow;
        SaveConfig();
        if (result.Status != PackageManagerStatus.UpdatesAvailable || result.Updates.Count == 0) return;

        // La stessa versione non avvisa due volte: chi rimanda un aggiornamento
        // non deve ritrovarsi lo stesso messaggio ogni giorno.
        var known = new HashSet<string>(_config.AnnouncedPackageUpdates, StringComparer.OrdinalIgnoreCase);
        var fresh = result.Updates.Where(update => !known.Contains(update.AnnounceKey)).ToList();
        _config.AnnouncedPackageUpdates = [.. result.Updates.Select(update => update.AnnounceKey)];
        SaveConfig();
        if (fresh.Count == 0) return;

        _center.Publish(new NotificationRecord
        {
            Key = "packages.updates",
            TitleKey = "notif.pkg.title",
            MessageKey = "notif.pkg.msg",
            MessageArgs = [fresh.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), fresh[0].Name],
            Severity = NotificationSeverity.Info,
            TargetSectionId = "nav.software",
        });
    }

    private async Task AnnounceDriverUpdatesAsync()
    {
        var result = await _drivers.SearchUpdatesAsync();
        _config.LastDriverCheckUtc = DateTime.UtcNow;
        SaveConfig();
        if (result.Status != DriverSearchStatus.UpdatesAvailable || result.Updates.Count == 0) return;

        _center.Publish(new NotificationRecord
        {
            Key = "drivers.updates",
            TitleKey = "notif.drv.title",
            MessageKey = "notif.drv.msg",
            MessageArgs = [result.Updates.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)],
            Severity = NotificationSeverity.Info,
            TargetSectionId = "nav.software",
        });
    }

    private void SaveConfig()
    {
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio esito controlli aggiornamenti non riuscito", ex); }
    }

    private void SaveBaseline(List<string> ids)
    {
        _config.StartupBaseline = ids;
        _config.StartupBaselineUpdatedUtc = DateTime.UtcNow;
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio baseline avvio non riuscito", ex); }
    }

    public void Dispose()
    {
        if (_started) _monitor.Snapshot -= OnSnapshot;
        _timer.Stop();
        _timer.Dispose();
    }
}
