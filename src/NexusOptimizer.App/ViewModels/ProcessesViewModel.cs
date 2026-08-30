using System.Collections.ObjectModel;
using System.Windows.Threading;
using NexusOptimizer.App.Services;

namespace NexusOptimizer.App.ViewModels;

public sealed class ProcessRowVm(ProcessSnapshot snapshot)
{
    public int Pid { get; } = snapshot.Pid;
    public string Name { get; } = snapshot.Name;
    public string CpuText { get; } = snapshot.CpuPercent is double cpu ? $"{cpu:N1}%" : "—";
    public string MemoryText { get; } = Formatter.Bytes(snapshot.WorkingSetBytes);
    public int Threads { get; } = snapshot.ThreadCount;
}

public sealed class ProcessesViewModel : ObservableBase, IPageLifecycle, IDisposable
{
    private readonly ProcessService _service;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ProcessSnapshot> _all = [];
    private bool _busy;
    private string _query = "";
    private string _status = "Pronto";
    private ProcessRowVm? _selected;
    private ProcessDetails? _details;
    private bool _verifying;
    private bool _rebuildingRows;

    public ObservableCollection<ProcessRowVm> Rows { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand VerifyCommand { get; }

    public ProcessesViewModel(ProcessService service)
    {
        _service = service;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
        VerifyCommand = new RelayCommand(_ => _ = VerifyAsync(), _ => CanVerify);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? "")) ApplyFilter(); }
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            RefreshCommand.RaiseCanExecute();
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public ProcessRowVm? Selected
    {
        get => _selected;
        set
        {
            // Rows.Clear() fa azzerare temporaneamente SelectedItem al ListBox.
            // Durante il refresh conserviamo invece PID e dettagli correnti.
            if (_rebuildingRows && value is null) return;
            if (!Set(ref _selected, value)) return;
            Details = null;
            VerifyCommand.RaiseCanExecute();
            if (value is not null) _ = LoadDetailsAsync(value.Pid, verify: false);
        }
    }

    public ProcessDetails? Details
    {
        get => _details;
        private set
        {
            if (!Set(ref _details, value)) return;
            Raise(nameof(HasDetails));
            VerifyCommand.RaiseCanExecute();
        }
    }

    public bool HasDetails => Details is not null;
    public bool CanVerify => !_verifying && Selected is not null && Details?.Path is not (null or "" or "—");

    public void Activate()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Deactivate() => _timer.Stop();

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _all = await Task.Run(_service.Collect);
            ApplyFilter();
            Status = $"{Rows.Count:N0} processi visibili · aggiornamento automatico ogni 3 s";
        }
        catch (Exception)
        {
            Status = "Impossibile leggere l'elenco processi.";
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        var needle = Query.Trim();
        var selectedPid = Selected?.Pid;
        _rebuildingRows = true;
        try
        {
            Rows.Clear();
            foreach (var process in _all.Where(row => needle.Length == 0
                         || row.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                         || row.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)
                             .Contains(needle, StringComparison.Ordinal)))
                Rows.Add(new ProcessRowVm(process));
        }
        finally { _rebuildingRows = false; }
        var restored = selectedPid.HasValue
            ? Rows.FirstOrDefault(row => row.Pid == selectedPid.Value)
            : null;
        if (restored is null && selectedPid.HasValue)
            Details = null;
        _selected = restored;
        Raise(nameof(Selected));
        VerifyCommand.RaiseCanExecute();
    }

    private async Task LoadDetailsAsync(int pid, bool verify)
    {
        try
        {
            var details = await Task.Run(() => _service.CollectDetails(pid, verify));
            if (Selected?.Pid == pid) Details = details;
        }
        catch (Exception) { Details = null; }
    }

    private async Task VerifyAsync()
    {
        var pid = Selected?.Pid;
        if (!pid.HasValue || !CanVerify) return;
        _verifying = true;
        VerifyCommand.RaiseCanExecute();
        Status = Locale.T("proc.verify.running");
        await LoadDetailsAsync(pid.Value, verify: true);
        _verifying = false;
        VerifyCommand.RaiseCanExecute();
        Status = Locale.T("proc.verify.done");
    }

    public void Dispose() => _timer.Stop();
}
