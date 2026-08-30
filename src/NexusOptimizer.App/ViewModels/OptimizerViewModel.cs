using System.Collections.ObjectModel;
using System.Windows.Input;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>
/// Riga dell'Optimizer: mostra lo stato reale dell'ottimizzazione sul sistema e
/// permette di applicarla o annullarla singolarmente.
/// </summary>
public sealed class OptimizerActionVm : ObservableBase
{
    private readonly OptimizerAction _action;
    private readonly RelayCommand _applyCommand;
    private readonly RelayCommand _revertCommand;
    private bool _isSelected = true;
    private bool _isBusy;
    private bool _isApplied;
    private bool _canApply = true;
    private bool _isUnlocked = true;
    private string _stateText = "Analisi in corso…";
    private string _measureText = "";
    private string _resultText = "";

    public OptimizerActionVm(OptimizerAction action, WpfBrush accent, Action<OptimizerActionVm> changed)
    {
        _action = action;
        Accent = accent;
        _applyCommand = new RelayCommand(_ => _ = ApplyAsync(), _ => !IsBusy && CanApply && IsUnlocked);
        _revertCommand = new RelayCommand(_ => _ = RevertAsync(), _ => !IsBusy && IsApplied && action.IsReversible);
        Changed = changed;
    }

    private Action<OptimizerActionVm> Changed { get; }

    public string Id => _action.Id;
    public string Title => _action.Title;
    public string Detail => _action.Detail;
    public string Benefit => _action.BenefitText;
    public string Risk => _action.RiskText;
    public string IconKind => _action.IconKind;
    public string TargetId => _action.TargetId;
    public bool IsReversible => _action.IsReversible;
    public bool CanResetToRecommendedDefaults => _action.CanResetToRecommendedDefaults;
    public string TrackingId => _action.TrackingId;
    public WpfBrush Accent { get; }

    /// <summary>Beneficio alto = verde: è un vantaggio, non un allarme.</summary>
    public WpfBrush BenefitBrush => _action.Benefit switch
    {
        OptimizerImpact.High => WpfBrushes.MediumSeaGreen,
        OptimizerImpact.Medium => WpfBrushes.Goldenrod,
        _ => WpfBrushes.LightSlateGray,
    };

    public WpfBrush RiskBrush => _action.Risk switch
    {
        OptimizerImpact.High => WpfBrushes.IndianRed,
        OptimizerImpact.Medium => WpfBrushes.Goldenrod,
        _ => WpfBrushes.MediumSeaGreen,
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // La selezione viene condivisa tra pagina Optimizer e Dashboard.
            // Una vista compatta o un binding non devono poter aggirare il livello attivo.
            var allowedValue = value && IsUnlocked;
            if (Set(ref _isSelected, allowedValue)) Changed(this);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) RaiseCommands(); }
    }

    public bool IsApplied
    {
        get => _isApplied;
        private set
        {
            if (!Set(ref _isApplied, value)) return;
            Raise(nameof(StateBrush));
            Raise(nameof(CanRevertNow));
            RaiseCommands();
        }
    }

    public bool CanApply
    {
        get => _canApply;
        private set { if (Set(ref _canApply, value)) RaiseCommands(); }
    }

    /// <summary>
    /// Il livello operativo comprende questa azione? Le voci bloccate restano
    /// visibili con il motivo: nascondere una funzione è meno onesto che spiegare
    /// perché non è disponibile.
    /// </summary>
    public bool IsUnlocked
    {
        get => _isUnlocked;
        internal set
        {
            if (!Set(ref _isUnlocked, value)) return;
            Raise(nameof(IsLocked));
            Raise(nameof(LockText));
            if (!value) IsSelected = false;
            Raise(nameof(CanRevertNow));
            RaiseCommands();
        }
    }

    public bool IsLocked => !IsUnlocked;

    /// <summary>
    /// L'annullamento non è mai bloccato dal livello: se un'ottimizzazione è già
    /// applicata deve poter essere revocata anche dopo essere tornati a SAFE.
    /// </summary>
    public bool CanRevertNow => IsApplied && IsReversible;

    public string LockText => Locale.F("opt.lock.requires", [MinimumLevel.ToDisplayName()]);

    public AppModeLevel MinimumLevel => _action.MinimumLevel;

    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    public string MeasureText { get => _measureText; private set => Set(ref _measureText, value); }

    public string ResultText
    {
        get => _resultText;
        private set { if (Set(ref _resultText, value)) Raise(nameof(HasResult)); }
    }

    public bool HasResult => _resultText.Length > 0;

    public WpfBrush StateBrush => IsApplied ? WpfBrushes.MediumSeaGreen : WpfBrushes.LightSlateGray;

    public ICommand ApplyCommand => _applyCommand;
    public ICommand RevertCommand => _revertCommand;

    public async Task InspectAsync()
    {
        IsBusy = true;
        try
        {
            var inspection = await _action.InspectAsync();
            IsApplied = inspection.IsApplied;
            StateText = inspection.StateText;
            MeasureText = inspection.MeasureText;
            CanApply = inspection.CanApply;
        }
        catch (Exception)
        {
            StateText = Locale.T("opt.state.unreadable");
            CanApply = false;
        }
        finally { IsBusy = false; }
    }

    public async Task<string> ApplyAsync()
    {
        if (IsBusy || !CanApply || !IsUnlocked) return "";
        IsBusy = true;
        try
        {
            var outcome = await _action.ApplyAsync();
            ResultText = outcome.Message;
            await InspectAsync();
            if (outcome.Changed && _action.IsReversible) IsApplied = true;
            return outcome.Message;
        }
        catch (Exception)
        {
            ResultText = Locale.T("opt.apply.failed");
            return ResultText;
        }
        finally { IsBusy = false; }
    }

    public async Task<string> RevertAsync()
    {
        if (IsBusy) return "";
        IsBusy = true;
        try
        {
            var outcome = await _action.RevertAsync();
            ResultText = outcome.Message;
            await InspectAsync();
            return outcome.Message;
        }
        catch (Exception)
        {
            ResultText = Locale.T("opt.revert.failed");
            return ResultText;
        }
        finally { IsBusy = false; }
    }

    private void RaiseCommands()
    {
        _applyCommand.RaiseCanExecute();
        _revertCommand.RaiseCanExecute();
        Raise(nameof(IsBusy));
    }
}

/// <summary>
/// Piano di ottimizzazione reale: ogni voce viene ispezionata sul sistema, si
/// applica solo su richiesta e — dove ha senso — si annulla riportando lo stato
/// precedente. Nessuna azione parte in automatico all'apertura della pagina.
/// </summary>
public sealed class OptimizerViewModel : ObservableBase, IPageLifecycle
{
    private readonly OptimizerEngine _engine;
    private readonly RelayCommand _applySelectedCommand;
    private readonly RelayCommand _refreshCommand;
    private bool _busy;
    private bool _inspected;
    private string _status = Locale.T("opt.status.scanning");

    public OptimizerViewModel(OptimizerEngine engine)
    {
        _engine = engine;
        OpenCommand = new RelayCommand(target =>
        {
            if (target is string id && id.Length > 0) NavigateRequested?.Invoke(id);
        });
        _refreshCommand = new RelayCommand(_ => _ = InspectAllAsync(), _ => !IsBusy);
        _applySelectedCommand = new RelayCommand(_ => _ = ApplySelectedAsync(), _ => !IsBusy && SelectedCount > 0);

        var accents = new Dictionary<string, WpfBrush>(StringComparer.Ordinal)
        {
            ["startup"] = WpfBrushes.Goldenrod,
            ["cache"] = WpfBrushes.MediumSeaGreen,
            ["windows"] = WpfBrushes.DeepSkyBlue,
            ["memory"] = WpfBrushes.MediumPurple,
            ["visual"] = WpfBrushes.CornflowerBlue,
        };

        Items = [];
        foreach (var action in engine.Actions)
        {
            Items.Add(new OptimizerActionVm(action,
                accents.GetValueOrDefault(action.Id, WpfBrushes.LightSlateGray),
                OnItemChanged));
        }

        ApplyLevel();
        _engine.LevelChanged += OnLevelChanged;
    }

    /// <summary>Sblocca o blocca le voci in base al livello scelto dall'utente.</summary>
    private void ApplyLevel()
    {
        foreach (var item in Items) item.IsUnlocked = _engine.IsUnlocked(ActionOf(item));
        Raise(nameof(LevelSummary));
        OnItemChanged(null);
    }

    private OptimizerAction ActionOf(OptimizerActionVm item)
        => _engine.Actions.First(action => action.Id == item.Id);

    private void OnLevelChanged()
    {
        ApplyLevel();
        Raise(nameof(LevelSummary));
    }

    /// <summary>Riga informativa sul livello attivo e su cosa sblocca.</summary>
    public string LevelSummary
    {
        get
        {
            var unlocked = Items.Count(item => item.IsUnlocked);
            var locked = Items.Count - unlocked;
            return locked == 0
                ? Locale.F("opt.level.all", [_engine.Level.ToDisplayName(), Formatter.Count(Items.Count)])
                : Locale.F("opt.level.partial",
                    [_engine.Level.ToDisplayName(), Formatter.Count(unlocked),
                     Locale.P(locked, "opt.level.locked.one", "opt.level.locked.many")]);
        }
    }

    public event Action<string>? NavigateRequested;

    public ObservableCollection<OptimizerActionVm> Items { get; }
    public ICommand OpenCommand { get; }
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Nome storico usato anche dalla card della dashboard.</summary>
    public ICommand PrepareCommand => _applySelectedCommand;
    public ICommand ApplySelectedCommand => _applySelectedCommand;

    public int SelectedCount => Items.Count(item => item.IsSelected && item.CanApply && item.IsUnlocked);
    public string PrepareButtonText => IsBusy
        ? Locale.T("opt.button.applying")
        : Locale.F("opt.button.apply", [Formatter.Count(SelectedCount)]);

    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            Raise(nameof(PrepareButtonText));
            _applySelectedCommand.RaiseCanExecute();
            _refreshCommand.RaiseCanExecute();
        }
    }

    public void Activate()
    {
        if (!_inspected && !IsBusy) _ = InspectAllAsync();
    }

    public void Deactivate() { }

    /// <summary>Rilegge lo stato di tutte le voci senza modificare nulla.</summary>
    public async Task InspectAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = Locale.T("opt.status.scanning");
        try
        {
            // L'ispezione è sola lettura: si esegue anche sulle voci bloccate, così
            // una modifica applicata in passato resta visibile e annullabile.
            foreach (var item in Items) await item.InspectAsync();
            _inspected = true;
            var applied = Items.Count(i => i.IsApplied);
            var available = Items.Count(i => i.CanApply && i.IsUnlocked);
            Status = available == 0
                ? Locale.T("opt.status.aligned")
                : Locale.F("opt.status.available",
                    [Locale.P(available, "opt.status.available.one", "opt.status.available.many"),
                     Formatter.Count(applied)]);
        }
        finally
        {
            IsBusy = false;
            OnItemChanged(null);
        }
    }

    private async Task ApplySelectedAsync()
    {
        if (IsBusy) return;
        var selected = Items.Where(item => item.IsSelected && item.CanApply && item.IsUnlocked).ToArray();
        if (selected.Length == 0) return;

        IsBusy = true;
        Status = Locale.F("opt.status.applying", [Formatter.Count(selected.Length)]);
        var applied = 0;
        try
        {
            foreach (var item in selected)
            {
                var message = await item.ApplyAsync();
                if (message.Length > 0) applied++;
            }
            _engine.Persist();
            Status = applied == 0
                ? Locale.T("opt.status.nochange")
                : Locale.F("opt.status.done",
                    [Locale.P(applied, "opt.status.done.one", "opt.status.done.many")]);
        }
        finally
        {
            IsBusy = false;
            OnItemChanged(null);
        }
    }

    private void OnItemChanged(OptimizerActionVm? _)
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(PrepareButtonText));
        _applySelectedCommand.RaiseCanExecute();
    }
}
