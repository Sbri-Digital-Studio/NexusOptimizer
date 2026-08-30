using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using NexusOptimizer.App.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

public sealed class StartupRowVm(StartupEntry entry) : ObservableBase
{
    public StartupEntry Entry { get; } = entry;
    private string? _runtimeText;
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Publisher => Entry.Publisher;
    public string Source => Entry.Source;
    public bool IsEnabled => Entry.IsEnabled;
    public bool CanModify => Entry.CanModify;
    /// <summary>Stato dell'avvio con Windows, non stato della finestra/processo.</summary>
    public string StateText => IsEnabled ? "ABILITATO" : "DISABILITATO";
    public string AccessText => CanModify ? "Reversibile" : "Sola lettura";
    public WpfBrush StateBrush => IsEnabled ? WpfBrushes.MediumSeaGreen : WpfBrushes.Goldenrod;

    /// <summary>Stato istantaneo del processo associato alla voce di avvio.</summary>
    public string RuntimeText => _runtimeText ??= DetectRuntimeState();

    public WpfBrush RuntimeBrush
    {
        get
        {
            return RuntimeText switch
            {
                "IN ESECUZIONE" => WpfBrushes.MediumSeaGreen,
                "CHIUSO" => WpfBrushes.Gray,
                _ => WpfBrushes.Goldenrod,
            };
        }
    }

    internal void RefreshRuntime()
    {
        _runtimeText = null;
        Raise(nameof(RuntimeText));
        Raise(nameof(RuntimeBrush));
    }

    private string DetectRuntimeState()
    {
        var executable = StartupService.ExtractExecutablePath(Command);
        if (string.IsNullOrWhiteSpace(executable)) return "—";
        var processName = Path.GetFileNameWithoutExtension(executable);
        if (string.IsNullOrWhiteSpace(processName)) return "—";
        try
        {
            return Process.GetProcessesByName(processName).Length > 0
                ? "IN ESECUZIONE"
                : "CHIUSO";
        }
        catch
        {
            return "N.D.";
        }
    }
}

public sealed class StartupViewModel : ObservableBase, IPageLifecycle
{
    private readonly StartupService _service;
    private IReadOnlyList<StartupEntry> _all = [];
    private StartupRowVm? _selected;
    private bool _busy;
    private string _query = "";
    private string _status = "Pronto";

    public ObservableCollection<StartupRowVm> Rows { get; } = [];
    public RelayCommand RefreshCommand { get; }

    public StartupViewModel(StartupService service)
    {
        _service = service;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
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
            Raise(nameof(CanToggle));
            RefreshCommand.RaiseCanExecute();
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public StartupRowVm? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(CanToggle));
            Raise(nameof(ActionLabel));
        }
    }

    public bool CanToggle => !IsBusy && Selected?.CanModify == true;
    public string ActionLabel => Selected?.IsEnabled == false ? "RIATTIVA" : "DISABILITA";

    public void Activate() => _ = RefreshAsync();
    public void Deactivate() { }

    public async Task ToggleSelectedAsync()
    {
        var entry = Selected?.Entry;
        if (entry is null || !CanToggle) return;
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                if (entry.IsEnabled) _service.Disable(entry);
                else _service.Enable(entry);
            });
            Status = Locale.T(entry.IsEnabled ? "startup.done.disabled" : "startup.done.enabled");
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            Status = $"Operazione non completata: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await RefreshCoreAsync(); }
        catch (Exception) { Status = "Impossibile leggere le voci di avvio."; }
        finally { IsBusy = false; }
    }

    private async Task RefreshCoreAsync()
    {
        _all = await Task.Run(_service.Collect);
        ApplyFilter();
        var editable = _all.Count(item => item.CanModify);
        Status = $"{_all.Count:N0} voci rilevate · {editable:N0} gestibili senza privilegi amministrativi";
    }

    private void ApplyFilter()
    {
        var needle = Query.Trim();
        var selectedId = Selected?.Id;
        Rows.Clear();
        foreach (var item in _all.Where(item => needle.Length == 0
                     || item.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                     || item.Publisher.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                     || item.Command.Contains(needle, StringComparison.CurrentCultureIgnoreCase)))
            Rows.Add(new StartupRowVm(item));
        Selected = selectedId is null ? null : Rows.FirstOrDefault(item => item.Id == selectedId);
    }
}
