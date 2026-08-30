using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
// Disambiguazione WPF vs WinForms (UseWindowsForms=true nel csproj).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace NexusOptimizer.App.Controls;

/// <summary>
/// Gauge circolare leggero, renderizzato direttamente da WPF. Non esegue polling:
/// anima soltanto il passaggio tra due campioni e resta a CPU zero quando è fermo.
/// </summary>
public sealed class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double?), typeof(RadialGauge),
            new PropertyMetadata(null, OnValueChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RadialGauge),
            new PropertyMetadata(100d, OnVisualPropertyChanged));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(RadialGauge),
            new PropertyMetadata(Color.FromRgb(0x4F, 0x8C, 0xFF), OnVisualPropertyChanged));

    public static readonly DependencyProperty CenterTextProperty =
        DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(RadialGauge),
            new PropertyMetadata("—", OnVisualPropertyChanged));

    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(RadialGauge),
            new PropertyMetadata("UTILIZZO", OnVisualPropertyChanged));

    public static readonly DependencyProperty CenterForegroundProperty =
        DependencyProperty.Register(nameof(CenterForeground), typeof(Brush), typeof(RadialGauge),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty RingThicknessProperty =
        DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(RadialGauge),
            new PropertyMetadata(6d, OnVisualPropertyChanged));

    public static readonly DependencyProperty AnimationsEnabledProperty =
        DependencyProperty.Register(nameof(AnimationsEnabled), typeof(bool), typeof(RadialGauge),
            new PropertyMetadata(true));

    private static readonly DependencyProperty AnimatedValueProperty =
        DependencyProperty.Register(nameof(AnimatedValue), typeof(double), typeof(RadialGauge),
            new PropertyMetadata(0d, OnVisualPropertyChanged));

    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
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

    public Brush? CenterForeground
    {
        get => (Brush?)GetValue(CenterForegroundProperty);
        set => SetValue(CenterForegroundProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public bool AnimationsEnabled
    {
        get => (bool)GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    private double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    public RadialGauge()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RadialGauge)d;
        var target = e.NewValue is double value && double.IsFinite(value)
            ? Math.Clamp(value, 0, Math.Max(gauge.Maximum, 0))
            : 0d;

        var current = gauge.AnimatedValue;
        gauge.BeginAnimation(AnimatedValueProperty, null);
        gauge.AnimatedValue = target;

        if (!gauge.AnimationsEnabled || !gauge.IsLoaded || Math.Abs(current - target) < 0.05)
        {
            gauge.InvalidateVisual();
            return;
        }

        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        gauge.BeginAnimation(AnimatedValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RadialGauge)d).InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var thickness = Math.Clamp(RingThickness, 2, Math.Max(2, size / 5));
        var radius = Math.Max(1, (size - thickness) / 2 - 1);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var trackBrush = TryFindResource("SeparatorBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0x2C, 0x36, 0x40));
        var trackPen = new Pen(trackBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        var maximum = Math.Max(Maximum, 0.0001);
        var ratio = Math.Clamp(AnimatedValue / maximum, 0, 1);
        if (ratio > 0)
        {
            var accentBrush = new SolidColorBrush(Accent);
            var accentPen = new Pen(accentBrush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            if (ratio >= 0.9999)
                dc.DrawEllipse(null, accentPen, center, radius, radius);
            else
                dc.DrawGeometry(null, accentPen, BuildArc(center, radius, -90, ratio * 360));
        }

        var gaugeSize = Math.Min(ActualWidth, ActualHeight);
        var valueFont = Math.Clamp(gaugeSize * 0.24, 9.5, 20);
        var subFont = Math.Clamp(gaugeSize * 0.105, 6.5, 8.5);
        // Senza sottotitolo il valore va centrato davvero, altrimenti resta alto
        // e l'anello sembra sbilanciato (accade nelle card compatte dei dischi).
        var hasSub = !string.IsNullOrWhiteSpace(SubText);
        DrawCenteredText(dc, CenterText, center.Y - valueFont * (hasSub ? 0.72 : 0.63), valueFont,
            FontWeights.SemiBold,
            CenterForeground ?? TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White);
        DrawCenteredText(dc, SubText, center.Y + valueFont * 0.45, subFont, FontWeights.Normal,
            TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
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
