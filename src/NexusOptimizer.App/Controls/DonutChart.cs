using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
// Disambiguazione WPF vs WinForms (UseWindowsForms=true nel csproj).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace NexusOptimizer.App.Controls;

/// <summary>Segmento proporzionale del grafico ad anello.</summary>
public interface IDonutSegment
{
    double Weight { get; }
    Brush SegmentBrush { get; }
}

/// <summary>
/// Anello proporzionale disegnato direttamente da WPF: nessuna dipendenza esterna,
/// nessun polling. Le proporzioni riflettono i valori reali passati in Segments;
/// se il totale è zero viene mostrata solo la traccia vuota.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments), typeof(IEnumerable), typeof(DonutChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty RingThicknessProperty =
        DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(DonutChart),
            new PropertyMetadata(11d, OnVisualPropertyChanged));

    public static readonly DependencyProperty CenterTextProperty =
        DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(DonutChart),
            new PropertyMetadata("—", OnVisualPropertyChanged));

    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(DonutChart),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public DonutChart()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DonutChart)d).InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var thickness = Math.Clamp(RingThickness, 3, Math.Max(3, size / 4));
        var radius = Math.Max(1, (size - thickness) / 2 - 1);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var trackBrush = TryFindResource("SeparatorBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0x2C, 0x36, 0x40));
        dc.DrawEllipse(null, new Pen(trackBrush, thickness), center, radius, radius);

        var segments = ReadSegments();
        var total = segments.Sum(s => s.Weight);
        if (total > 0)
        {
            // 2 gradi di stacco tra i settori: la lettura resta chiara anche con
            // categorie molto piccole, senza falsare le proporzioni.
            const double gap = 2d;
            var angle = -90d;
            foreach (var segment in segments)
            {
                var sweep = segment.Weight / total * 360d;
                if (sweep <= 0.05) { angle += sweep; continue; }
                var drawn = Math.Max(sweep - gap, 0.8);
                var pen = new Pen(segment.SegmentBrush, thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawGeometry(null, pen, BuildArc(center, radius, angle + gap / 2, drawn));
                angle += sweep;
            }
        }

        var valueFont = Math.Clamp(size * 0.2, 11, 24);
        DrawCenteredText(dc, CenterText, center.Y - valueFont * 0.78, valueFont, FontWeights.SemiBold,
            TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White);
        DrawCenteredText(dc, SubText, center.Y + valueFont * 0.34, Math.Clamp(size * 0.085, 7, 10),
            FontWeights.Normal, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
    }

    private List<IDonutSegment> ReadSegments()
    {
        var list = new List<IDonutSegment>();
        if (Segments is null) return list;
        foreach (var item in Segments)
        {
            if (item is IDonutSegment segment && segment.Weight > 0) list.Add(segment);
        }
        return list;
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        static Point Polar(Point c, double r, double degrees)
        {
            var radians = degrees * Math.PI / 180d;
            return new Point(c.X + Math.Cos(radians) * r, c.Y + Math.Sin(radians) * r);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(center, radius, startAngle), false, false);
            context.ArcTo(Polar(center, radius, startAngle + sweepAngle),
                new Size(radius, radius), 0, sweepAngle > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawCenteredText(DrawingContext dc, string? text, double top, double size,
                                  FontWeight weight, Brush brush)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point((ActualWidth - formatted.Width) / 2, top));
    }
}
