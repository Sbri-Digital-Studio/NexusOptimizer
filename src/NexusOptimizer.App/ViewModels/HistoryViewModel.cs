using System.Collections.ObjectModel;
using System.Globalization;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Safety;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Riga della cronologia, volutamente priva di percorsi o nomi di file.</summary>
public sealed class HistoryOperationVm : ObservableBase
{
    public HistoryOperationVm(SafetyOperationRecord record) => Record = record;

    public SafetyOperationRecord Record { get; }
    public string DateText => Record.StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string CategoriesText => string.Join(" · ", Record.Categories.Select(id => Locale.T("cat." + id)));
    public string SummaryText => Locale.T("history.summary")
        .Replace("{0}", Record.ItemsQuarantined.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
        .Replace("{1}", Formatter.Bytes(Record.BytesQuarantined), StringComparison.Ordinal);
    public string StateText => Locale.T("history.status." + Record.Status.ToString().ToLowerInvariant());
    public bool CanUndo => Record.CanUndo;

    public void RefreshLocalized()
    {
        Raise(nameof(DateText));
        Raise(nameof(CategoriesText));
        Raise(nameof(SummaryText));
        Raise(nameof(StateText));
    }
}

public enum ActiveChangeState
{
    SystemCore,
    Detected,
    ChangedAfterApply,
}

/// <summary>
/// Una modifica persistente visibile nel Centro ripristino. Mantiene separati
/// gli snapshot creati da Nexus dalle configurazioni trovate già attive.
/// </summary>
public sealed class ActiveChangeVm : ObservableBase
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    internal ActiveChangeVm(
        OptimizerAction action,
        ActiveChangeState state,
        OptimizerInspection inspection,
        int snapshotCount,
        DateTime? appliedAtUtc,
        Action selectionChanged)
    {
        Action = action;
        State = state;
        Inspection = inspection;
        SnapshotCount = snapshotCount;
        AppliedAtUtc = appliedAtUtc;
        _selectionChanged = selectionChanged;
    }

    internal OptimizerAction Action { get; }
    internal OptimizerInspection Inspection { get; }
    public ActiveChangeState State { get; }
    public int SnapshotCount { get; }
    public DateTime? AppliedAtUtc { get; }
    public string Title => Action.Title;
    public string Detail => Action.Detail;
    public string IconKind => Action.IconKind;
    public string MeasureText => Inspection.MeasureText;

    public bool IsSystemCore => State is ActiveChangeState.SystemCore;
    public bool IsDetected => State is ActiveChangeState.Detected;
    public bool IsChangedAfterApply => State is ActiveChangeState.ChangedAfterApply;
    public bool CanSelectRecommended => IsDetected && Action.CanResetToRecommendedDefaults;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var accepted = value && CanSelectRecommended;
            if (!Set(ref _isSelected, accepted)) return;
            _selectionChanged();
        }
    }

    public string StateText => Locale.T(State switch
    {
        ActiveChangeState.SystemCore => "restore.state.systemcore",
        ActiveChangeState.Detected => "restore.state.detected",
        _ => "restore.state.changed",
    });

    public string SourceText => Locale.T(State switch
    {
        ActiveChangeState.SystemCore => "restore.source.systemcore",
        ActiveChangeState.Detected => "restore.source.unknown",
        _ => "restore.source.changed",
    });

    public string MetaText
    {
        get
        {
            if (AppliedAtUtc is DateTime applied)
            {
                var date = applied.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                return Locale.F("restore.meta.snapshot", [date, Formatter.Count(SnapshotCount)]);
            }

            return Locale.T("restore.meta.detected");
        }
    }

    public string ActionText => Locale.T(IsDetected
        ? "restore.action.recommended.one"
        : IsChangedAfterApply
            ? "restore.action.review.one"
            : "restore.action.undo.one");

    public WpfBrush Accent => State switch
    {
        ActiveChangeState.SystemCore => WpfBrushes.MediumSeaGreen,
        ActiveChangeState.Detected => WpfBrushes.Goldenrod,
        _ => WpfBrushes.IndianRed,
    };

    public void RefreshLocalized()
    {
        Raise(nameof(Title));
        Raise(nameof(Detail));
        Raise(nameof(StateText));
        Raise(nameof(SourceText));
        Raise(nameof(MetaText));
        Raise(nameof(ActionText));
    }
}

/// <summary>
/// Centro modifiche e ripristino: unisce gli undo esatti dell'Optimizer, la
/// scansione guidata delle impostazioni note e la quarantena delle pulizie.
/// </summary>
public sealed class HistoryViewModel : ObservableBase, IPageLifecycle
{
    private readonly SafetyEngine _safety;
    private readonly OptimizerEngine _optimizer;
    private HistoryOperationVm? _selected;
    private bool _busy;
    private bool _changesLoaded;
    private string _status = string.Empty;
    private string _changesStatus = Locale.T("restore.scan.ready");

    public HistoryViewModel(SafetyEngine safety, OptimizerEngine optimizer)
    {
        _safety = safety;
        _optimizer = optimizer;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
        UndoCommand = new RelayCommand(_ => _ = UndoSelectedAsync(), _ => CanUndo);
        Locale.Changed += OnLocaleChanged;
        RefreshHistory();
    }

    public ObservableCollection<ActiveChangeVm> ActiveChanges { get; } = [];
    public ObservableCollection<HistoryOperationVm> Rows { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand UndoCommand { get; }

    public string Title => Locale.T("restore.title");
    public string Subtitle => Locale.T("restore.subtitle");
    public string PrivacyNote => Locale.T("history.privacy");
    public string ChangesStatus { get => _changesStatus; private set => Set(ref _changesStatus, value); }

    public int SystemCoreCount => ActiveChanges.Count(item => item.IsSystemCore);
    public int DetectedCount => ActiveChanges.Count(item => item.IsDetected);
    public int ChangedCount => ActiveChanges.Count(item => item.IsChangedAfterApply);
    public int TotalActiveCount => ActiveChanges.Count;
    public int RecommendedSelectedCount => ActiveChanges.Count(item => item.IsSelected && item.CanSelectRecommended);
    public bool HasActiveChanges => TotalActiveCount > 0;
    public bool HasNoActiveChanges => !HasActiveChanges;
    public bool HasChangedItems => ChangedCount > 0;
    public bool CanRestoreSystemCore => !IsBusy && SystemCoreCount > 0;
    public bool CanRestoreRecommended => !IsBusy && RecommendedSelectedCount > 0;

    public string HeroCountText => Formatter.Count(TotalActiveCount);
    public string HeroStatusText => Locale.T(TotalActiveCount == 0
        ? "restore.hero.clean"
        : TotalActiveCount == 1 ? "restore.hero.one" : "restore.hero.many");
    public string SystemCoreCountText => Formatter.Count(SystemCoreCount);
    public string DetectedCountText => Formatter.Count(DetectedCount);
    public string ChangedCountText => Formatter.Count(ChangedCount);
    public string RecommendedButtonText => Locale.F(
        "restore.action.recommended.selected", [Formatter.Count(RecommendedSelectedCount)]);

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            RaiseSummary();
            RefreshCommand.RaiseCanExecute();
            UndoCommand.RaiseCanExecute();
        }
    }

    public HistoryOperationVm? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(CanUndo));
            UndoCommand.RaiseCanExecute();
        }
    }

    public bool CanUndo => !IsBusy && Selected?.CanUndo == true;

    public void Activate()
    {
        if (!_changesLoaded && !IsBusy) _ = RefreshAsync();
        else RefreshHistory();
    }

    public void Deactivate() { }

    /// <summary>Scansione sola lettura: non scrive registro, servizi o piani energetici.</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ChangesStatus = Locale.T("restore.scan.running");
        try
        {
            RefreshHistory();
            var found = new List<ActiveChangeVm>();
            foreach (var action in _optimizer.Actions.Where(item => item.IsReversible))
            {
                var inspection = await action.InspectAsync();
                var snapshots = _optimizer.StateOf(action.TrackingId);
                ActiveChangeState? state = snapshots.Count > 0
                    ? inspection.IsTargetState
                        ? ActiveChangeState.SystemCore
                        : ActiveChangeState.ChangedAfterApply
                    : inspection.HasAnyTargetState && action.CanResetToRecommendedDefaults
                        ? ActiveChangeState.Detected
                        : null;

                if (state is null) continue;
                found.Add(new ActiveChangeVm(
                    action,
                    state.Value,
                    inspection,
                    snapshots.Count,
                    snapshots.Select(entry => (DateTime?)entry.AppliedAtUtc).FirstOrDefault(),
                    OnRecommendedSelectionChanged));
            }

            ActiveChanges.Clear();
            foreach (var item in found) ActiveChanges.Add(item);
            _changesLoaded = true;
            ChangesStatus = Locale.F("restore.scan.done", [Formatter.Count(TotalActiveCount)]);
            RaiseSummary();
        }
        catch (Exception)
        {
            ChangesStatus = Locale.T("restore.scan.failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ripristina solo le modifiche ancora uguali a quelle applicate da Nexus.</summary>
    public async Task RestoreAllSystemCoreAsync()
    {
        if (!CanRestoreSystemCore) return;
        var targets = ActiveChanges.Where(item => item.IsSystemCore).ToArray();
        await RunRestoreBatchAsync(targets, useRecommendedDefaults: false);
    }

    public async Task RestoreSelectedRecommendedAsync()
    {
        if (!CanRestoreRecommended) return;
        var targets = ActiveChanges.Where(item => item.IsSelected && item.CanSelectRecommended).ToArray();
        await RunRestoreBatchAsync(targets, useRecommendedDefaults: true);
    }

    public async Task RestoreChangeAsync(ActiveChangeVm item)
    {
        if (IsBusy || !ActiveChanges.Contains(item)) return;
        await RunRestoreBatchAsync([item], useRecommendedDefaults: item.IsDetected);
    }

    private async Task RunRestoreBatchAsync(ActiveChangeVm[] targets, bool useRecommendedDefaults)
    {
        if (targets.Length == 0 || IsBusy) return;
        IsBusy = true;
        ChangesStatus = Locale.T("restore.running");
        var restored = 0;
        var failed = 0;
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    var outcome = useRecommendedDefaults
                        ? await item.Action.ResetToRecommendedDefaultsAsync()
                        : await item.Action.RevertAsync();
                    if (outcome.Changed) restored++;
                    else failed++;
                }
                catch (Exception)
                {
                    failed++;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
        ChangesStatus = Locale.F("restore.done", [Formatter.Count(restored), Formatter.Count(failed)]);
    }

    public async Task UndoSelectedAsync()
    {
        var selected = Selected;
        if (selected is null || !CanUndo) return;
        IsBusy = true;
        Status = Locale.T("history.undo.running");
        try
        {
            var result = await _safety.RestoreAsync(selected.Record.Id);
            Status = Locale.T("history.undo.done")
                .Replace("{0}", result.RestoredItems.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", result.SkippedItems.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
            RefreshHistory(keepStatus: true);
        }
        catch (OperationCanceledException)
        {
            Status = Locale.T("history.undo.cancel");
        }
        catch (Exception)
        {
            Status = Locale.T("history.undo.error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshHistory(bool keepStatus = false)
    {
        var selectedId = Selected?.Record.Id;
        Rows.Clear();
        foreach (var operation in _safety.GetHistory())
            Rows.Add(new HistoryOperationVm(operation));
        Selected = selectedId is Guid id ? Rows.FirstOrDefault(row => row.Record.Id == id) : null;
        if (!keepStatus) Status = Rows.Count == 0 ? Locale.T("history.empty") : string.Empty;
    }

    private void OnRecommendedSelectionChanged() => RaiseSummary();

    private void RaiseSummary()
    {
        Raise(nameof(SystemCoreCount));
        Raise(nameof(DetectedCount));
        Raise(nameof(ChangedCount));
        Raise(nameof(TotalActiveCount));
        Raise(nameof(RecommendedSelectedCount));
        Raise(nameof(HasActiveChanges));
        Raise(nameof(HasNoActiveChanges));
        Raise(nameof(HasChangedItems));
        Raise(nameof(CanRestoreSystemCore));
        Raise(nameof(CanRestoreRecommended));
        Raise(nameof(HeroCountText));
        Raise(nameof(HeroStatusText));
        Raise(nameof(SystemCoreCountText));
        Raise(nameof(DetectedCountText));
        Raise(nameof(ChangedCountText));
        Raise(nameof(RecommendedButtonText));
        Raise(nameof(CanUndo));
        UndoCommand.RaiseCanExecute();
    }

    private void OnLocaleChanged()
    {
        foreach (var row in Rows) row.RefreshLocalized();
        foreach (var change in ActiveChanges) change.RefreshLocalized();
        Raise(nameof(Title));
        Raise(nameof(Subtitle));
        Raise(nameof(PrivacyNote));
        RaiseSummary();
    }
}
