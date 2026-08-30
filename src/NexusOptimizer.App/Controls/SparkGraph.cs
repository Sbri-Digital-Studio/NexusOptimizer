using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
// Alias per disambiguare i tipi WPF dai global using WinForms.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace NexusOptimizer.App.Controls;

/// <summary>
/// Grafico leggero disegnato su DrawingContext: griglia, assi numerici,
/// tooltip e puntatore interattivo senza dipendenze grafiche esterne.
/// </summary>
public sealed class SparkGraph : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<double>),
            typeof(SparkGraph), new PropertyMetadata(null, OnSeriesChanged));

    public IReadOnlyList<double>? Series
    {
        get => (IReadOnlyList<double>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Scala massima fissa (es. CPU/RAM % = 100); null = auto-adattiva.</summary>
    public double? FixedMax
    {
        get => (double?)GetValue(FixedMaxProperty);
        set => SetValue(FixedMaxProperty, value);
    }

    /// <summary>percent, bytesPerSecond o numeric.</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>Durata della finestra visualizzata per le tacche dell'asse X.</summary>
    public int WindowSeconds
    {
        get => (int)GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    /// <summary>Mostra le tacche numeriche dell'asse Y (disattivato nelle card compatte).</summary>
    public bool ShowYAxis
    {
        get => (bool)GetValue(ShowYAxisProperty);
        set => SetValue(ShowYAxisProperty, value);
    }

    /// <summary>Mostra le etichette temporali dell'asse X.</summary>
    public bool ShowXAxis
    {
        get => (bool)GetValue(ShowXAxisProperty);
        set => SetValue(ShowXAxisProperty, value);
    }

    /// <summary>Raccorda visivamente i campioni senza alterare i valori o i tooltip.</summary>
    public bool Smooth
    {
        get => (bool)GetValue(SmoothProperty);
        set => SetValue(SmoothProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public static readonly DependencyProperty FixedMaxProperty =
        DependencyProperty.Register(nameof(FixedMax), typeof(double?), typeof(SparkGraph),
            new PropertyMetadata(null, static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SparkGraph),
            new PropertyMetadata("percent", static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty WindowSecondsProperty =
        DependencyProperty.Register(nameof(WindowSeconds), typeof(int), typeof(SparkGraph),
            new PropertyMetadata(30, static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty ShowYAxisProperty =
        DependencyProperty.Register(nameof(ShowYAxis), typeof(bool), typeof(SparkGraph),
            new PropertyMetadata(true, static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty ShowXAxisProperty =
        DependencyProperty.Register(nameof(ShowXAxis), typeof(bool), typeof(SparkGraph),
            new PropertyMetadata(true, static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty SmoothProperty =
        DependencyProperty.Register(nameof(Smooth), typeof(bool), typeof(SparkGraph),
            new PropertyMetadata(false, static (d, _) => ((SparkGraph)d).InvalidateVisual()));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(SparkGraph),
            new PropertyMetadata(Color.FromRgb(0x4F, 0x8C, 0xFF), OnAccentChanged));

    private Pen? _linePen;
    private Pen? _glowPen;
    private Brush? _fillBrush;
    private Rect _plotRect;
    private int _hoverIndex = -1;

    public SparkGraph()
    {
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Loaded += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) => InvalidateVisual();
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        System.Windows.Controls.ToolTipService.SetShowDuration(this, 30_000);
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (SparkGraph)d;
        graph._linePen = null;
        graph._glowPen = null;
        graph._fillBrush = null;
        graph.InvalidateVisual();
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (SparkGraph)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= graph.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged current) current.CollectionChanged += graph.OnCollectionChanged;
        graph._hoverIndex = -1;
        graph.ToolTip = null;
        graph.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_hoverIndex >= (Series?.Count ?? 0)) _hoverIndex = -1;
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        var data = Series;
        if (data is null || data.Count == 0 || !_plotRect.Contains(e.GetPosition(this))) return;
        var x = e.GetPosition(this).X;
        var ratio = (x - _plotRect.Left) / Math.Max(_plotRect.Width, 1);
        _hoverIndex = Math.Clamp((int)Math.Round(ratio * Math.Max(data.Count - 1, 0)), 0, data.Count - 1);
        ToolTip = BuildTooltip(data, _hoverIndex);
        InvalidateVisual();
    }

    private void OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        _hoverIndex = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(ActualWidth, 1);
        var h = Math.Max(ActualHeight, 1);
        var data = Series;
        // Le card compatte della Dashboard usano tutta la larghezza per la
        // sparkline; gli assi completi restano disponibili in Performance.
        var left = ShowYAxis
            ? Unit.Equals("bytesPerSecond", StringComparison.OrdinalIgnoreCase) ? 72 : 54
            : 3;
        var bottomInset = ShowXAxis ? 34 : 8;
        _plotRect = new Rect(left, 6, Math.Max(1, w - left - 8), Math.Max(1, h - bottomInset));

        var max = FixedMax ?? NiceCeiling(ComputeMax(data));
        max = Math.Max(max, 0.0001);
        DrawGrid(dc, max);

        if (data is not null && data.Count > 0)
        {
            DrawSeries(dc, data, max);
            if (_hoverIndex >= 0 && _hoverIndex < data.Count)
                DrawHover(dc, data[_hoverIndex], _hoverIndex, max);
        }

        if (ShowXAxis) DrawXAxis(dc, data?.Count ?? 0);
    }

    private void DrawGrid(DrawingContext dc, double max)
    {
        var gridBrush = TryFindResource("SeparatorBrush") as Brush
                        ?? new SolidColorBrush(Color.FromArgb(120, 0x35, 0x40, 0x4C));
        var textBrush = TryFindResource("TextSecondaryBrush") as Brush
                        ?? new SolidColorBrush(Color.FromArgb(220, 0x9A, 0xA6, 0xB2));
        var gridPen = new Pen(gridBrush, 1) { DashStyle = DashStyles.Dot };
        // In una card molto bassa mostriamo solo tre tacche ben distinte.
        var tickCount = _plotRect.Height < 82 ? 2 : 4;
        for (var i = 0; i <= tickCount; i++)
        {
            var fraction = i / (double)tickCount;
            var y = _plotRect.Bottom - fraction * _plotRect.Height;
            dc.DrawLine(gridPen, new Point(_plotRect.Left, y), new Point(_plotRect.Right, y));
            if (ShowYAxis)
            {
                var labelY = Math.Clamp(y - 6, 0, Math.Max(0, ActualHeight - 14));
                DrawTextRight(dc, FormatValue(max * fraction), _plotRect.Left - 8, labelY, textBrush);
            }
        }
    }

    private void DrawSeries(DrawingContext dc, IReadOnlyList<double> data, double max)
    {
        _linePen ??= BuildPen();
        _glowPen ??= BuildGlowPen();
        _fillBrush ??= new LinearGradientBrush(
            Color.FromArgb(82, Accent.R, Accent.G, Accent.B),
            Color.FromArgb(2, Accent.R, Accent.G, Accent.B),
            new Point(0.5, 0), new Point(0.5, 1));
        var stepX = _plotRect.Width / Math.Max(data.Count - 1, 1);
        var points = new Point[data.Count];
        for (var i = 0; i < data.Count; i++) points[i] = PointFor(i, data[i], max, stepX);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
            AppendSeries(ctx, points);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(_plotRect.Left, _plotRect.Bottom), true, true);
            ctx.LineTo(points[0], true, false);
            AppendSeries(ctx, points);
            ctx.LineTo(new Point(_plotRect.Right, _plotRect.Bottom), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(_fillBrush, null, area);
        dc.DrawGeometry(null, _glowPen, line);
        dc.DrawGeometry(null, _linePen, line);
    }

    private void AppendSeries(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        if (!Smooth || points.Count < 3)
        {
            for (var i = 1; i < points.Count; i++) context.LineTo(points[i], true, false);
            return;
        }

        // Catmull-Rom convertita in Bézier cubiche. I punti misurati restano
        // invariati; cambia soltanto il raccordo grafico tra un campione e l'altro.
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(points.Count - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6d,
                Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6d, _plotRect.Top, _plotRect.Bottom));
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6d,
                Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6d, _plotRect.Top, _plotRect.Bottom));
            context.BezierTo(c1, c2, p2, true, false);
        }
    }

    private void DrawHover(DrawingContext dc, double value, int index, double max)
    {
        var data = Series!;
        var stepX = _plotRect.Width / Math.Max(data.Count - 1, 1);
        var point = PointFor(index, value, max, stepX);
        var crossBrush = new SolidColorBrush(Color.FromArgb(150, Accent.R, Accent.G, Accent.B));
        var crossPen = new Pen(crossBrush, 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(crossPen, new Point(point.X, _plotRect.Top), new Point(point.X, _plotRect.Bottom));
        dc.DrawEllipse(new SolidColorBrush(Accent), null, point, 3.5, 3.5);
    }

    private void DrawXAxis(DrawingContext dc, int count)
    {
        var textBrush = TryFindResource("TextSecondaryBrush") as Brush
                        ?? new SolidColorBrush(Color.FromArgb(220, 0x9A, 0xA6, 0xB2));
        var leftLabel = WindowSeconds > 0 ? $"−{FormatDuration(WindowSeconds)}" : "inizio";
        DrawText(dc, leftLabel, new Point(_plotRect.Left, _plotRect.Bottom + 4), textBrush);
        var middle = WindowSeconds > 1 ? $"−{FormatDuration(WindowSeconds / 2)}" : "";
        DrawText(dc, middle, new Point(_plotRect.Left + _plotRect.Width / 2 - 12, _plotRect.Bottom + 4), textBrush);
        DrawText(dc, count > 0 ? "ora" : "in attesa…", new Point(_plotRect.Right - 25, _plotRect.Bottom + 4), textBrush);
    }

    private Point PointFor(int index, double value, double max, double stepX)
        => new(_plotRect.Left + index * stepX,
            _plotRect.Bottom - Clamp01(value / max) * _plotRect.Height);

    private string BuildTooltip(IReadOnlyList<double> data, int index)
    {
        var pointsFromEnd = data.Count - 1 - index;
        var secondsAgo = data.Count <= 1
            ? 0
            : (int)Math.Round(WindowSeconds * pointsFromEnd / (double)(data.Count - 1));
        var when = secondsAgo == 0 ? "ora" : $"−{FormatDuration(secondsAgo)}";
        return $"{FormatValue(data[index])} · {when}\nCampione {index + 1}/{data.Count}";
    }

    private string FormatValue(double value)
    {
        if (Unit.Equals("percent", StringComparison.OrdinalIgnoreCase))
            return $"{value.ToString("0.#", CultureInfo.CurrentCulture)}%";
        if (Unit.Equals("bytesPerSecond", StringComparison.OrdinalIgnoreCase))
            return NexusOptimizer.App.Services.Formatter.RatePerSec(value / 1024d);
        return value.ToString("#,##0.##", CultureInfo.CurrentCulture);
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds >= 3600) return $"{seconds / 3600d:0.#} h";
        if (seconds >= 60) return $"{seconds / 60d:0.#} min";
        return $"{seconds}s";
    }

    private void DrawText(DrawingContext dc, string text, Point origin, Brush brush)
    {
        if (string.IsNullOrEmpty(text)) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, brush, dpi);
        dc.DrawText(formatted, origin);
    }

    private void DrawTextRight(DrawingContext dc, string text, double right, double top, Brush brush)
    {
        if (string.IsNullOrEmpty(text)) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, brush, dpi);
        dc.DrawText(formatted, new Point(Math.Max(0, right - formatted.Width), top));
    }

    private Pen BuildPen()
    {
        var brush = new SolidColorBrush(Accent);
        return new Pen(brush, 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
    }

    private Pen BuildGlowPen()
        => new(new SolidColorBrush(Color.FromArgb(54, Accent.R, Accent.G, Accent.B)), 5.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

    private static double ComputeMax(IReadOnlyList<double>? data)
    {
        if (data is null || data.Count == 0) return 1;
        var max = 0.001;
        foreach (var value in data)
            if (!double.IsNaN(value) && !double.IsInfinity(value)) max = Math.Max(max, value);
        return max * 1.2;
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value)) return 1;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / exponent;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * exponent;
    }

    private static double Clamp01(double value) => value <= 0 ? 0 : value >= 1 ? 1 : value;
}
