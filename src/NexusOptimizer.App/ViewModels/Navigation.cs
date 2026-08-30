using NexusOptimizer.App.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace NexusOptimizer.App.ViewModels;

/// <summary>
/// Voce della navigazione laterale. Testo e sottotitolo sono calcolati dal Locale:
/// il cambio lingua notifica le singole voci tramite Refresh() senza ricreare oggetti.
/// </summary>
public sealed class NavItem : ObservableBase
{
    private static readonly WpfBrush DashboardBrush = MakeBrush(0x6E, 0xA8, 0xFE);
    private static readonly WpfBrush CleanBrush = MakeBrush(0x58, 0xD6, 0xA8);
    private static readonly WpfBrush PerformanceBrush = MakeBrush(0xB7, 0x9C, 0xFF);
    private static readonly WpfBrush ProcessesBrush = MakeBrush(0xFF, 0x78, 0xA5);
    private static readonly WpfBrush StartupBrush = MakeBrush(0xFF, 0xB4, 0x54);
    private static readonly WpfBrush DiagnosticsBrush = MakeBrush(0x44, 0xD7, 0xC5);
    private static readonly WpfBrush SystemBrush = MakeBrush(0x57, 0xC7, 0xFF);
    private static readonly WpfBrush HistoryBrush = MakeBrush(0x74, 0xB7, 0xFF);
    private static readonly WpfBrush SettingsBrush = MakeBrush(0xA8, 0xB5, 0xC7);
    private static readonly WpfBrush DiskBrush = MakeBrush(0x54, 0xA8, 0xFF);
    private static readonly WpfBrush PrivacyBrush = MakeBrush(0x36, 0xD4, 0xA8);
    private static readonly WpfBrush ToolsBrush = MakeBrush(0xD0, 0xA3, 0xFF);
    private static readonly WpfBrush GamingBrush = MakeBrush(0x6B, 0xD9, 0x3D);
    private static readonly WpfBrush SoftwareBrush = MakeBrush(0xFF, 0x9E, 0x64);

    public string Id { get; }
    public string IconKind { get; }

    public NavItem(string id, string iconKind)
    {
        Id = id;
        IconKind = iconKind;
    }

    public string Title => Locale.T(Id);

    /// <summary>Colore funzionale per distinguere a colpo d'occhio le aree attive.</summary>
    public WpfBrush IconBrush => IconKind switch
    {
        "home" => DashboardBrush,
        "broom" => CleanBrush,
        "chart" => PerformanceBrush,
        "chip" => ProcessesBrush,
        "rocket" => StartupBrush,
        "pulse" => DiagnosticsBrush,
        "info" => SystemBrush,
        "monitor" => SystemBrush,
        "disk" => DiskBrush,
        "shield" => PrivacyBrush,
        "apps" => ToolsBrush,
        "gamepad" => GamingBrush,
        "installedApps" => SoftwareBrush,
        "history" => HistoryBrush,
        "restoreCenter" => HistoryBrush,
        _ => SettingsBrush,
    };

    /// <summary>Notifica la vista dopo un cambio lingua (le proprietà sono calcolate).</summary>
    public void Refresh()
    {
        Raise(nameof(Title));
        Raise(nameof(Subtitle));
        Raise(nameof(HasSubtitle));
    }

    /// <summary>Descrizione breve mostrata sotto il titolo nella sidebar.</summary>
    public string Subtitle => Locale.T(Id + ".sub");

    /// <summary>La voce selezionata della home non mostra sottotitolo (vedi design di riferimento).</summary>
    public bool HasSubtitle => Subtitle.Length > 0 && Subtitle != Id + ".sub";

    private static WpfBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new WpfSolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Risultato selezionabile nella Command Palette.</summary>
public sealed record PaletteItem(string Title, string Subtitle, Action Execute);

/// <summary>
/// Voce del selettore "LIVELLO MODALITÀ" (sidebar e titlebar). Il livello non è
/// un'etichetta: decide cosa Modalità Gaming e Optimizer possono proporre.
/// </summary>
public sealed class ModeLevelVm : ObservableBase
{
    private bool _isSelected;

    public ModeLevelVm(NexusOptimizer.Core.Configuration.AppModeLevel level,
                       string title, string subtitle, string iconKind, WpfBrush accent)
    {
        Level = level;
        Title = title;
        Subtitle = subtitle;
        IconKind = iconKind;
        Accent = accent;
    }

    public NexusOptimizer.Core.Configuration.AppModeLevel Level { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string IconKind { get; }
    public WpfBrush Accent { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
