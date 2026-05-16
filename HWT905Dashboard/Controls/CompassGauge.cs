using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace HWT905Dashboard.Controls;

public sealed class CompassGauge : FrameworkElement
{
    public double Heading { get; set; }
    public double MagX { get; set; }
    public double MagY { get; set; }
    public double MagZ { get; set; }

    public void SetValues(double heading, double mx, double my, double mz)
    {
        Heading = Normalize360(heading);
        MagX = mx;
        MagY = my;
        MagZ = mz;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 40 || h < 40) return;

        dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(5, 13, 20), Color.FromRgb(11, 22, 31), 90),
            null, new Rect(0, 0, w, h), 8, 8);

        double r = Math.Min(w, h) * 0.42;
        Point c = new(w / 2, h / 2 + 2);
        dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(19, 29, 39), Color.FromRgb(5, 8, 12)), new Pen(new SolidColorBrush(Color.FromRgb(56, 70, 83)), 2), c, r, r);

        for (int deg = 0; deg < 360; deg += 2)
        {
            bool major = deg % 30 == 0;
            bool mid = deg % 10 == 0;
            double len = major ? 18 : mid ? 12 : 7;
            double thickness = major ? 2 : 1;
            double rad = (deg - 90) * Math.PI / 180.0;
            Point p1 = new(c.X + Math.Cos(rad) * (r - len), c.Y + Math.Sin(rad) * (r - len));
            Point p2 = new(c.X + Math.Cos(rad) * (r - 3), c.Y + Math.Sin(rad) * (r - 3));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, 220, 230, 240)), thickness), p1, p2);
        }

        DrawCompassText(dc, "N", c, r * 0.72, 0, 26, Brushes.White, FontWeights.Bold);
        DrawCompassText(dc, "E", c, r * 0.72, 90, 26, Brushes.White, FontWeights.Bold);
        DrawCompassText(dc, "S", c, r * 0.72, 180, 26, Brushes.White, FontWeights.Bold);
        DrawCompassText(dc, "W", c, r * 0.72, 270, 26, Brushes.White, FontWeights.Bold);
        DrawCompassText(dc, "NE", c, r * 0.74, 45, 13, Brushes.White, FontWeights.Normal);
        DrawCompassText(dc, "SE", c, r * 0.74, 135, 13, Brushes.White, FontWeights.Normal);
        DrawCompassText(dc, "SW", c, r * 0.74, 225, 13, Brushes.White, FontWeights.Normal);
        DrawCompassText(dc, "NW", c, r * 0.74, 315, 13, Brushes.White, FontWeights.Normal);

        // Needle: red points to heading, white tail opposite.
        DrawNeedle(dc, c, r * 0.70, Heading, new SolidColorBrush(Color.FromRgb(255, 56, 50)));
        DrawNeedle(dc, c, r * 0.52, Heading + 180, new SolidColorBrush(Color.FromRgb(230, 235, 240)));
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(210, 215, 220)), new Pen(new SolidColorBrush(Color.FromRgb(60, 70, 80)), 2), c, 9, 9);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(70, 80, 88)), null, c, 3, 3);
    }

    private static void DrawNeedle(DrawingContext dc, Point c, double len, double deg, Brush brush)
    {
        double rad = (Normalize360(deg) - 90) * Math.PI / 180.0;
        double side = 11;
        Point tip = new(c.X + Math.Cos(rad) * len, c.Y + Math.Sin(rad) * len);
        Point left = new(c.X + Math.Cos(rad + Math.PI / 2) * side, c.Y + Math.Sin(rad + Math.PI / 2) * side);
        Point right = new(c.X + Math.Cos(rad - Math.PI / 2) * side, c.Y + Math.Sin(rad - Math.PI / 2) * side);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(left, true, false);
            ctx.LineTo(right, true, false);
        }
        dc.DrawGeometry(brush, null, g);
    }

    private static void DrawCompassText(DrawingContext dc, string text, Point c, double radius, double deg, double size, Brush brush, FontWeight weight)
    {
        double rad = (deg - 90) * Math.PI / 180.0;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
        dc.DrawText(ft, new Point(c.X + Math.Cos(rad) * radius - ft.Width / 2, c.Y + Math.Sin(rad) * radius - ft.Height / 2));
    }

    private static double Normalize360(double d)
    {
        d %= 360.0;
        if (d < 0) d += 360.0;
        return d;
    }
}
