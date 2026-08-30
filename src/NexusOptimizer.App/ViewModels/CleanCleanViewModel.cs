using System.Windows.Input;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using System.Globalization;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Safety;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Riga categoria: selezionabile, con livello, dimensione rilevata ed esito.</summary>
public sealed class CleanCategoryVm : ObservableBase
{
    public CleanCategoryDef Def { get; }
    public CleanCategoryVm(CleanCategoryDef def)
    {
        Def = def;
        _selected = def.SelectedByDefault;
    }

    public string Id => Def.Id;
    public string Name => Locale.T("cat." + Def.Id);
    public SecurityLevel Level => Def.Level;
    public bool RequiresAdmin => Def.RequiresAdmin;
    public bool IsRed => Def.Level == SecurityLevel.Red;
    public string AdminNotice => RequiresAdmin ? "· amministratore" : string.Empty;
    public WpfBrush LevelBrush => Def.Level switch
    {
        SecurityLevel.Green => WpfBrushes.MediumSeaGreen,
        SecurityLevel.Yellow => WpfBrushes.Goldenrod,
        _ => WpfBrushes.IndianRed,
    };

    internal Action? SelectionChanged { get; set; }

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            // MAI selezionare di default categorie Red; l'utente può forzarle coscientemente.
            if (_selected == value) return;
            _selected = value;
            Raise();
            SelectionChanged?.Invoke();
        }
    }

    private bool _analyzed;
    public bool Analyzed { get => _analyzed; private set { _analyzed = value; Raise(); } }

    private long _bytes;
    public long Bytes { get => _bytes; private set { _bytes = value; Raise(); Raise(nameof(SizeText)); } }

    private int _files;
    public int Files { get => _files; private set { _files = value; Raise(); } }

    public string SizeText => Formatter.Bytes(_bytes);
    public string LevelText => Locale.T("cat.level." + Def.Level.ToString().ToLowerInvariant());

    internal void RefreshLocalized()
    {
        Raise(nameof(Name));
        Raise(nameof(LevelText));
    }

    internal void SetResult(long bytes, int files)
    {
        Bytes = bytes;
        Files = files;
        Analyzed = true;
    }
}

/// <summary>
/// Smart Clean: analisi asincrona e cancellabile, anteprima per categoria,
/// Dry Run obbligatorio di default, eliminazione reale SOLO sotto conferma e
/// su categorie non protette (Red/Admin sono escluse o chiaramente segnalate).
/// </summary>
public sealed class CleanCleanViewModel : ObservableBase, IDisposable
{
    private readonly CleanExecutor _executor;
    private readonly AppConfig _cfg;
    private readonly CleanScanner _scanner;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<CleanCategoryVm> Categories { get; }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Raise(); Raise(nameof(CanRun)); Raise(nameof(CanDelete)); RaiseCommands(); } }

    private bool _dryRun = true;
    public bool DryRun { get => _dryRun; set { if (_dryRun == value) return; _dryRun = value; Raise(); } }

    private string _status = "";
    public string Status
    {
        get => _status;
        private set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(StatusText));
        }
    }

    private long _recoverable;
    public long Recoverable { get => _recoverable; private set { _recoverable = value; Raise(); Raise(nameof(RecoverableText)); } }
    public string RecoverableText => Formatter.Bytes(_recoverable);

    public string PageTitle => Locale.T("clean.title");
    public string PageSub => Locale.T("clean.sub");
    public string LblRecover => Locale.T("clean.recoverable");
    public string BtnAnalyze => Locale.T("clean.btn.analyze");
    public string BtnClean => Locale.T("clean.btn.run");
    public string BtnDelete => Locale.T("clean.btn.delete");
    public string BtnDry => Locale.T("clean.btn.dry");
    public string DryRunNote => Locale.T("clean.dry.note");
    public string StatusText => string.IsNullOrWhiteSpace(Status) ? Locale.T("clean.ready") : Status;
    public bool HasScan => _lastScan is not null;
    public bool HasSelection => Categories.Any(c => c.IsSelected);
    public bool CanRun => !IsBusy && HasSelection;
    public bool CanDelete => !IsBusy && HasScan && HasSelection;

    public ICommand AnalyzeCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    public void CommandReset()
    {
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (AnalyzeCommand as RelayCommand)?.RaiseCanExecute();
        (RunCommand as RelayCommand)?.RaiseCanExecute();
        (CancelCommand as RelayCommand)?.RaiseCanExecute();
    }

    private ScanResult? _lastScan;

    public CleanCleanViewModel(AppConfig cfg, SafetyEngine safety)
    {
        _cfg = cfg;
        _scanner = new CleanScanner(cfg.Exclusions);
        _executor = new CleanExecutor(cfg.Exclusions, safety);
        var categories = CleanCatalog.Categories.Select(c => new CleanCategoryVm(c)).ToList();
        foreach (var category in categories)
            category.SelectionChanged = OnCategorySelectionChanged;
        Categories = categories;

        AnalyzeCommand = new RelayCommand(_ => _ = AnalyzeAsync(), _ => !IsBusy && HasSelection);
        RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => CanRun);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        Locale.Changed += OnLocaleChanged;
    }

    private void OnCategorySelectionChanged()
    {
        // Modificando le categorie, il risultato precedente non e' piu' una
        // anteprima valida: richiediamo una nuova analisi prima della pulizia.
        _lastScan = null;
        Recoverable = 0;
        Status = Locale.T("clean.ready");
        Raise(nameof(HasScan));
        Raise(nameof(HasSelection));
        Raise(nameof(CanRun));
        Raise(nameof(CanDelete));
        RaiseCommands();
    }

    private void OnLocaleChanged()
    {
        foreach (var category in Categories)
            category.RefreshLocalized();
        Raise(nameof(PageTitle)); Raise(nameof(PageSub)); Raise(nameof(LblRecover));
        Raise(nameof(BtnAnalyze)); Raise(nameof(BtnClean)); Raise(nameof(BtnDelete)); Raise(nameof(BtnDry)); Raise(nameof(DryRunNote)); Raise(nameof(StatusText));
    }

    public async Task AnalyzeAsync()
    {
        if (IsBusy) return;
        var selected = Categories.Where(c => c.IsSelected).Select(c => c.Def).ToList();
        if (selected.Count == 0) { Status = Locale.T("clean.nosel"); return; }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = Locale.T("clean.scanning");
        try
        {
            var progress = new Progress<CleanScanner.ScanProgress>(p => Status = p.CurrentPath);
            var scan = await _scanner.ScanAsync(selected, progress, _cts.Token);

            foreach (var c in Categories)
            {
                var r = scan.Categories.FirstOrDefault(x => x.Category.Id == c.Id);
                c.SetResult(r?.TotalBytes ?? 0, r?.Items.Count ?? 0);
            }
            Recoverable = scan.Categories.Sum(x => x.TotalBytes);
            _lastScan = scan;
            Raise(nameof(HasScan)); Raise(nameof(CanRun)); Raise(nameof(CanDelete));
            Status = Locale.T("clean.scan.done");
        }
        catch (OperationCanceledException)
        {
            Status = Locale.T("clean.cancel");
        }
        catch (Exception)
        {
            // Un percorso può sparire o diventare non accessibile durante la
            // scansione: l'errore resta nella pagina senza chiudere l'app.
            _lastScan = null;
            Recoverable = 0;
            Raise(nameof(HasScan)); Raise(nameof(CanDelete));
            Status = Locale.T("clean.scan.error");
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(Recoverable));
        }
    }

    public Task RunAsync() => ExecuteAsync(DryRun);

    /// <summary>Esegue la pulizia reale spostando gli elementi selezionati nel Cestino.</summary>
    public Task DeleteAsync() => ExecuteAsync(dryRun: false);

    private async Task ExecuteAsync(bool dryRun)
    {
        if (_lastScan is null || IsBusy) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = dryRun ? Locale.T("clean.dry.running") : Locale.T("clean.run.running");
        try
        {
            var progress = new Progress<CleanExecutor.CleanProgress>(p =>
                Status = $"{p.Processed} · {Formatter.Bytes(p.BytesFreed)}");
            var result = await _executor.RunAsync(_lastScan,
                new CleanOptions
                {
                    DryRun = dryRun,
                    UseRecycleBin = false,
                    UseQuarantine = !dryRun,
                    Exclusions = _cfg.Exclusions,
                },
                progress, _cts.Token);
            Recoverable = result.BytesFreed;
            var statusKey = dryRun ? "clean.dry.done"
                : result.ErrorMessages.Count == 0 ? "clean.run.done" : "clean.run.partial";
            Status = FormatStatus(statusKey, result.ItemsRemoved, result.BytesFreed);
            if (!dryRun) _lastScan = null;
            Raise(nameof(HasScan)); Raise(nameof(CanRun)); Raise(nameof(CanDelete));
        }
        catch (OperationCanceledException)
        {
            Status = Locale.T("clean.cancel");
        }
        catch (Exception)
        {
            Status = Locale.T("clean.run.error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        Locale.Changed -= OnLocaleChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private static string FormatStatus(string key, int items, long bytes)
        => Locale.T(key)
            .Replace("{0}", items.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{1}", Formatter.Bytes(bytes), StringComparison.Ordinal);
}
