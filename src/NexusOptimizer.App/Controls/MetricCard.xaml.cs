using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
// Disambiguazione WPF vs WinForms (UseWindowsForms=true nel csproj).
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty CardTitleProperty =
        DependencyProperty.Register(nameof(CardTitle), typeof(string), typeof(MetricCard),
            new PropertyMetadata("TITOLO"));

    public static readonly DependencyProperty CardValueProperty =
        DependencyProperty.Register(nameof(CardValue), typeof(string), typeof(MetricCard),
            new PropertyMetadata(Formatter.Dash));

    public static readonly DependencyProperty CardSubProperty =
        DependencyProperty.Register(nameof(CardSub), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CardCaptionProperty =
        DependencyProperty.Register(nameof(CardCaption), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CardStat1LabelProperty =
        DependencyProperty.Register(nameof(CardStat1Label), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CardStat1ValueProperty =
        DependencyProperty.Register(nameof(CardStat1Value), typeof(string), typeof(MetricCard),
            new PropertyMetadata(Formatter.Dash));

    public static readonly DependencyProperty CardStat2LabelProperty =
        DependencyProperty.Register(nameof(CardStat2Label), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CardStat2ValueProperty =
        DependencyProperty.Register(nameof(CardStat2Value), typeof(string), typeof(MetricCard),
            new PropertyMetadata(Formatter.Dash));

    public static readonly DependencyProperty CardStat3LabelProperty =
        DependencyProperty.Register(nameof(CardStat3Label), typeof(string), typeof(MetricCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CardStat3ValueProperty =
        DependencyProperty.Register(nameof(CardStat3Value), typeof(string), typeof(MetricCard),
            new PropertyMetadata(Formatter.Dash));

    public static readonly DependencyProperty CardStat3BrushProperty =
        DependencyProperty.Register(nameof(CardStat3Brush), typeof(Brush), typeof(MetricCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CardSeriesProperty =
        DependencyProperty.Register(nameof(CardSeries), typeof(IReadOnlyList<double>),
            typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CardFixedMaxProperty =
        DependencyProperty.Register(nameof(CardFixedMax), typeof(double?), typeof(MetricCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CardUnitProperty =
        DependencyProperty.Register(nameof(CardUnit), typeof(string), typeof(MetricCard),
            new PropertyMetadata("percent"));

    public static readonly DependencyProperty CardWindowSecondsProperty =
        DependencyProperty.Register(nameof(CardWindowSeconds), typeof(int), typeof(MetricCard),
            new PropertyMetadata(30));

    public static readonly DependencyProperty CardShowAxesProperty =
        DependencyProperty.Register(nameof(CardShowAxes), typeof(bool), typeof(MetricCard),
            new PropertyMetadata(false));

    /// <summary>
    /// Percentuale opzionale della seconda riga (usata dalla VRAM nella card GPU):
    /// senza valore la barra non compare, cosi' le card che non la usano restano
    /// identiche a prima.
    /// </summary>
    public static readonly DependencyProperty CardStat2ProgressProperty =
        DependencyProperty.Register(nameof(CardStat2Progress), typeof(double?), typeof(MetricCard),
            new PropertyMetadata(null, OnStat2ProgressChanged));

    public static readonly DependencyProperty CardStat2HasProgressProperty =
        DependencyProperty.Register(nameof(CardStat2HasProgress), typeof(bool), typeof(MetricCard),
            new PropertyMetadata(false));

    private static void OnStat2ProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetricCard)d).CardStat2HasProgress = e.NewValue is double;

    public static readonly DependencyProperty CardProgressProperty =
        DependencyProperty.Register(nameof(CardProgress), typeof(double?), typeof(MetricCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CardAccentProperty =
        DependencyProperty.Register(nameof(CardAccent), typeof(Color), typeof(MetricCard),
            new PropertyMetadata(Color.FromRgb(0x4F, 0x8C, 0xFF)));

    public static readonly DependencyProperty CardShowGaugeProperty =
        DependencyProperty.Register(nameof(CardShowGauge), typeof(bool), typeof(MetricCard),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CardAnimationsEnabledProperty =
        DependencyProperty.Register(nameof(CardAnimationsEnabled), typeof(bool), typeof(MetricCard),
            new PropertyMetadata(true));

    public MetricCard()
    {
        InitializeComponent();
    }

    public string CardTitle { get => (string)GetValue(CardTitleProperty); set => SetValue(CardTitleProperty, value); }
    public string CardValue { get => (string)GetValue(CardValueProperty); set => SetValue(CardValueProperty, value); }
    public string CardSub { get => (string)GetValue(CardSubProperty); set => SetValue(CardSubProperty, value); }
    /// <summary>Didascalia sotto l'indicatore (es. "Utilizzo", "Attivi").</summary>
    public string CardCaption { get => (string)GetValue(CardCaptionProperty); set => SetValue(CardCaptionProperty, value); }
    public string CardStat1Label { get => (string)GetValue(CardStat1LabelProperty); set => SetValue(CardStat1LabelProperty, value); }
    public string CardStat1Value { get => (string)GetValue(CardStat1ValueProperty); set => SetValue(CardStat1ValueProperty, value); }
    public string CardStat2Label { get => (string)GetValue(CardStat2LabelProperty); set => SetValue(CardStat2LabelProperty, value); }
    public string CardStat2Value { get => (string)GetValue(CardStat2ValueProperty); set => SetValue(CardStat2ValueProperty, value); }
    public string CardStat3Label { get => (string)GetValue(CardStat3LabelProperty); set => SetValue(CardStat3LabelProperty, value); }
    public string CardStat3Value { get => (string)GetValue(CardStat3ValueProperty); set => SetValue(CardStat3ValueProperty, value); }
    /// <summary>Colore della terza riga: usato per gli stati (es. rete connessa).</summary>
    public Brush? CardStat3Brush { get => (Brush?)GetValue(CardStat3BrushProperty); set => SetValue(CardStat3BrushProperty, value); }
    public IReadOnlyList<double>? CardSeries { get => (IReadOnlyList<double>?)GetValue(CardSeriesProperty); set => SetValue(CardSeriesProperty, value); }
    /// <summary>Scala massima del grafico; null = auto-adattiva.</summary>
    public double? CardFixedMax { get => (double?)GetValue(CardFixedMaxProperty); set => SetValue(CardFixedMaxProperty, value); }
    /// <summary>Unità serie: percent, bytesPerSecond o numeric.</summary>
    public string CardUnit { get => (string)GetValue(CardUnitProperty); set => SetValue(CardUnitProperty, value); }
    public int CardWindowSeconds { get => (int)GetValue(CardWindowSecondsProperty); set => SetValue(CardWindowSecondsProperty, value); }
    /// <summary>Le card Dashboard mostrano una sparkline pulita; gli assi completi vivono in Performance.</summary>
    public bool CardShowAxes { get => (bool)GetValue(CardShowAxesProperty); set => SetValue(CardShowAxesProperty, value); }
    public double? CardStat2Progress { get => (double?)GetValue(CardStat2ProgressProperty); set => SetValue(CardStat2ProgressProperty, value); }
    public bool CardStat2HasProgress { get => (bool)GetValue(CardStat2HasProgressProperty); private set => SetValue(CardStat2HasProgressProperty, value); }
    public double? CardProgress { get => (double?)GetValue(CardProgressProperty); set => SetValue(CardProgressProperty, value); }
    public Color CardAccent { get => (Color)GetValue(CardAccentProperty); set => SetValue(CardAccentProperty, value); }
    public bool CardShowGauge { get => (bool)GetValue(CardShowGaugeProperty); set => SetValue(CardShowGaugeProperty, value); }
    public bool CardAnimationsEnabled { get => (bool)GetValue(CardAnimationsEnabledProperty); set => SetValue(CardAnimationsEnabledProperty, value); }
}
