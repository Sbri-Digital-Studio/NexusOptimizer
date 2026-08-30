using System.Collections.ObjectModel;
using System.Windows.Input;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

public sealed class MainViewModel : ObservableBase
{
    public const string DashboardId = "nav.dashboard";

    private readonly DashboardViewModel _dashboard;
    private readonly CleanCleanViewModel _clean;
    private readonly SystemInfoViewModel _systemInfo;
    private readonly PerformanceViewModel _performance;
    private readonly ProcessesViewModel _processes;
    private readonly StartupViewModel _startup;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly HistoryViewModel _historyView;
    private readonly SettingsViewModel _settings;
    private readonly OptimizerViewModel _optimizer;
    private readonly RamManagerViewModel _ramManager;
    private readonly DiskManagerViewModel _diskManager;
    private readonly PrivacyGuardViewModel _privacyGuard;
    private readonly ToolsViewModel _tools;
    private readonly GamingViewModel _gaming;
    private readonly SoftwareViewModel _software;
    private readonly AppModeService _mode;
    private readonly NotificationsViewModel _notifications;
    private readonly Stack<NavItem> _history = [];
    private NavItem? _selected;
    private object _current;
    private bool _navigatingBack;

    /// <summary>
    /// Tutte le destinazioni raggiungibili (menu laterale, palette, scorciatoie).
    /// Le prime <see cref="MenuCount"/> voci compongono il menu laterale visibile;
    /// le restanti restano navigabili da Command Palette, titlebar e strumenti rapidi.
    /// </summary>
    public IReadOnlyList<NavItem> Items { get; } =
    [
        new(DashboardId,       "home"),
        new("nav.gaming",      "gamepad"),
        new("nav.systeminfo",  "monitor"),
        new("nav.cleancat",    "broom"),
        new("nav.optimizer",   "gear"),
        new("nav.rammanager",  "chip"),
        new("nav.diskmanager", "disk"),
        new("nav.startup",     "rocket"),
        new("nav.software",    "installedApps"),
        new("nav.privacy",     "shield"),
        new("nav.tools",       "apps"),
        new("nav.performance", "chart"),
        new("nav.history",     "restoreCenter"),
        // --- fuori dal menu laterale, ma sempre navigabili ---
        new("nav.processes",   "pulse"),
        new("nav.diagnostics", "pulse"),
        new("nav.settings",    "gear"),
    ];

    private const int MenuCount = 13;

    /// <summary>Voci mostrate nella sidebar, nell'ordine del design di riferimento.</summary>
    public IReadOnlyList<NavItem> MenuItems { get; }

    /// <summary>Livelli operativi mostrati nel riquadro "LIVELLO MODALITÀ".</summary>
    public IReadOnlyList<ModeLevelVm> ModeLevels { get; }

    public MainViewModel(DashboardViewModel dashboard,
                         CleanCleanViewModel clean,
                         SystemInfoViewModel systemInfo,
                         PerformanceViewModel performance,
                         ProcessesViewModel processes,
                         StartupViewModel startup,
                         DiagnosticsViewModel diagnostics,
                         HistoryViewModel historyView,
                         SettingsViewModel settings,
                         OptimizerViewModel optimizer,
                         RamManagerViewModel ramManager,
                         DiskManagerViewModel diskManager,
                         PrivacyGuardViewModel privacyGuard,
                         ToolsViewModel tools,
                         GamingViewModel gaming,
                         SoftwareViewModel software,
                         NotificationsViewModel notifications,
                         AppModeService mode)
    {
        _dashboard = dashboard;
        _clean = clean;
        _systemInfo = systemInfo;
        _performance = performance;
        _processes = processes;
        _startup = startup;
        _diagnostics = diagnostics;
        _historyView = historyView;
        _settings = settings;
        _optimizer = optimizer;
        _ramManager = ramManager;
        _diskManager = diskManager;
        _privacyGuard = privacyGuard;
        _tools = tools;
        _gaming = gaming;
        _software = software;
        _mode = mode;
        _notifications = notifications;
        _current = dashboard;
        MenuItems = Items.Take(MenuCount).ToArray();
        ModeLevels =
        [
            new(AppModeLevel.Safe, "SAFE", "Predefinito · Sicuro", "shield", WpfBrushes.MediumSeaGreen),
            new(AppModeLevel.Balanced, "BALANCED", "Consigliato", "bolt", WpfBrushes.Goldenrod),
            new(AppModeLevel.Expert, "EXPERT", "Avanzato", "gear", WpfBrushes.IndianRed),
        ];
        SetModeCommand = new RelayCommand(parameter =>
        {
            if (parameter is ModeLevelVm level) { _mode.Set(level.Level); IsModePickerOpen = false; }
        });
        _mode.Changed += RefreshMode;
        RefreshMode();
        _dashboard.AnalyzeRequested += OpenCleanAndAnalyze;
        _dashboard.DiagnosticsRequested += OpenDiagnostics;
        _dashboard.NavigationRequested += NavigateById;
        _optimizer.NavigateRequested += NavigateById;
        _diskManager.CleanRequested += OpenCleanAndAnalyze;
        _privacyGuard.NavigateRequested += NavigateById;
        _notifications.NavigationRequested += NavigateById;
        GoBackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack);
        CloseSectionCommand = new RelayCommand(_ =>
        {
            if (CanGoBack) GoBack();
            else NavigateById(DashboardId);
        });
        Selected = Items[0];

        // Cambio lingua: aggiorna titoli voci, header e placeholder visibili.
        Locale.Changed += RefreshLocalized;
    }

    public DashboardViewModel Dashboard => _dashboard;

    /// <summary>Campanella degli avvisi mostrata nella barra titolo.</summary>
    public NotificationsViewModel Notifications => _notifications;

    private void RefreshLocalized()
    {
        foreach (var it in Items)
        {
            it.Refresh();
        }
        Raise(nameof(CurrentTitle));
        Raise(nameof(CurrentSubtitle));
        Raise(nameof(SearchPlaceholder));
    }

    public NavItem? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || value is null) return;
            var previous = _selected;
            _selected = value;
            Raise();
            if (!_navigatingBack && previous is not null)
                _history.Push(previous);
            Raise(nameof(CanGoBack));
            (GoBackCommand as RelayCommand)?.RaiseCanExecute();
            Navigate(value);
            Raise(nameof(CurrentTitle));
            Raise(nameof(CurrentSubtitle));
        }
    }

    /// <summary>Pagina corrente per il ContentControl (VM risolta via DataTemplate).</summary>
    public object Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string CurrentTitle => Selected is null ? "NEXUS OPTIMIZER" : Locale.T(Selected.Id).ToUpperInvariant();

    public string CurrentSubtitle => Selected?.Subtitle ?? string.Empty;

    public string SearchPlaceholder => Locale.T("ui.search.placeholder");

    // ------------------------------------------------------------------
    // LIVELLO MODALITÀ (titlebar + sidebar)
    // ------------------------------------------------------------------
    public ICommand SetModeCommand { get; private set; } = new RelayCommand(_ => { });

    public string ModeDisplayName => _mode.DisplayName;

    public WpfBrush ModeBrush => _mode.Level switch
    {
        AppModeLevel.Balanced => WpfBrushes.Goldenrod,
        AppModeLevel.Expert => WpfBrushes.IndianRed,
        _ => WpfBrushes.MediumSeaGreen,
    };

    public string ModeCaption => _mode.Level switch
    {
        AppModeLevel.Balanced => "Azioni consigliate abilitate",
        AppModeLevel.Expert => "Tutte le azioni disponibili",
        _ => "Funzioni locali attive",
    };

    private bool _modePickerOpen;

    /// <summary>Apertura del selettore modalità nella titlebar.</summary>
    public bool IsModePickerOpen { get => _modePickerOpen; set => Set(ref _modePickerOpen, value); }

    private void RefreshMode()
    {
        foreach (var level in ModeLevels)
            level.IsSelected = level.Level == _mode.Level;
        Raise(nameof(ModeDisplayName));
        Raise(nameof(ModeBrush));
        Raise(nameof(ModeCaption));
    }

    /// <summary>Versione mostrata nel piede della sidebar.</summary>
    public static string VersionText
    {
        get
        {
            var version = typeof(MainViewModel).Assembly.GetName().Version;
            var bits = Environment.Is64BitProcess ? "64 bit" : "32 bit";
            return version is null ? bits : $"v{version.ToString(3)} — {bits}";
        }
    }

    public ICommand GoBackCommand { get; }

    /// <summary>
    /// Chiude la sezione corrente: torna da dove si è arrivati, oppure alla
    /// Dashboard se non c'è una cronologia. Le pagine aperte dalla barra titolo
    /// (Impostazioni) non sono nel menu laterale: senza questo comando non
    /// avrebbero un'uscita evidente.
    /// </summary>
    public ICommand CloseSectionCommand { get; private set; } = new RelayCommand(_ => { });

    public bool CanGoBack => _history.Count > 0;

    public void GoBack()
    {
        if (_history.Count == 0) return;
        var target = _history.Pop();
        _navigatingBack = true;
        try { Selected = target; }
        finally { _navigatingBack = false; }
        Raise(nameof(CanGoBack));
        (GoBackCommand as RelayCommand)?.RaiseCanExecute();
    }

    private void Navigate(NavItem item)
    {
        object next = item.Id switch
        {
            DashboardId => _dashboard,
            "nav.cleancat" => _clean,
            "nav.systeminfo" => OpenSystemInfo(),
            "nav.optimizer" => _optimizer,
            "nav.rammanager" => _ramManager,
            "nav.diskmanager" => _diskManager,
            "nav.privacy" => _privacyGuard,
            "nav.tools" => _tools,
            "nav.gaming" => _gaming,
            "nav.software" => _software,
            "nav.performance" => _performance,
            "nav.processes" => _processes,
            "nav.startup" => _startup,
            "nav.diagnostics" => _diagnostics,
            "nav.history" => _historyView,
            "nav.settings" => _settings,
            _ => _dashboard,
        };

        if (ReferenceEquals(Current, next)) return;
        if (Current is IPageLifecycle oldPage) oldPage.Deactivate();
        Current = next;
        if (next is IPageLifecycle newPage) newPage.Activate();
    }

    private SystemInfoViewModel OpenSystemInfo()
    {
        // Il caricamento WMI è intenzionalmente asincrono: la navigazione resta
        // immediata e i dati vengono popolati senza bloccare l'interfaccia.
        _ = _systemInfo.LoadIfNeededAsync();
        return _systemInfo;
    }

    private async void OpenCleanAndAnalyze()
    {
        var cleanItem = Items.First(item => item.Id == "nav.cleancat");
        if (!ReferenceEquals(Selected, cleanItem))
            Selected = cleanItem;
        await _clean.AnalyzeAsync();
    }

    private void OpenDiagnostics()
    {
        var diagnosticsItem = Items.First(item => item.Id == "nav.diagnostics");
        if (!ReferenceEquals(Selected, diagnosticsItem)) Selected = diagnosticsItem;
    }

    // ------------------------------------------------------------------
    // COMMAND PALETTE (CTRL+K)
    // ------------------------------------------------------------------
    private bool _paletteOpen;

    public bool IsPaletteOpen
    {
        get => _paletteOpen;
        private set { if (_paletteOpen != value) { _paletteOpen = value; Raise(); if (!value) { _query = string.Empty; Raise(nameof(Query)); Results.Clear(); } } }
    }

    public void OpenPalette() => IsPaletteOpen = true;

    public void ClosePalette() => IsPaletteOpen = false;

    private string _query = string.Empty;

    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? string.Empty;
            Raise();
            RebuildResults(_query);
        }
    }

    /// <summary>Apre la palette pronta per la ricerca della sezione voluta.</summary>
    public void OpenPaletteWithQuery(string text)
    {
        OpenPalette();
        Query = text;
    }

    public ObservableCollection<PaletteItem> Results { get; } = [];

    public ICommand SelectPaletteItemCommand { get; } =
        new RelayCommand(p => (p as PaletteItem)?.Execute());

    /// <summary>Esegue la prima voce visibile (Invio dalla palette).</summary>
    public void CommitFirstResult()
    {
        if (Results.Count > 0) Results[0].Execute();
    }

    private void RebuildResults(string q)
    {
        Results.Clear();
        var needle = q.Trim();
        if (needle.Length == 0) return;

        foreach (var item in Items)
        {
            var title = Locale.T(item.Id);
            if (title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || item.Id.Contains(needle.ToLowerInvariant(), StringComparison.Ordinal))
            {
                var captured = item.Id;
                Results.Add(new PaletteItem(title, Locale.T(captured + ".sub"),
                    () => { IsPaletteOpen = false; NavigateById(captured); }));
            }
        }
    }

    private void NavigateById(string id)
    {
        var target = Items.FirstOrDefault(i => i.Id == id);
        if (target != null) Selected = target;
    }

    /// <summary>
    /// Apertura diretta di una sezione (usata da "--page:&lt;id&gt;" sulla riga di comando,
    /// utile per collegamenti dedicati come una scorciatoia alla Modalità Gaming).
    /// </summary>
    public void OpenSection(string id) => NavigateById(id);
}

/// <summary>Buffer circolare osservabile per le serie dei grafici.</summary>
public sealed class SeriesPointBuffer(int capacity) : ObservableCollection<double>
{
    public int Capacity { get; } = capacity;

    public void Push(double value)
    {
        Add(value);
        while (Count > Capacity)
            RemoveAt(0);
    }
}
