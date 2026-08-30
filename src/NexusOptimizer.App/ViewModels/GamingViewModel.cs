using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using Application = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Riga selezionabile della lista app in background.</summary>
public sealed class BackgroundAppVm : ObservableBase
{
    private bool _isSelected;

    public BackgroundAppVm(BackgroundAppInfo info)
    {
        Info = info;
        _isSelected = info.RecommendedByDefault;
    }

    public BackgroundAppInfo Info { get; }
    public string DisplayName => Info.DisplayName;
    public string ProcessName => Info.ProcessName + ".exe";
    public string CategoryText => BackgroundAppCatalog.Describe(Info.Category);
    public string MemoryText => Formatter.Bytes(Info.WorkingSetBytes);
    public string Note => Info.Note;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public WpfBrush CategoryBrush => Info.Category switch
    {
        BackgroundAppCategory.Browser => WpfBrushes.CornflowerBlue,
        BackgroundAppCategory.Comunicazione => WpfBrushes.MediumPurple,
        BackgroundAppCategory.Musica => WpfBrushes.MediumSeaGreen,
        BackgroundAppCategory.Sincronizzazione => WpfBrushes.DeepSkyBlue,
        BackgroundAppCategory.Periferiche => WpfBrushes.Goldenrod,
        BackgroundAppCategory.Launcher => WpfBrushes.IndianRed,
        BackgroundAppCategory.Aggiornamenti => WpfBrushes.LightSlateGray,
        _ => WpfBrushes.Silver,
    };

    public string IconKind => Info.Category switch
    {
        BackgroundAppCategory.Browser => "globe",
        BackgroundAppCategory.Comunicazione => "chat",
        BackgroundAppCategory.Musica => "music",
        BackgroundAppCategory.Sincronizzazione => "cloud",
        BackgroundAppCategory.Periferiche => "keyboard",
        BackgroundAppCategory.Launcher => "gamepad",
        BackgroundAppCategory.Aggiornamenti => "history",
        _ => "apps",
    };
}

/// <summary>Riga del resoconto azioni: cosa è stato realmente applicato.</summary>
public sealed record GamingReportRow(string Title, string Detail, bool Applied)
{
    public WpfBrush StateBrush => Applied ? WpfBrushes.MediumSeaGreen : WpfBrushes.Goldenrod;
    public string StateText => Applied ? "APPLICATA" : "NON APPLICATA";
}

/// <summary>
/// Modalità Gaming: prepara il PC prima di giocare con azioni reali e misurate.
/// Tutto ciò che viene modificato è registrato e ripristinato alla disattivazione.
/// </summary>
public sealed class GamingViewModel : ObservableBase, IPageLifecycle, IDisposable
{
    private readonly GamingModeService _service;
    private readonly SystemMonitor _monitor;
    private readonly AppModeService _mode;
    private readonly FileLogService _log;
    private readonly DispatcherTimer _sessionTimer;
    private readonly RelayCommand _scanCommand;
    private readonly RelayCommand _activateCommand;
    private readonly RelayCommand _deactivateCommand;
    private readonly RelayCommand _restoreCommand;

    private bool _busy;
    private bool _pageActive;
    private string _status = Locale.T("gam.status.idle");
    private string _sessionDuration = "00:00:00";
    private string _freedText = Formatter.Dash;
    private string _closedText = "0";
    private string _trimmedText = Formatter.Dash;
    private string _cpuNow = Formatter.Dash;
    private string _ramNow = Formatter.Dash;
    private string _gpuNow = Formatter.Dash;
    private string _availableNow = Formatter.Dash;
    private int _restoredCount;

    public GamingViewModel(GamingModeService service, SystemMonitor monitor,
                           AppModeService mode, FileLogService log)
    {
        _service = service;
        _monitor = monitor;
        _mode = mode;
        _log = log;

        _scanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsBusy);
        _activateCommand = new RelayCommand(_ => _ = ActivateAsync(), _ => !IsBusy && !IsActive);
        _deactivateCommand = new RelayCommand(_ => _ = DeactivateAsync(), _ => !IsBusy && IsActive);
        _restoreCommand = new RelayCommand(_ => _ = RestoreAppsAsync(), _ => !IsBusy && ClosedAppCount > 0);
        SelectRecommendedCommand = new RelayCommand(_ => SetSelection(app => app.Info.RecommendedByDefault));
        SelectNoneCommand = new RelayCommand(_ => SetSelection(_ => false));

        _sessionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) => UpdateSessionDuration();

        _monitor.Snapshot += OnSnapshot;
        _mode.Changed += OnModeChanged;
    }

    // ------------------------------------------------------------ contenuti

    public ObservableCollection<BackgroundAppVm> Apps { get; } = [];
    public ObservableCollection<GamingReportRow> Report { get; } = [];

    public ICommand ScanCommand => _scanCommand;
    public ICommand ActivateCommand => _activateCommand;
    public ICommand DeactivateCommand => _deactivateCommand;
    public ICommand RestoreAppsCommand => _restoreCommand;
    public ICommand SelectRecommendedCommand { get; }
    public ICommand SelectNoneCommand { get; }

    public bool IsActive => _service.IsActive;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RaiseCommands(); } }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string SessionDuration { get => _sessionDuration; private set => Set(ref _sessionDuration, value); }
    public string FreedText { get => _freedText; private set => Set(ref _freedText, value); }
    public string ClosedText { get => _closedText; private set => Set(ref _closedText, value); }
    public string TrimmedText { get => _trimmedText; private set => Set(ref _trimmedText, value); }
    public string CpuNow { get => _cpuNow; private set => Set(ref _cpuNow, value); }
    public string RamNow { get => _ramNow; private set => Set(ref _ramNow, value); }
    public string GpuNow { get => _gpuNow; private set => Set(ref _gpuNow, value); }
    public string AvailableNow { get => _availableNow; private set => Set(ref _availableNow, value); }

    public int ClosedAppCount => _service.ClosedApps.Count;
    public string ModeLevelText => _mode.DisplayName;

    public string StateTitle => Locale.T(IsActive ? "gam.state.on" : "gam.state.off");
    public WpfBrush StateBrush => IsActive ? WpfBrushes.MediumSeaGreen : WpfBrushes.Goldenrod;
    public string StateDetail => Locale.T(IsActive ? "gam.state.on.detail" : "gam.state.off.detail");

    public string SelectionText => Apps.Count == 0
        ? Locale.T("gam.sel.none")
        : Locale.F("gam.sel.count",
            [Formatter.Count(Apps.Count(a => a.IsSelected)), Formatter.Count(Apps.Count),
             Formatter.Bytes(SelectedBytes)]);

    public string RestoreButtonText => _restoredCount > 0
        ? Locale.F("gam.restore.done", [Formatter.Count(_restoredCount)])
        : Locale.F("gam.restore.button", [Formatter.Count(ClosedAppCount)]);

    public string ForceCloseLabel => Locale.T(_mode.Level == AppModeLevel.Expert
        ? "gam.force.on"
        : "gam.force.off");

    public bool ForceCloseAvailable => _mode.Level == AppModeLevel.Expert;
    public bool ServicesAvailable => GamingModeService.IsElevated;

    public string ServicesLabel => Locale.T(GamingModeService.IsElevated
        ? "gam.services.on"
        : "gam.services.off");

    private long SelectedBytes => Apps.Where(a => a.IsSelected).Sum(a => a.Info.WorkingSetBytes);

    // ------------------------------------------------------------- opzioni

    private readonly GamingBoostOptions _options = new();

    public bool CloseBackgroundApps { get => _options.CloseBackgroundApps; set { _options.CloseBackgroundApps = value; Raise(); } }
    public bool HighPerformancePowerPlan { get => _options.HighPerformancePowerPlan; set { _options.HighPerformancePowerPlan = value; Raise(); } }
    public bool DisableGameDvr { get => _options.DisableGameDvr; set { _options.DisableGameDvr = value; Raise(); } }
    public bool EnableWindowsGameMode { get => _options.EnableWindowsGameMode; set { _options.EnableWindowsGameMode = value; Raise(); } }
    public bool TrimBackgroundMemory { get => _options.TrimBackgroundMemory; set { _options.TrimBackgroundMemory = value; Raise(); } }
    public bool PrioritizeForegroundGame { get => _options.PrioritizeForegroundGame; set { _options.PrioritizeForegroundGame = value; Raise(); } }

    public bool SuspendIndexingServices
    {
        get => _options.SuspendIndexingServices && GamingModeService.IsElevated;
        set { _options.SuspendIndexingServices = value && GamingModeService.IsElevated; Raise(); }
    }

    public bool AllowForceClose
    {
        get => _options.AllowForceClose && ForceCloseAvailable;
        set { _options.AllowForceClose = value && ForceCloseAvailable; Raise(); }
    }

    // ----------------------------------------------------------- lifecycle

    public void Activate()
    {
        _pageActive = true;
        if (Apps.Count == 0 && !IsBusy) _ = ScanAsync();
        if (IsActive) _sessionTimer.Start();
    }

    public void Deactivate()
    {
        _pageActive = false;
        _sessionTimer.Stop();
    }

    // -------------------------------------------------------------- azioni

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = Locale.T("gam.status.scanning");
        try
        {
            var found = await _service.ScanAsync(_mode.Level);
            Apps.Clear();
            foreach (var info in found)
            {
                var vm = new BackgroundAppVm(info);
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(BackgroundAppVm.IsSelected)) Raise(nameof(SelectionText));
                };
                Apps.Add(vm);
            }
            Raise(nameof(SelectionText));
            _log.Info($"Modalità Gaming: {Apps.Count} app in background rilevate "
                      + $"({Formatter.Bytes(Apps.Sum(a => a.Info.WorkingSetBytes))} occupati, livello {_mode.DisplayName}).");
            Status = Apps.Count == 0
                ? Locale.T("gam.status.clean")
                : Locale.F("gam.status.found", [Formatter.Count(Apps.Count), _mode.DisplayName]);
        }
        catch (Exception ex)
        {
            _log.Error("Analisi app in background non riuscita", ex);
            Status = Locale.T("gam.status.scanfailed");
        }
        finally { IsBusy = false; }
    }

    private async Task ActivateAsync()
    {
        if (IsBusy || IsActive) return;
        IsBusy = true;
        Status = Locale.T("gam.status.applying");
        Report.Clear();
        try
        {
            var selected = Apps.Where(a => a.IsSelected).Select(a => a.Info).ToArray();
            var report = await _service.ActivateAsync(_options, selected);

            foreach (var action in report.Actions)
                Report.Add(new GamingReportRow(action.Title, action.Detail, action.Applied));

            FreedText = report.MemoryFreedBytes > 0 ? Formatter.Bytes(report.MemoryFreedBytes) : "0 B";
            TrimmedText = report.TrimmedBytes > 0 ? Formatter.Bytes(report.TrimmedBytes) : "0 B";
            ClosedText = report.ClosedCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _restoredCount = 0;
            Status = report.NotClosedCount == 0
                ? Locale.F("gam.status.active", [Formatter.Count(report.ClosedCount), FreedText])
                : Locale.F("gam.status.partial", [Formatter.Count(report.NotClosedCount)]);
            await ScanAsync();
            _sessionTimer.Start();
        }
        catch (Exception ex)
        {
            _log.Error("Attivazione Modalità Gaming non riuscita", ex);
            Status = Locale.T("gam.status.activatefailed");
        }
        finally
        {
            IsBusy = false;
            RaiseState();
        }
    }

    private async Task DeactivateAsync()
    {
        if (IsBusy || !IsActive) return;
        IsBusy = true;
        Status = Locale.T("gam.status.restoring");
        try
        {
            var actions = await _service.DeactivateAsync();
            Report.Clear();
            foreach (var action in actions)
                Report.Add(new GamingReportRow(action.Title, action.Detail, action.Applied));
            _sessionTimer.Stop();
            SessionDuration = "00:00:00";
            Status = Locale.T("gam.status.off");
        }
        catch (Exception ex)
        {
            _log.Error("Disattivazione Modalità Gaming non riuscita", ex);
            Status = Locale.T("gam.status.restorefailed");
        }
        finally
        {
            IsBusy = false;
            RaiseState();
        }
    }

    private async Task RestoreAppsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _restoredCount = await _service.RestoreClosedAppsAsync();
            Status = _restoredCount > 0
                ? Locale.F("gam.status.reopened", [Formatter.Count(_restoredCount)])
                : Locale.T("gam.status.noreopen");
        }
        catch (Exception ex)
        {
            _log.Error("Riapertura app non riuscita", ex);
            Status = Locale.T("gam.status.reopenfailed");
        }
        finally
        {
            IsBusy = false;
            RaiseState();
        }
    }

    private void SetSelection(Func<BackgroundAppVm, bool> predicate)
    {
        foreach (var app in Apps) app.IsSelected = predicate(app);
        Raise(nameof(SelectionText));
    }

    // -------------------------------------------------------------- eventi

    private void OnSnapshot(SystemSnapshot snapshot)
    {
        if (!_pageActive) return;
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            CpuNow = Formatter.Percent(snapshot.CpuPercent);
            RamNow = Formatter.Percent(snapshot.RamUsedPercent);
            GpuNow = Formatter.Percent(snapshot.GpuPercent);
            AvailableNow = Formatter.Bytes(snapshot.RamAvailableBytes);
        });
    }

    private void OnModeChanged()
    {
        Raise(nameof(ModeLevelText));
        Raise(nameof(ForceCloseAvailable));
        Raise(nameof(ForceCloseLabel));
        Raise(nameof(AllowForceClose));
        if (!ForceCloseAvailable) AllowForceClose = false;
        if (!IsBusy) _ = ScanAsync();
    }

    private void UpdateSessionDuration()
    {
        if (_service.ActivatedAtUtc is not DateTime start) { SessionDuration = "00:00:00"; return; }
        var elapsed = DateTime.UtcNow - start;
        SessionDuration = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void RaiseState()
    {
        Raise(nameof(IsActive));
        Raise(nameof(StateTitle));
        Raise(nameof(StateBrush));
        Raise(nameof(StateDetail));
        Raise(nameof(ClosedAppCount));
        Raise(nameof(RestoreButtonText));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        _scanCommand.RaiseCanExecute();
        _activateCommand.RaiseCanExecute();
        _deactivateCommand.RaiseCanExecute();
        _restoreCommand.RaiseCanExecute();
    }

    public void Dispose()
    {
        _monitor.Snapshot -= OnSnapshot;
        _mode.Changed -= OnModeChanged;
        _sessionTimer.Stop();
    }
}
