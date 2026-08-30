using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexusOptimizer.App.Controls;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Health;
using NexusOptimizer.Core.Updates;
// Alias per disambiguare i tipi WPF dai global using WinForms (UseWindowsForms=true nel csproj).
using Application = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace NexusOptimizer.App.ViewModels;

public sealed record DashboardQuickTool(string TitleKey, string DetailKey, string IconKind,
                                        string TargetId, WpfBrush Accent)
{
    public string Title => Locale.T(TitleKey);
    public string Detail => Locale.T(DetailKey);
}

/// <summary>
/// Riga della card Smart Clean: il peso alimenta anche l'anello proporzionale.
/// Finché la scansione non è completata il valore resta "—" (mai una stima).
/// </summary>
public sealed class CleanGroupVm(string titleKey, string iconKind, WpfBrush accent) : ObservableBase, IDonutSegment
{
    private long _bytes = -1;

    /// <summary>Il titolo vive nel dizionario: la riga non porta con sé una lingua.</summary>
    public string Title => Locale.T(titleKey);
    public string IconKind { get; } = iconKind;
    public WpfBrush Accent { get; } = accent;

    public long Bytes
    {
        get => _bytes;
        set { if (Set(ref _bytes, value)) { Raise(nameof(SizeText)); Raise(nameof(Weight)); } }
    }

    public string SizeText => _bytes < 0 ? Formatter.Dash : Formatter.Bytes(_bytes);
    public double Weight => Math.Max(0, _bytes);
    public WpfBrush SegmentBrush => Accent;
}

/// <summary>
/// ViewModel della Home: espone SOLO metriche reali misurate dal SystemMonitor,
/// da WMI e dal Cestino. Dove Windows non fornisce un dato affidabile la UI mostra
/// "n.d." o "—" (mai valori inventati).
/// </summary>
public sealed class DashboardViewModel : ObservableBase, IDisposable
{
    private readonly SystemMonitor _monitor;
    private readonly ConfigStore _store;
    private readonly AppConfig _cfg;
    private readonly FileLogService _log;
    private readonly HealthAssessmentCache _healthCache;
    private readonly SystemInfoService _systemInfo;
    private readonly OptimizerViewModel _optimizer;
    private readonly CancellationTokenSource _backgroundCts = new();
    private int _greetingHour = -1;
    private double? _gpuVramTotalBytes;

    private DashboardSystemSummary _systemSummary =
        new(Array.Empty<DashboardSystemFact>(), Formatter.Dash, Array.Empty<DashboardDiskSummary>(), null);

    public DashboardSystemSummary SystemSummary
    {
        get => _systemSummary;
        private set => Set(ref _systemSummary, value);
    }

    /// <summary>Piano di ottimizzazione condiviso con la pagina Optimizer.</summary>
    public OptimizerViewModel Optimizer => _optimizer;

    public IReadOnlyList<DashboardQuickTool> QuickTools { get; } =
    [
        new("home.tool.browser", "home.tool.browser.sub", "globe", "nav.cleancat", WpfBrushes.DeepSkyBlue),
        new("home.tool.startup", "home.tool.startup.sub", "rocket", "nav.startup", WpfBrushes.MediumSeaGreen),
        new("home.tool.duplicate", "home.tool.duplicate.sub", "copy", "nav.tools", WpfBrushes.MediumPurple),
        new("home.tool.uninstall", "home.tool.uninstall.sub", "trash", "nav.tools", WpfBrushes.IndianRed),
        new("home.tool.privacy", "home.tool.privacy.sub", "shield", "nav.privacy", WpfBrushes.MediumSeaGreen),
        new("home.tool.gaming", "home.tool.gaming.sub", "gamepad", "nav.gaming", WpfBrushes.YellowGreen),
        new("home.tool.repair", "home.tool.repair.sub", "wrench", "nav.diagnostics", WpfBrushes.DeepSkyBlue),
        new("home.tool.memory", "home.tool.memory.sub", "memory", "nav.rammanager", WpfBrushes.MediumPurple),
    ];

    // --- Serie live ---
    public SeriesPointBuffer CpuSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer RamSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer DiskSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer DownSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer UpSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer ProcessSeries { get; } = new(SystemMonitor.RingCapacity);
    public SeriesPointBuffer GpuSeries { get; } = new(SystemMonitor.RingCapacity);

    // --- Header ---
    private string _greeting = Locale.T("greet.morning");
    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }

    private string _systemSentence = Locale.T("home.sentence.reading");
    public string SystemSentence { get => _systemSentence; private set => Set(ref _systemSentence, value); }

    private string _systemStateTitle = Locale.T("home.state.reading");
    public string SystemStateTitle { get => _systemStateTitle; private set => Set(ref _systemStateTitle, value); }

    // --- CPU ---
    private string _cpuText = Formatter.Dash;
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

    private double? _cpuProgress;
    public double? CpuProgress { get => _cpuProgress; private set => Set(ref _cpuProgress, value); }

    private string _cpuTempText = Formatter.Unavailable;
    public string CpuTempText { get => _cpuTempText; private set => Set(ref _cpuTempText, value); }

    private string _cpuClockText = Formatter.Unavailable;
    public string CpuClockText { get => _cpuClockText; private set => Set(ref _cpuClockText, value); }

    private string _cpuCoreText = Formatter.Unavailable;
    public string CpuCoreText { get => _cpuCoreText; private set => Set(ref _cpuCoreText, value); }

    // --- RAM ---
    private string _ramText = Formatter.Dash;
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    private double? _ramProgress;
    public double? RamProgress { get => _ramProgress; private set => Set(ref _ramProgress, value); }

    private string _ramUsedValueText = Formatter.Dash;
    public string RamUsedValueText { get => _ramUsedValueText; private set => Set(ref _ramUsedValueText, value); }

    private string _ramAvailableValueText = Formatter.Dash;
    public string RamAvailableValueText { get => _ramAvailableValueText; private set => Set(ref _ramAvailableValueText, value); }

    private string _ramCachedText = Formatter.Unavailable;
    public string RamCachedText { get => _ramCachedText; private set => Set(ref _ramCachedText, value); }

    // --- GPU (contatori PDH "GPU Engine"/"GPU Adapter Memory") ---
    private string _gpuText = Formatter.Unavailable;
    public string GpuText { get => _gpuText; private set => Set(ref _gpuText, value); }

    private double? _gpuProgress;
    public double? GpuProgress { get => _gpuProgress; private set => Set(ref _gpuProgress, value); }

    private string _gpuVramText = Formatter.Unavailable;
    public string GpuVramText { get => _gpuVramText; private set => Set(ref _gpuVramText, value); }

    private double? _gpuVramPercent;

    /// <summary>
    /// Quota di VRAM occupata: disponibile solo quando si conosce anche il totale
    /// della scheda (NVML o registro). Senza totale la barra non compare.
    /// </summary>
    public double? GpuVramPercent { get => _gpuVramPercent; private set => Set(ref _gpuVramPercent, value); }

    private string _gpuTempText = Formatter.Unavailable;
    public string GpuTempText { get => _gpuTempText; private set => Set(ref _gpuTempText, value); }

    private string _gpuClockText = Formatter.Unavailable;
    public string GpuClockText { get => _gpuClockText; private set => Set(ref _gpuClockText, value); }

    private string _gpuPowerText = Formatter.Unavailable;
    public string GpuPowerText { get => _gpuPowerText; private set => Set(ref _gpuPowerText, value); }

    // --- Disco ---
    private string _diskText = Formatter.Dash;
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }

    private double? _diskProgress;
    public double? DiskProgress { get => _diskProgress; private set => Set(ref _diskProgress, value); }

    private string _diskReadText = Formatter.Dash;
    public string DiskReadText { get => _diskReadText; private set => Set(ref _diskReadText, value); }

    private string _diskWriteText = Formatter.Dash;
    public string DiskWriteText { get => _diskWriteText; private set => Set(ref _diskWriteText, value); }

    /// <summary>La temperatura disco richiede SMART elevato: resta dichiarata non disponibile.</summary>
    public static string DiskTempText => Formatter.Unavailable;

    // --- Rete ---
    private string _netDownText = Formatter.Dash;
    public string NetDownText { get => _netDownText; private set => Set(ref _netDownText, value); }

    private string _netUpText = Formatter.Dash;
    public string NetUpText { get => _netUpText; private set => Set(ref _netUpText, value); }

    private string _netStatusText = Formatter.Dash;
    public string NetStatusText { get => _netStatusText; private set => Set(ref _netStatusText, value); }

    private WpfBrush _netStatusBrush = WpfBrushes.Gray;
    public WpfBrush NetStatusBrush { get => _netStatusBrush; private set => Set(ref _netStatusBrush, value); }

    // --- Processi ---
    private string _procText = Formatter.Dash;
    public string ProcText { get => _procText; private set => Set(ref _procText, value); }

    private string _procUserText = Formatter.Dash;
    public string ProcUserText { get => _procUserText; private set => Set(ref _procUserText, value); }

    private string _procSystemText = Formatter.Dash;
    public string ProcSystemText { get => _procSystemText; private set => Set(ref _procSystemText, value); }

    private string _procServiceText = Formatter.Unavailable;
    public string ProcServiceText { get => _procServiceText; private set => Set(ref _procServiceText, value); }

    // --- Striscia stato ---
    private string _uptimeDaysText = Formatter.Dash;
    public string UptimeDaysText { get => _uptimeDaysText; private set => Set(ref _uptimeDaysText, value); }

    private string _uptimeClockText = "00:00:00";
    public string UptimeClockText { get => _uptimeClockText; private set => Set(ref _uptimeClockText, value); }

    private string _powerPlanText = Formatter.Unavailable;
    public string PowerPlanText { get => _powerPlanText; private set => Set(ref _powerPlanText, value); }

    // --- Smart Clean (scansione reale in background) ---
    public ObservableCollection<CleanGroupVm> CleanGroups { get; } =
    [
        new("home.clean.temp", "broom", MakeBrush(0x2F, 0x8C, 0xFF)),
        new("home.clean.appcache", "apps", MakeBrush(0x16, 0xC7, 0xC9)),
        new("home.clean.browser", "globe", MakeBrush(0xA6, 0x4C, 0xEB)),
        new("home.clean.dumps", "pulse", MakeBrush(0xF0, 0xA5, 0x00)),
        new("home.clean.bin", "trash", MakeBrush(0x7D, 0x89, 0x96)),
        new("home.clean.other", "shield", MakeBrush(0x6B, 0xD9, 0x3D)),
    ];

    private string _cleanTotalText = Formatter.Dash;
    public string CleanTotalText { get => _cleanTotalText; private set => Set(ref _cleanTotalText, value); }

    private string _cleanStatusText = "Analisi locale in avvio…";
    public string CleanStatusText { get => _cleanStatusText; private set => Set(ref _cleanStatusText, value); }

    private string _cleanFilesText = "Scansione non ancora eseguita";
    public string CleanFilesText { get => _cleanFilesText; private set => Set(ref _cleanFilesText, value); }

    // --- Spazio recuperabile (fonte verificata: cestino reale via shell API) ---
    private string _recoverableText = Formatter.Dash;
    public string RecoverableText { get => _recoverableText; private set => Set(ref _recoverableText, value); }

    private string _recoverableSub = Locale.T("home.recover.calculating");
    public string RecoverableSub { get => _recoverableSub; private set => Set(ref _recoverableSub, value); }

    private string _healthScoreText = Formatter.Dash;
    public string HealthScoreText { get => _healthScoreText; private set => Set(ref _healthScoreText, value); }

    private double? _healthScoreValue;
    public double? HealthScoreValue { get => _healthScoreValue; private set => Set(ref _healthScoreValue, value); }

    private WpfBrush _healthScoreBrush = WpfBrushes.Gray;
    public WpfBrush HealthScoreBrush { get => _healthScoreBrush; private set => Set(ref _healthScoreBrush, value); }

    private WpfColor _healthScoreColor = WpfColor.FromRgb(0x7D, 0x89, 0x96);
    public WpfColor HealthScoreColor { get => _healthScoreColor; private set => Set(ref _healthScoreColor, value); }

    private string _healthScoreSub = Locale.T("home.health.empty");
    public string HealthScoreSub { get => _healthScoreSub; private set => Set(ref _healthScoreSub, value); }
    public string OpenDiagnosticsLabel => Locale.T("home.health.open");

    // --- Azioni ---
    /// <summary>Richiesta della scansione dalla card Dashboard.</summary>
    public event Action? AnalyzeRequested;
    public event Action? DiagnosticsRequested;
    public event Action<string>? NavigationRequested;
    public ICommand AnalyzeCommand { get; }
    public ICommand OpenDiagnosticsCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand ApplyOptimizerCommand { get; }

    public static string AnalyzeTooltip =>
        "Apre Smart Clean e avvia una scansione reale.\n"
        + "Dopo l'anteprima potrai eseguire Dry Run o eliminare nel Cestino.";

    // ---- Etichette localizzate (raise in Locale.Changed) ----
#pragma warning disable CA1822 // proprietà legate in XAML: accesso a istanza richiesto
    public string OverviewTitle => Locale.T("home.overview.title");
    public string CpuSub => Locale.T("home.metric.cpu.sub");
    public string ProcessSub => Locale.T("home.metric.process.sub");
    public bool AnimationsEnabled => _cfg.Animations;
    public string LblHealth => Locale.T("home.health.title");
    public string LblRecover => Locale.T("home.recover.title");
    public string LblQuickState => Locale.T("home.quickstate.title");
    public string LblAnalyze => Locale.T("home.analyze.btn");
    public string LblLiveOn => Locale.T("home.live.on");
    public string LblLiveOff => Locale.T("home.live.off");
    public string LblLightNote => Locale.T("home.lightnote");
    public string CardCpu => Locale.T("card.cpu");
    public string CardRam => Locale.T("card.ram");
    public string CardDisk => Locale.T("card.disk");
    public string CardNet => Locale.T("card.netdown");
    public string CardProcesses => Locale.T("card.processes");
    public string TglLive => Locale.T("toggle.live");
    public string TglLiveSub => Locale.T("toggle.live.sub");
    public string TglQuiet => Locale.T("toggle.quiet");
    public string TglQuietSub => Locale.T("toggle.quiet.sub");
    public string TglGaming => Locale.T("toggle.gaming");
    public string TglGamingSub => Locale.T("toggle.gaming.sub");
    public string TglTemp => Locale.T("toggle.tempalerts");
    public string TglTempSub => Locale.T("toggle.tempalerts.sub");
    public string TglStartup => Locale.T("toggle.startupmon");
    public string TglStartupSub => Locale.T("toggle.startupmon.sub");

    /// <summary>
    /// Sotto al piano energetico mostriamo il consumo reale della GPU quando NVML
    /// lo espone: è l'unico wattaggio che Windows rende misurabile senza sensori
    /// aggiuntivi. Il totale di sistema resta dichiarato non disponibile.
    /// </summary>
    public string EnergySub => GpuPowerText == Formatter.Unavailable
        ? Locale.T("home.energy.none")
        : Locale.F("home.energy.gpu", [GpuPowerText]);
    public string SecurityStatus => Locale.T(_cfg.TelemetryEnabled ? "home.sec.optin" : "home.sec.nodata");
    public string SecuritySub => Locale.T("home.sec.sub");
    public string LocalDataStatus => Locale.T("home.localdata");

    public string AutoCleanStatus => _cfg.LastAutoCleanUtc is DateTime last
        ? Locale.F("home.autoclean.last",
            [last.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)])
        : Locale.T("home.autoclean.never");

    /// <summary>
    /// Stato reale del canale aggiornamenti: attivo senza un indirizzo configurato
    /// non controlla nulla, e dirlo "attivo" sarebbe una promessa non mantenuta.
    /// </summary>
    public string UpdatesStatus
    {
        get
        {
            if (!_cfg.CheckForUpdates) return Locale.T("home.updates.off");
            if (!UpdateChannel.IsSupportedFeed(_cfg.UpdateFeedUrl)) return Locale.T("home.updates.nochannel");
            return _cfg.LastUpdateCheckUtc is DateTime last
                ? Locale.F("home.updates.last",
                    [last.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)])
                : Locale.T("home.updates.never");
        }
    }

    /// <summary>Rialza tutte le etichette localizzate della Home.</summary>
    public void RaiseLocalized()
    {
        Raise(nameof(OverviewTitle));
        Raise(nameof(CpuSub));
        Raise(nameof(ProcessSub));
        Raise(nameof(LblHealth));
        Raise(nameof(OpenDiagnosticsLabel));
        Raise(nameof(LblRecover));
        Raise(nameof(LblQuickState));
        Raise(nameof(LblAnalyze));
        Raise(nameof(LblLightNote));
        Raise(nameof(CardCpu));
        Raise(nameof(CardRam));
        Raise(nameof(CardDisk));
        Raise(nameof(CardNet));
        Raise(nameof(CardProcesses));
        Raise(nameof(TglLive)); Raise(nameof(TglLiveSub));
        Raise(nameof(TglQuiet)); Raise(nameof(TglQuietSub));
        Raise(nameof(TglGaming)); Raise(nameof(TglGamingSub));
        Raise(nameof(TglTemp)); Raise(nameof(TglTempSub));
        Raise(nameof(TglStartup)); Raise(nameof(TglStartupSub));
        Raise(nameof(SecurityStatus));
    }
#pragma warning restore CA1822

    // --- Toggle rapidi persistiti in config.json ---
    public bool LiveMonitoring
    {
        get => _cfg.LiveMonitoring;
        set
        {
            if (_cfg.LiveMonitoring == value) return;
            _cfg.LiveMonitoring = value;
            Raise();
            ApplyMonitoring();
            Persist();
        }
    }

    public bool QuietMode { get => _cfg.QuietMode; set { if (_cfg.QuietMode == value) return; _cfg.QuietMode = value; Raise(); Persist(); } }
    public bool GamingMode { get => _cfg.GamingMode; set { if (_cfg.GamingMode == value) return; _cfg.GamingMode = value; Raise(); Persist(); } }
    public bool TemperatureAlerts { get => _cfg.TemperatureAlerts; set { if (_cfg.TemperatureAlerts == value) return; _cfg.TemperatureAlerts = value; Raise(); Persist(); } }
    public bool StartupMonitoring { get => _cfg.StartupMonitoring; set { if (_cfg.StartupMonitoring == value) return; _cfg.StartupMonitoring = value; Raise(); Persist(); } }

    private string _liveStatusLabel = "";
    public string LiveStatusLabel { get => _liveStatusLabel; private set => Set(ref _liveStatusLabel, value); }

    public DashboardViewModel(SystemMonitor monitor, ConfigStore store, AppConfig cfg, FileLogService log,
                              HealthAssessmentCache healthCache, SystemInfoService systemInfo,
                              OptimizerViewModel optimizer)
    {
        _monitor = monitor;
        _store = store;
        _cfg = cfg;
        _log = log;
        _healthCache = healthCache;
        _systemInfo = systemInfo;
        _optimizer = optimizer;
        AnalyzeCommand = new RelayCommand(_ => AnalyzeRequested?.Invoke());
        OpenDiagnosticsCommand = new RelayCommand(_ => DiagnosticsRequested?.Invoke());
        NavigateCommand = new RelayCommand(target =>
        {
            if (target is string id && id.Length > 0) NavigationRequested?.Invoke(id);
        });
        // Dalla home non si applica nulla alla cieca: si apre Optimizer, dove ogni
        // voce mostra lo stato reale e il risultato misurato dell'operazione.
        ApplyOptimizerCommand = new RelayCommand(_ => NavigationRequested?.Invoke("nav.optimizer"));

        _monitor.Snapshot += HandleSnapshot;
        _healthCache.Updated += HandleHealthAssessment;
        if (_healthCache.Current is not null) ApplyHealthAssessment(_healthCache.Current);
        UpdateLiveLabel();
        Locale.Changed += OnLocaleChanged;

        // Poll leggero del Cestino ogni 30 s (API shell documentata, costo trascurabile).
        _ = Task.Run(() => RecycleLoop(_backgroundCts.Token));
        _ = LoadDashboardSummaryAsync();
        _ = LoadPowerPlanAsync();
        // L'ispezione dell'Optimizer resta alla sua pagina: include una scansione
        // delle cache e all'avvio si sovrapporrebbe a quella di Smart Clean.
        _ = RunSmartCleanScanAsync(_backgroundCts.Token);
    }

    private async Task LoadDashboardSummaryAsync()
    {
        try
        {
            var summary = await Task.Run(_systemInfo.CollectDashboardSummary);
            Dispatch(() =>
            {
                SystemSummary = summary;
                _gpuVramTotalBytes = summary.GraphicsVramBytes;
            });
        }
        catch (Exception ex)
        {
            _log.Error("Riepilogo dashboard non disponibile", ex);
        }
    }

    private async Task LoadPowerPlanAsync()
    {
        try
        {
            var plan = await Task.Run(PowerPlanService.ReadActiveName);
            Dispatch(() => PowerPlanText = plan ?? Formatter.Unavailable);
        }
        catch (Exception ex) { _log.Warning("Piano energetico non leggibile: " + ex.Message); }
    }

    /// <summary>
    /// Scansione reale delle categorie sicure, avviata poco dopo l'apertura per non
    /// competere con il caricamento della finestra. Le categorie che richiedono
    /// privilegi vengono incluse solo se il processo è già elevato.
    /// </summary>
    private async Task RunSmartCleanScanAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
            Dispatch(() => CleanStatusText = "Analisi locale in corso…");

            var elevated = GamingModeService.IsElevated;
            var categories = CleanCatalog.Categories
                .Where(category => !category.RequiresAdmin || elevated)
                .ToArray();
            var scanner = new CleanScanner(_cfg.Exclusions);
            var result = await scanner.ScanAsync(categories, progress: null, ct);

            var totals = new long[CleanGroups.Count];
            foreach (var category in result.Categories)
                totals[GroupIndex(category.Category.Id)] += category.TotalBytes;

            var files = result.TotalFiles;
            var used = result.Categories.Count(c => c.TotalBytes > 0);

            Dispatch(() =>
            {
                for (var i = 0; i < CleanGroups.Count; i++) CleanGroups[i].Bytes = totals[i];
                CleanTotalText = Formatter.Bytes(result.TotalBytes);
                CleanStatusText = "Analisi completata";
                CleanFilesText = used == 0
                    ? "Nessun file recuperabile trovato"
                    : $"{Formatter.Count(files)} file trovati in {used} categorie";
            });
            _log.Info($"Scansione dashboard completata: {result.TotalBytes} byte in {used} categorie.");
        }
        catch (OperationCanceledException) { /* chiusura dell'app */ }
        catch (Exception ex)
        {
            _log.Error("Scansione dashboard non riuscita", ex);
            Dispatch(() =>
            {
                CleanStatusText = "Analisi non riuscita";
                CleanFilesText = "Apri Smart Clean per una scansione guidata";
            });
        }
    }

    /// <summary>Mappa le categorie del catalogo sulle sei righe mostrate in Home.</summary>
    private static int GroupIndex(string categoryId) => categoryId switch
    {
        "user_temp" or "windows_temp" => 0,
        "thumbnail_cache" or "dx_shader_cache" => 1,
        "edge_cache" or "chrome_cache" or "firefox_cache" => 2,
        "crash_dumps" or "error_reports" => 3,
        "recycle_bin" => 4,
        _ => 5,
    };

    private void OnLocaleChanged()
    {
        RaiseLocalized();
        UpdateLiveLabel();
        if (_healthCache.Current is not null) ApplyHealthAssessment(_healthCache.Current);
        else HealthScoreSub = Locale.T("home.health.empty");
    }

    /// <summary>Allineato al ciclo di vita della finestra: parte solo se l'utente lo vuole.</summary>
    public void StartIfNeeded()
        => ApplyMonitoring();

    /// <summary>Aggiornamento quando il flag LiveMonitoring cambia dalle Impostazioni.</summary>
    public void RefreshLiveFromConfig()
    {
        Raise(nameof(LiveMonitoring));
        ApplyMonitoring();
    }

    // Greeting locale-aware
    private static string LocalGreeting()
    {
        var h = DateTime.Now.Hour;
        return h switch { >= 6 and < 12 => Locale.T("greet.morning"),
                          >= 12 and < 19 => Locale.T("greet.afternoon"),
                          _ => Locale.T("greet.night") };
    }

    private async Task RecycleLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            UpdateRecycle();
            while (!ct.IsCancellationRequested)
                await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException) { /* stop normale */ }
        catch (Exception ex) { _log.Error("Poll cestino interrotto", ex); }
    }

    private void UpdateRecycle()
    {
        var q = RecycleBinHelper.Query();
        Dispatch(() =>
        {
            if (q.HasValue)
            {
                RecoverableText = Formatter.Bytes(q.Value.Bytes);
                RecoverableSub = Locale.F("home.recover.bin",
                    [Locale.P(q.Value.Items, "home.recover.item.one", "home.recover.item.many")]);
            }
            else
            {
                RecoverableText = Formatter.Unavailable;
                RecoverableSub = Locale.T("home.recover.unavailable");
            }
        });
    }

    private void HandleSnapshot(SystemSnapshot s)
        => Dispatch(() => Apply(s));

    private void HandleHealthAssessment(HealthAssessment assessment)
        => Dispatch(() => ApplyHealthAssessment(assessment));

    private void ApplyHealthAssessment(HealthAssessment assessment)
    {
        HealthScoreText = assessment.Score is int score ? $"{score}/100" : Formatter.Unavailable;
        HealthScoreValue = assessment.Score;
        HealthScoreBrush = assessment.Score switch
        {
            >= 80 => WpfBrushes.MediumSeaGreen,
            >= 60 => WpfBrushes.Goldenrod,
            >= 0 => WpfBrushes.IndianRed,
            _ => WpfBrushes.Gray,
        };
        HealthScoreColor = assessment.Score switch
        {
            >= 80 => WpfColor.FromRgb(0x34, 0xC7, 0x59),
            >= 60 => WpfColor.FromRgb(0xE6, 0xA2, 0x3C),
            >= 0 => WpfColor.FromRgb(0xFF, 0x5A, 0x5F),
            _ => WpfColor.FromRgb(0x7D, 0x89, 0x96),
        };
        HealthScoreSub = assessment.Score is null
            ? Locale.T("home.health.partial")
            : assessment.IsPartial ? Locale.T("home.health.partial") : Locale.T("home.health.complete");
    }

    private void Apply(SystemSnapshot s)
    {
        var hour = DateTime.Now.Hour;
        if (hour != _greetingHour)
        {
            _greetingHour = hour;
            var user = Environment.UserName;
            Greeting = user.Length == 0
                ? LocalGreeting()
                : $"{LocalGreeting()}, {char.ToUpperInvariant(user[0])}{user[1..]}";
        }

        // CPU
        CpuText = Formatter.Percent(s.CpuPercent);
        CpuProgress = s.CpuPercent;
        CpuTempText = Formatter.Celsius(s.CpuTemperatureCelsius);
        CpuClockText = Formatter.Clock(s.CpuClockMhz);
        CpuCoreText = s.CpuCores is int cores && s.CpuThreads is int threads
            ? $"{cores} / {threads}"
            : s.CpuThreads is int only ? $"{only}" : Formatter.Unavailable;

        // RAM
        RamText = Formatter.Percent(s.RamUsedPercent);
        RamProgress = s.RamUsedPercent;
        RamUsedValueText = s.RamTotalBytes is double total && s.RamAvailableBytes is double available
            ? Formatter.Bytes(total - available)
            : Formatter.Unavailable;
        RamAvailableValueText = Formatter.Bytes(s.RamAvailableBytes);
        RamCachedText = Formatter.Bytes(s.RamCachedBytes);

        // GPU
        GpuText = Formatter.Percent(s.GpuPercent);
        GpuProgress = s.GpuPercent;
        GpuTempText = Formatter.Celsius(s.GpuTemperatureCelsius);
        GpuClockText = Formatter.Clock(s.GpuClockMhz);
        GpuPowerText = s.GpuPowerWatts is double watts
            ? watts.ToString("#,##0", System.Globalization.CultureInfo.CurrentCulture) + " W"
            : Formatter.Unavailable;
        Raise(nameof(EnergySub));

        // Il totale VRAM arriva da NVML quando disponibile, altrimenti dal registro.
        var vramTotalBytes = s.GpuMemoryTotalBytes ?? _gpuVramTotalBytes;
        GpuVramText = s.GpuMemoryUsedBytes is double vramUsed
            ? vramTotalBytes is double vramTotal && vramTotal > 0
                ? $"{Formatter.Gigabytes(vramUsed)} / {Formatter.Gigabytes(vramTotal)}"
                : Formatter.Gigabytes(vramUsed)
            : Formatter.Unavailable;
        GpuVramPercent = s.GpuMemoryUsedBytes is double used && vramTotalBytes is double capacity && capacity > 0
            ? Math.Clamp(100.0 * used / capacity, 0, 100)
            : null;

        // Disco
        DiskText = Formatter.Percent(s.DiskActivePercent);
        DiskProgress = s.DiskActivePercent;
        DiskReadText = s.DiskReadBytesPerSecond is double read ? Formatter.RatePerSec(read / 1024d) : Formatter.Unavailable;
        DiskWriteText = s.DiskWriteBytesPerSecond is double write ? Formatter.RatePerSec(write / 1024d) : Formatter.Unavailable;

        // Rete
        NetDownText = s.NetDownKBytesPerSecond is double down ? Formatter.Mbps(down) : Formatter.Dash;
        NetUpText = s.NetUpKBytesPerSecond is double up ? Formatter.Mbps(up) : Formatter.Dash;
        NetStatusText = s.NetworkAvailable ? "Connesso" : "Disconnesso";
        NetStatusBrush = s.NetworkAvailable ? WpfBrushes.MediumSeaGreen : WpfBrushes.IndianRed;

        // Processi
        ProcText = s.ProcessCount > 0 ? Formatter.Count(s.ProcessCount) : Formatter.Dash;
        ProcUserText = s.UserProcessCount > 0 ? Formatter.Count(s.UserProcessCount) : Formatter.Dash;
        ProcSystemText = s.SystemProcessCount > 0 ? Formatter.Count(s.SystemProcessCount) : Formatter.Dash;
        ProcServiceText = Formatter.Count(s.ServiceCount);

        // Uptime
        UptimeDaysText = s.Uptime > TimeSpan.Zero ? Formatter.UptimeDays(s.Uptime) : Formatter.Dash;
        UptimeClockText = s.Uptime > TimeSpan.Zero ? Formatter.UptimeClock(s.Uptime) : "00:00:00";

        // Frase onesta: giudizio SOLO su metriche certe.
        var (stateKey, sentenceKey) = s.CpuPercent switch
        {
            null => ("home.state.reading", "home.sentence.reading.short"),
            < 25 when s.RamUsedPercent < 70 => ("home.state.ok", "home.sentence.ok"),
            >= 85 => ("home.state.busy", "home.sentence.busy"),
            _ when s.RamUsedPercent >= 90 => ("home.state.memory", "home.sentence.memory"),
            _ => ("home.state.ok", "home.sentence.normal"),
        };
        SystemStateTitle = Locale.T(stateKey);
        SystemSentence = Locale.T(sentenceKey);

        CpuSeries.Push(s.CpuPercent ?? 0);
        RamSeries.Push(s.RamUsedPercent ?? 0);
        GpuSeries.Push(s.GpuPercent ?? 0);
        DiskSeries.Push(s.DiskActivePercent ?? 0);
        // Le serie rete usano byte/s come base comune con PerformanceView;
        // l'asse del grafico le converte poi in KB/s, MB/s o GB/s.
        DownSeries.Push(Math.Max(0, s.NetDownKBytesPerSecond ?? 0) * 1024d);
        UpSeries.Push(Math.Max(0, s.NetUpKBytesPerSecond ?? 0) * 1024d);
        ProcessSeries.Push(Math.Max(0, s.ProcessCount));
    }

    private void UpdateLiveLabel()
        => LiveStatusLabel = LiveMonitoring ? LblLiveOn : LblLiveOff;

    private void ApplyMonitoring()
    {
        if (_cfg.LiveMonitoring) { if (!_monitor.IsRunning) _monitor.Resume(); }
        else if (_monitor.IsRunning) _monitor.Pause();
        UpdateLiveLabel();
    }

    private void Persist()
    {
        try { _store.Save(_cfg); }
        catch (Exception ex) { _log.Error("Salvataggio config fallito", ex); }
    }

    private static WpfBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new WpfSolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    public void Dispose()
    {
        _monitor.Snapshot -= HandleSnapshot;
        _healthCache.Updated -= HandleHealthAssessment;
        Locale.Changed -= OnLocaleChanged;
        _backgroundCts.Cancel();
        try { _backgroundCts.Dispose(); } catch (Exception) { /* niente crash a exit */ }
    }
}
