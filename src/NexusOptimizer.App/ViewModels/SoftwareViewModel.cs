using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using System.Windows.Media.Imaging;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Riga della tabella dei programmi installati.</summary>
public sealed class InstalledAppVm(InstalledApp app)
{
    public InstalledApp Model { get; } = app;

    /// <summary>
    /// Icona vera del programma, estratta dal file che la contiene. Null quando
    /// il programma non ne dichiara una: la riga mostra allora un segnaposto.
    /// </summary>
    public BitmapSource? Icon { get; init; }

    public bool HasIcon => Icon is not null;

    public string Name => Model.Name;
    public string Publisher => Model.Publisher.Length > 0 ? Model.Publisher : Formatter.Dash;
    public string Version => Model.Version.Length > 0 ? Model.Version : Formatter.Dash;
    public string SizeText => Model.SizeBytes is long bytes ? Formatter.Bytes(bytes) : Formatter.Dash;
    public string InstalledText => Model.InstalledOn is DateTime date
        ? date.ToString("d", Locale.Culture)
        : Formatter.Dash;
    public string ScopeText => Locale.T(Model.IsUserScope ? "soft.scope.user" : "soft.scope.machine");
    public string ArchitectureText => Model.Is64Bit ? "64 bit" : "32 bit";
    public string Location => Model.InstallLocation;
    public bool CanUninstall => Model.CanUninstall;
    public bool HasLocation => Model.InstallLocation.Length > 0;

    /// <summary>Ordinamento per dimensione: le voci senza dato restano in fondo.</summary>
    public long SortSize => Model.SizeBytes ?? -1;
    public DateTime SortDate => Model.InstalledOn ?? DateTime.MinValue;
}

/// <summary>Riga della tabella dei driver.</summary>
public sealed class DriverVm(DeviceDriver driver)
{
    public DeviceDriver Model { get; } = driver;

    /// <summary>Icona e colore della classe di periferica.</summary>
    public string IconKind => DriverService.VisualFor(Model.RawClass).Kind;

    public WpfBrush IconBrush => Brush(DriverService.VisualFor(Model.RawClass).Color);

    private static WpfBrush Brush(string hex)
    {
        try
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return WpfBrushes.LightSlateGray;
        }
    }

    public string Device => Model.Device;
    public string DeviceClass => Model.DeviceClass;
    public string Provider => Model.Provider.Length > 0 ? Model.Provider : Formatter.Dash;
    public string Version => Model.Version.Length > 0 ? Model.Version : Formatter.Dash;
    public string DateText => Model.Date is DateTime date ? date.ToString("d", Locale.Culture) : Formatter.Dash;
    public bool HasProblem => Model.HasProblem;
    public bool HasVendorPage => DriverService.VendorPageFor(Model) is not null;

    public string StateText => Model.HasProblem
        ? Locale.F("drv.state.problem", [Model.ProblemCode.ToString(CultureInfo.CurrentCulture)])
        : Model.IsSigned ? Locale.T("drv.state.signed") : Locale.T("drv.state.unsigned");

    public WpfBrush StateBrush => Model.HasProblem
        ? WpfBrushes.IndianRed
        : Model.IsSigned ? WpfBrushes.MediumSeaGreen : WpfBrushes.Goldenrod;
}

/// <summary>
/// Programmi e driver installati sul PC. Tutto ciò che si vede qui è letto dal
/// sistema: il Registro per i programmi, WMI per i driver. Le azioni non
/// rimuovono nulla per conto proprio: avviano il disinstallatore dell'autore del
/// software oppure aprono lo strumento di Windows competente.
/// </summary>
public sealed class SoftwareViewModel : ObservableBase, IPageLifecycle
{
    private readonly InstalledAppsService _apps;
    private readonly DriverService _drivers;
    private readonly WingetService _packages;
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly FileLogService _log;

    private IReadOnlyList<InstalledAppVm> _allApps = [];
    private IReadOnlyList<DriverVm> _allDrivers = [];
    private bool _loaded;
    private bool _busy;
    private bool _searchingUpdates;
    private bool _scanningPackages;
    private string? _upgradingId;
    private int _tab;
    private string _appQuery = "";
    private string _driverQuery = "";
    private bool _onlyProblems;
    private int _appSort;
    private InstalledAppVm? _selectedApp;
    private DriverVm? _selectedDriver;
    private string _status = "";
    private string _updateStatus = "";

    public SoftwareViewModel(InstalledAppsService apps, DriverService drivers, WingetService packages,
                             AppConfig config, ConfigStore store, FileLogService log)
    {
        _apps = apps;
        _drivers = drivers;
        _packages = packages;
        _config = config;
        _store = store;
        _log = log;

        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(force: true), _ => !IsBusy);
        SearchDriverUpdatesCommand = new RelayCommand(_ => _ = SearchDriverUpdatesAsync(),
            _ => !_searchingUpdates);
        OpenLocationCommand = new RelayCommand(_ =>
        {
            if (SelectedApp is not null && !InstalledAppsService.TryOpenLocation(SelectedApp.Model))
                Status = Locale.T("soft.location.missing");
        });
        OpenWindowsAppsCommand = new RelayCommand(_ => InstalledAppsService.OpenWindowsAppsPage());
        OpenDeviceManagerCommand = new RelayCommand(_ => DriverService.OpenDeviceManager());
        OpenWindowsUpdateCommand = new RelayCommand(_ => DriverService.OpenWindowsUpdate());
        OpenVendorPageCommand = new RelayCommand(_ =>
        {
            if (SelectedDriver is null) return;
            var url = DriverService.VendorPageFor(SelectedDriver.Model);
            if (url is not null) DriverService.Launch(url);
        });

        ScanPackagesCommand = new RelayCommand(_ => _ = ScanPackagesAsync(), _ => !_scanningPackages);
        UpgradePackageCommand = new RelayCommand(parameter =>
        {
            if (parameter is PackageUpdate package) _ = UpgradePackageAsync(package);
        }, parameter => _upgradingId is null && parameter is PackageUpdate);
        SelectTabCommand = new RelayCommand(parameter =>
        {
            if (parameter is string text && int.TryParse(text, out var index)) Tab = index;
        });

        Locale.Changed += OnLocaleChanged;
    }

    // ------------------------------------------------------------------- aree

    /// <summary>0 = programmi installati, 1 = aggiornamenti, 2 = driver.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (!Set(ref _tab, value)) return;
            Raise(nameof(ShowApps));
            Raise(nameof(ShowUpdates));
            Raise(nameof(ShowDrivers));
        }
    }

    public bool ShowApps => _tab == 0;
    public bool ShowUpdates => _tab == 1;
    public bool ShowDrivers => _tab == 2;

    public ICommand SelectTabCommand { get; }

    // ------------------------------------------------------------------ stato

    public ObservableCollection<InstalledAppVm> Apps { get; } = [];
    public ObservableCollection<DriverVm> Drivers { get; } = [];
    public ObservableCollection<DriverUpdate> DriverUpdates { get; } = [];

    /// <summary>Programmi con una versione più recente disponibile (winget).</summary>
    public ObservableCollection<PackageUpdate> PackageUpdates { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand SearchDriverUpdatesCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand OpenWindowsAppsCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public ICommand OpenWindowsUpdateCommand { get; }
    public ICommand OpenVendorPageCommand { get; }
    public ICommand ScanPackagesCommand { get; }
    public ICommand UpgradePackageCommand { get; }

    /// <summary>winget è il gestore pacchetti di Windows: senza, l'area lo dichiara.</summary>
    public bool PackagesSupported => _packages.IsAvailable;

    public bool HasPackageUpdates => PackageUpdates.Count > 0;

    private string _packageStatus = "";
    public string PackageStatus { get => _packageStatus; private set => Set(ref _packageStatus, value); }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            (RefreshCommand as RelayCommand)?.RaiseCanExecute();
        }
    }

    public string AppQuery
    {
        get => _appQuery;
        set { if (Set(ref _appQuery, value ?? "")) ApplyAppFilter(); }
    }

    public string DriverQuery
    {
        get => _driverQuery;
        set { if (Set(ref _driverQuery, value ?? "")) ApplyDriverFilter(); }
    }

    /// <summary>Mostra solo le periferiche che Windows segnala come non funzionanti.</summary>
    public bool OnlyProblems
    {
        get => _onlyProblems;
        set { if (Set(ref _onlyProblems, value)) ApplyDriverFilter(); }
    }

    public IReadOnlyList<string> AppSortOptions =>
        [Locale.T("soft.sort.name"), Locale.T("soft.sort.size"), Locale.T("soft.sort.date")];

    public int AppSortIndex
    {
        get => _appSort;
        set { if (Set(ref _appSort, value)) ApplyAppFilter(); }
    }

    public InstalledAppVm? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (!Set(ref _selectedApp, value)) return;
            Raise(nameof(HasSelectedApp));
        }
    }

    public bool HasSelectedApp => _selectedApp is not null;

    public DriverVm? SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            if (!Set(ref _selectedDriver, value)) return;
            Raise(nameof(HasSelectedDriver));
        }
    }

    public bool HasSelectedDriver => _selectedDriver is not null;

    public string Status { get => _status; private set => Set(ref _status, value); }

    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    public bool HasDriverUpdates => DriverUpdates.Count > 0;

    /// <summary>Riepilogo dei programmi: quantità e spazio dichiarato.</summary>
    public string AppsSummary
    {
        get
        {
            if (_allApps.Count == 0) return Locale.T("soft.apps.empty");
            var total = _allApps.Sum(a => a.Model.SizeBytes ?? 0);
            return Locale.F("soft.apps.summary",
                [Formatter.Count(_allApps.Count), Formatter.Bytes(total)]);
        }
    }

    public string DriversSummary
    {
        get
        {
            if (_allDrivers.Count == 0) return Locale.T("soft.drivers.empty");
            var problems = _allDrivers.Count(d => d.HasProblem);
            return problems == 0
                ? Locale.F("soft.drivers.summary", [Formatter.Count(_allDrivers.Count)])
                : Locale.F("soft.drivers.summary.problems",
                    [Formatter.Count(_allDrivers.Count), Formatter.Count(problems)]);
        }
    }

    // ------------------------------------------------------------- caricamento

    public void Activate()
    {
        if (!_loaded && !IsBusy) _ = LoadAsync(force: false);
    }

    public void Deactivate() { }

    private async Task LoadAsync(bool force)
    {
        if (IsBusy) return;
        if (_loaded && !force) return;
        IsBusy = true;
        Status = Locale.T("soft.loading");
        try
        {
            // Le icone si estraggono qui, sul thread di lavoro: sono letture di file
            // e vengono congelate per poter essere mostrate dall'interfaccia.
            var apps = await Task.Run(() => _apps.Collect()
                .Select(app => new InstalledAppVm(app) { Icon = LoadIcon(app) })
                .ToList());
            var drivers = await Task.Run(_drivers.Collect);

            _allApps = [.. apps];
            _allDrivers = [.. drivers.Select(driver => new DriverVm(driver))];
            _loaded = true;
            _log.Info($"Inventario: {_allApps.Count} programmi, {_allDrivers.Count} driver "
                      + $"({_allDrivers.Count(d => d.HasProblem)} con problemi), winget={_packages.IsAvailable}.");

            ApplyAppFilter();
            ApplyDriverFilter();
            Status = "";
        }
        catch (Exception ex)
        {
            _log.Error("Inventario programmi e driver non riuscito", ex);
            Status = Locale.T("soft.load.failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Icona dichiarata dal programma; in mancanza si prova il suo disinstallatore,
    /// che quasi sempre porta lo stesso logo. Mai un'immagine presa da altrove.
    /// </summary>
    private static BitmapSource? LoadIcon(InstalledApp app)
    {
        var icon = ShellIconLoader.Load(app.IconSource);
        if (icon is not null) return icon;

        var (file, _) = InstalledAppsService.SplitCommand(app.UninstallCommand);
        if (file.Length == 0 || file.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return ShellIconLoader.Load(file);
    }

    private void ApplyAppFilter()
    {
        var needle = _appQuery.Trim();
        IEnumerable<InstalledAppVm> filtered = _allApps;
        if (needle.Length > 0)
        {
            filtered = filtered.Where(a =>
                a.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || a.Model.Publisher.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        filtered = _appSort switch
        {
            1 => filtered.OrderByDescending(a => a.SortSize),
            2 => filtered.OrderByDescending(a => a.SortDate),
            _ => filtered.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Apps.Clear();
        foreach (var app in filtered) Apps.Add(app);
        Raise(nameof(AppsSummary));
    }

    private void ApplyDriverFilter()
    {
        var needle = _driverQuery.Trim();
        IEnumerable<DriverVm> filtered = _allDrivers;
        if (_onlyProblems) filtered = filtered.Where(d => d.HasProblem);
        if (needle.Length > 0)
        {
            filtered = filtered.Where(d =>
                d.Device.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || d.Model.Provider.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || d.DeviceClass.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        Drivers.Clear();
        foreach (var driver in filtered) Drivers.Add(driver);
        Raise(nameof(DriversSummary));
    }

    // ------------------------------------------------------------- disinstalla

    /// <summary>
    /// Avvia il disinstallatore del programma selezionato. La conferma esplicita
    /// avviene nella vista: da qui si parte solo dopo un sì.
    /// </summary>
    public async Task UninstallSelectedAsync()
    {
        var selected = SelectedApp;
        if (selected is null) return;

        if (!_apps.TryStartUninstall(selected.Model, out var failure))
        {
            Status = Locale.T(failure == "no-command" ? "soft.uninstall.nocommand" : "soft.uninstall.failed");
            return;
        }

        Status = Locale.F("soft.uninstall.started", [selected.Name]);
        // Il disinstallatore è un processo esterno con la sua interfaccia: si
        // rilegge l'inventario dopo qualche secondo, senza bloccare la pagina.
        await Task.Delay(TimeSpan.FromSeconds(6));
        await LoadAsync(force: true);
    }

    // --------------------------------------------------------- aggiornamenti

    private async Task SearchDriverUpdatesAsync()
    {
        if (_searchingUpdates) return;
        _searchingUpdates = true;
        (SearchDriverUpdatesCommand as RelayCommand)?.RaiseCanExecute();
        UpdateStatus = Locale.T("drv.search.running");
        DriverUpdates.Clear();
        Raise(nameof(HasDriverUpdates));
        try
        {
            var result = await _drivers.SearchUpdatesAsync();
            _config.LastDriverCheckUtc = DateTime.UtcNow;
            Persist();

            foreach (var update in result.Updates) DriverUpdates.Add(update);
            Raise(nameof(HasDriverUpdates));
            UpdateStatus = result.Status switch
            {
                DriverSearchStatus.UpToDate => Locale.T("drv.search.uptodate"),
                DriverSearchStatus.UpdatesAvailable =>
                    Locale.F("drv.search.available", [Formatter.Count(result.Updates.Count)]),
                _ => Locale.T("drv.search.failed"),
            };
        }
        finally
        {
            _searchingUpdates = false;
            (SearchDriverUpdatesCommand as RelayCommand)?.RaiseCanExecute();
        }
    }


    // ------------------------------------------------- aggiornamenti programmi

    /// <summary>
    /// Chiede a winget quali programmi installati hanno una versione più recente.
    /// È una chiamata di rete e parte solo da qui o dal controllo automatico.
    /// </summary>
    private async Task ScanPackagesAsync()
    {
        if (_scanningPackages) return;
        if (!_packages.IsAvailable)
        {
            PackageStatus = Locale.T("pkg.unavailable");
            return;
        }

        _scanningPackages = true;
        (ScanPackagesCommand as RelayCommand)?.RaiseCanExecute();
        PackageStatus = Locale.T("pkg.scanning");
        PackageUpdates.Clear();
        Raise(nameof(HasPackageUpdates));
        try
        {
            var result = await _packages.ScanAsync();
            _config.LastSoftwareCheckUtc = DateTime.UtcNow;
            Persist();

            foreach (var package in result.Updates) PackageUpdates.Add(package);
            Raise(nameof(HasPackageUpdates));
            PackageStatus = result.Status switch
            {
                PackageManagerStatus.NotAvailable => Locale.T("pkg.unavailable"),
                PackageManagerStatus.UpToDate => Locale.T("pkg.uptodate"),
                PackageManagerStatus.UpdatesAvailable =>
                    Locale.F("pkg.available", [Formatter.Count(result.Updates.Count)]),
                _ => Locale.T("pkg.failed"),
            };
        }
        finally
        {
            _scanningPackages = false;
            (ScanPackagesCommand as RelayCommand)?.RaiseCanExecute();
        }
    }

    /// <summary>
    /// Aggiorna un programma con l'installer originale, su richiesta esplicita.
    /// Un fallimento tipico è la mancanza di privilegi: viene detto, non nascosto.
    /// </summary>
    private async Task UpgradePackageAsync(PackageUpdate package)
    {
        if (_upgradingId is not null) return;
        _upgradingId = package.Id;
        (UpgradePackageCommand as RelayCommand)?.RaiseCanExecute();
        PackageStatus = Locale.F("pkg.upgrading", [package.Name]);
        try
        {
            var (ok, _) = await _packages.UpgradeAsync(package);
            if (ok)
            {
                PackageUpdates.Remove(package);
                Raise(nameof(HasPackageUpdates));
                PackageStatus = Locale.F("pkg.upgraded", [package.Name, package.AvailableVersion]);
                await LoadAsync(force: true);
            }
            else
            {
                PackageStatus = Locale.F("pkg.upgrade.failed", [package.Name]);
            }
        }
        finally
        {
            _upgradingId = null;
            (UpgradePackageCommand as RelayCommand)?.RaiseCanExecute();
        }
    }

    private void Persist()
    {
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio esito ricerca driver non riuscito", ex); }
    }

    private void OnLocaleChanged()
    {
        Raise(nameof(AppSortOptions));
        Raise(nameof(AppsSummary));
        Raise(nameof(DriversSummary));
    }

    public void Dispose() => Locale.Changed -= OnLocaleChanged;
}
