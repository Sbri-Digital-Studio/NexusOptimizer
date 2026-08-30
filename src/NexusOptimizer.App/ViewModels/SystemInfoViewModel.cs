using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Globalization;
using NexusOptimizer.App.Services;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Riquadro di sintesi in cima a "Il mio PC": una sezione, un dato chiave.</summary>
public sealed record SystemInfoHighlight(string Label, string Value, string IconKind,
                                         System.Windows.Media.Brush Accent);

/// <summary>Pagina "Il mio PC": carica le informazioni WMI fuori dal thread UI.</summary>
public sealed class SystemInfoViewModel : ObservableBase
{
    private readonly Services.SystemInfoService _service;
    private bool _loaded;
    private bool _loading;
    private string _status = "";

    public ObservableCollection<Services.SystemInfoSection> Sections { get; } = [];

    /// <summary>Le quattro voci hardware mostrate nell'intestazione della pagina.</summary>
    public ObservableCollection<SystemInfoHighlight> Highlights { get; } = [];

    private string _heroTitle = Environment.MachineName;
    public string HeroTitle { get => _heroTitle; private set => Set(ref _heroTitle, value); }

    private string _heroSubtitle = "Lettura hardware in corso…";
    public string HeroSubtitle { get => _heroSubtitle; private set => Set(ref _heroSubtitle, value); }
    public bool IsLoading { get => _loading; private set { _loading = value; Raise(); Raise(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsLoading;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string PageTitle => Locale.T("sysinfo.title");
    public string PageSub => Locale.T("sysinfo.sub");
    public string RefreshLabel => Locale.T("sysinfo.refresh");

    public ICommand RefreshCommand { get; }

    public SystemInfoViewModel(Services.SystemInfoService service)
    {
        _service = service;
        RefreshCommand = new RelayCommand(_ => _ = LoadAsync(), _ => CanRefresh);
        Locale.Changed += OnLocaleChanged;
    }

    public async Task LoadIfNeededAsync()
    {
        if (!_loaded) await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        Status = Locale.T("sysinfo.loading");
        try
        {
            var sections = await Task.Run(_service.Collect);
            Sections.Clear();
            foreach (var section in sections) Sections.Add(section);
            BuildHero(sections);
            _loaded = true;
            Status = Locale.T("sysinfo.done")
                .Replace("{0}", Sections.Count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
        }
        catch (Exception)
        {
            Status = Locale.T("sysinfo.error");
        }
        finally
        {
            IsLoading = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecute();
        }
    }

    /// <summary>
    /// L'intestazione riusa le sintesi già calcolate dalle sezioni: nessuna query
    /// WMI aggiuntiva, quindi nessun costo extra all'apertura della pagina.
    /// </summary>
    private void BuildHero(IReadOnlyList<Services.SystemInfoSection> sections)
    {
        Services.SystemInfoSection? Find(string title)
            => sections.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

        var system = Find("SISTEMA OPERATIVO");
        var name = system?.Rows.FirstOrDefault(r => r.Key == "Nome computer")?.Value;
        HeroTitle = string.IsNullOrWhiteSpace(name) || name == "—" ? Environment.MachineName : name;

        var board = Find("SCHEDA MADRE")?.Caption ?? "";
        HeroSubtitle = string.Join(" · ", new[] { system?.Caption ?? "", board }
            .Where(v => v.Length > 0 && v != "—"));

        Highlights.Clear();
        foreach (var title in new[] { "PROCESSORE", "SCHEDA VIDEO", "MEMORIA RAM", "ARCHIVIAZIONE" })
        {
            var section = Find(title);
            if (section is null || section.Caption.Length == 0) continue;
            Highlights.Add(new SystemInfoHighlight(section.Title, section.Caption,
                section.IconKind, section.Accent));
        }
    }

    private void OnLocaleChanged()
    {
        Raise(nameof(PageTitle)); Raise(nameof(PageSub)); Raise(nameof(RefreshLabel));
    }
}
