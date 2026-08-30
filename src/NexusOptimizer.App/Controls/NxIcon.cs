using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
// Alias per disambiguare i tipi WPF dai global using WinForms.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace NexusOptimizer.App.Controls;

/// <summary>
/// Icona vettoriale leggera: disegna una geometria del dizionario AppIcons tinta
/// col brush ereditato dal testo. Nessuna dipendenza da font o asset esterni.
///
/// Le icone del catalogo sono definite su una griglia 24x24 e possono essere
/// a tratto o a superficie piena: il tratto viene scalato con l'icona, così lo
/// spessore resta identico fra una voce di menu da 19 px e una card da 32 px.
/// </summary>
public sealed class NxIcon : FrameworkElement
{
    /// <summary>Lato della griglia di progetto delle icone.</summary>
    private const double DesignSize = 24d;

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(NxIcon),
            new PropertyMetadata("home", static (d, _) => ((NxIcon)d).InvalidateVisual()));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(NxIcon),
            new PropertyMetadata(null, static (d, _) => ((NxIcon)d).InvalidateVisual()));

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Moltiplicatore dello spessore del tratto (1 = spessore di progetto).</summary>
    public static readonly DependencyProperty StrokeScaleProperty =
        DependencyProperty.Register(nameof(StrokeScale), typeof(double), typeof(NxIcon),
            new PropertyMetadata(1d, static (d, _) => ((NxIcon)d).InvalidateVisual()));

    public double StrokeScale
    {
        get => (double)GetValue(StrokeScaleProperty);
        set => SetValue(StrokeScaleProperty, value);
    }

    public NxIcon()
    {
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(ActualWidth, 1);
        var h = Math.Max(ActualHeight, 1);

        var brush = Foreground ?? TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        var icon = Services.AppIcons.Get(Kind);
        if (icon is null)
        {
            dc.DrawRectangle(null, new Pen(brush, 1), new Rect(1, 1, w - 2, h - 2));
            return;
        }

        // Scala sulla griglia di progetto (non sui bounds): icone diverse restano
        // otticamente della stessa dimensione e allineate fra loro.
        var scale = Math.Min(w, h) / DesignSize;
        dc.PushTransform(new TranslateTransform((w - DesignSize * scale) / 2, (h - DesignSize * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));

        if (icon.Stroked)
        {
            var pen = new Pen(brush, Math.Max(icon.Thickness * StrokeScale, 0.1))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, icon.Geometry);
        }
        else
        {
            dc.DrawGeometry(brush, null, icon.Geometry);
        }

        dc.Pop();
        dc.Pop();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        => null; // l'icona non intercetta input: clic transitano al contenitore
}
