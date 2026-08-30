using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace NexusOptimizer.App.Controls;

/// <summary>
/// Illustrazione vettoriale del PC per la dashboard: case con pannello in vetro,
/// interni visibili (dissipatore, banchi RAM, scheda video, alimentatore) e
/// illuminazione a tema. È disegnata da WPF, resta nitida a qualsiasi DPI, non
/// carica asset bitmap e non ha costo a runtime dopo il primo render.
///
/// Il colore di accento segue il tema dell'applicazione: cambiando accento cambia
/// anche la luce del case, senza rigenerare nulla.
/// </summary>
public sealed class PcTowerIcon : FrameworkElement
{
    /// <summary>Griglia di progetto dell'illustrazione.</summary>
    private const double DesignWidth = 100d;
    private const double DesignHeight = 112d;

    // Corpo del case: faccia in vetro (fronte), coperchio e fianco frontale.
    private static readonly Point GlassTopLeft = new(21, 26);
    private static readonly Point GlassTopRight = new(63, 26);
    private static readonly Point GlassBottomRight = new(63, 102);
    private static readonly Point GlassBottomLeft = new(21, 102);
    private static readonly Point TopBackLeft = new(37, 15);
    private static readonly Point TopBackRight = new(79, 15);
    private static readonly Point SideBottomRight = new(79, 91);

    public PcTowerIcon()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color
                     ?? Color.FromRgb(0x4F, 0x8C, 0xFF);
        var second = Color.FromRgb(0x27, 0xD5, 0x9B);   // luce secondaria delle ventole

        dc.PushTransform(new ScaleTransform(ActualWidth / DesignWidth, ActualHeight / DesignHeight));

        DrawGround(dc, accent);
        DrawChassis(dc, accent);
        DrawInterior(dc, accent, second);
        DrawGlass(dc);
        DrawFrontPanel(dc, accent);

        dc.Pop();
    }

    /// <summary>Ombra di appoggio e alone di luce: danno profondità senza sfocature costose.</summary>
    private static void DrawGround(DrawingContext dc, Color accent)
    {
        for (var i = 3; i >= 1; i--)
        {
            var alpha = (byte)(10 * i);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)), null,
                new Point(50, 98), 26 + i * 4, 10 + i * 2.5);
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Point(50, 106), 29, 4.5);
    }

    private static void DrawChassis(DrawingContext dc, Color accent)
    {
        var edge = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        var frame = new SolidColorBrush(Color.FromRgb(0x2B, 0x3A, 0x4B));

        // Coperchio superiore.
        var top = Polygon(GlassTopLeft, TopBackLeft, TopBackRight, GlassTopRight);
        dc.DrawGeometry(Gradient(0x30, 0x40, 0x52, 0x18, 0x22, 0x2E, 20), new Pen(frame, 1), top);

        // Fianco destro (frontale del case).
        var side = Polygon(GlassTopRight, TopBackRight, SideBottomRight, GlassBottomRight);
        dc.DrawGeometry(Gradient(0x1B, 0x27, 0x35, 0x0A, 0x11, 0x1A, 90), new Pen(frame, 1), side);

        // Corpo in vetro (faccia principale).
        var glass = Polygon(GlassTopLeft, GlassTopRight, GlassBottomRight, GlassBottomLeft);
        dc.DrawGeometry(Gradient(0x12, 0x1C, 0x27, 0x07, 0x0D, 0x15, 65), new Pen(frame, 1.1), glass);

        // Filo di luce sugli spigoli superiori: è ciò che rende "acceso" il case.
        var lightPen = new Pen(edge, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(lightPen, GlassTopLeft, GlassTopRight);
        dc.DrawLine(lightPen, GlassTopRight, TopBackRight);
    }

    /// <summary>Componenti interni visti attraverso il vetro.</summary>
    private static void DrawInterior(DrawingContext dc, Color accent, Color second)
    {
        // Piastra motherboard.
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x0D, 0x18, 0x22)),
            new Pen(new SolidColorBrush(Color.FromArgb(90, 0x3E, 0x55, 0x6B)), 0.8),
            new Rect(25, 30, 34, 68), 2.5, 2.5);

        // Dissipatore CPU: l'elemento più grande, in alto come nel montaggio reale.
        DrawFan(dc, new Point(36, 44), 10, accent);

        // Banchi di memoria con la sommità accesa.
        for (var i = 0; i < 3; i++)
        {
            var x = 49.5 + i * 3.4;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x2C, 0x3A)), null, new Rect(x, 34, 2.2, 19));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)), null,
                new Rect(x, 34, 2.2, 2.2));
        }

        // Scheda video con doppia ventola e barra luminosa.
        dc.DrawRoundedRectangle(Gradient(0x22, 0x2F, 0x3D, 0x14, 0x1D, 0x28, 90),
            new Pen(new SolidColorBrush(Color.FromArgb(130, 0x4B, 0x63, 0x7A)), 0.8),
            new Rect(26, 61, 32, 14), 2.5, 2.5);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(165, second.R, second.G, second.B)), null,
            new Rect(28, 73.4, 28, 1.3));
        DrawFan(dc, new Point(34.5, 67.5), 5, second);
        DrawFan(dc, new Point(48.5, 67.5), 5, second);

        // Copertura alimentatore con spia di stato.
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x23, 0x2F)),
            new Pen(new SolidColorBrush(Color.FromArgb(80, 0x46, 0x5D, 0x74)), 0.8),
            new Rect(25, 85, 34, 12), 2.5, 2.5);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)), null,
            new Point(53, 91), 1.7, 1.7);
    }

    /// <summary>Riflesso sul vetro: una diagonale chiara a bassissima opacità.</summary>
    private static void DrawGlass(DrawingContext dc)
    {
        var reflection = Polygon(new Point(21, 58), new Point(41, 26), new Point(49, 26), new Point(21, 71));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(16, 0xFF, 0xFF, 0xFF)), null, reflection);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 0x7E, 0x93, 0xA8)), 0.9),
            Polygon(GlassTopLeft, GlassTopRight, GlassBottomRight, GlassBottomLeft));
    }

    /// <summary>Griglie di aerazione, LED di stato e porte sul frontale.</summary>
    private static void DrawFrontPanel(DrawingContext dc, Color accent)
    {
        var ventPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x53, 0x6B, 0x81)), 0.9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        // Le feritoie seguono l'inclinazione del fianco: da (58,y) verso (84,y-8).
        for (var i = 0; i < 6; i++)
        {
            var y = 36 + i * 8.4;
            dc.DrawLine(ventPen, new Point(65.5, y), new Point(76.5, y - 6.2));
        }

        // Barra luminosa e pulsante di accensione.
        var bar = Polygon(new Point(64.5, 25.6), new Point(77.5, 18.4), new Point(77.5, 20), new Point(64.5, 27.2));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(215, accent.R, accent.G, accent.B)), null, bar);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)), null,
            new Point(71, 84), 4.4, 4.4);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(240, accent.R, accent.G, accent.B)), null,
            new Point(71, 84), 2, 2);
    }

    /// <summary>Ventola: anello luminoso, pale e mozzo centrale.</summary>
    private static void DrawFan(DrawingContext dc, Point center, double radius, Color color)
    {
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)), 1.3),
            center, radius, radius);

        var blade = new Pen(new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)), 0.9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        for (var i = 0; i < 4; i++)
        {
            var angle = Math.PI / 4 + i * Math.PI / 2;
            var inner = new Point(center.X + Math.Cos(angle) * radius * 0.28,
                                  center.Y + Math.Sin(angle) * radius * 0.28);
            var outer = new Point(center.X + Math.Cos(angle) * (radius - 1.2),
                                  center.Y + Math.Sin(angle) * (radius - 1.2));
            dc.DrawLine(blade, inner, outer);
        }

        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B)), null,
            center, radius * 0.22, radius * 0.22);
    }

    private static LinearGradientBrush Gradient(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, double angle)
    {
        var brush = new LinearGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2), angle);
        brush.Freeze();
        return brush;
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++) context.LineTo(points[i], true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
