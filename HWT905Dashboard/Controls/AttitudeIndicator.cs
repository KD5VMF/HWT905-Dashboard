using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace HWT905Dashboard.Controls;

public sealed class AttitudeIndicator : FrameworkElement
{
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Heading { get; set; }

    public void SetValues(double roll, double pitch, double heading)
    {
        Roll = roll;
        Pitch = pitch;
        Heading = heading;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 50 || h < 50) return;

        var bg = new LinearGradientBrush(Color.FromRgb(5, 13, 20), Color.FromRgb(13, 24, 34), 90);
        dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(Color.FromRgb(21, 42, 58)), 1), new Rect(0, 0, w, h), 8, 8);

        double r = Math.Min(w * 0.305, h * 0.335);
        Point c = new(w * 0.48, h * 0.385);
        var clip = new EllipseGeometry(c, r, r);

        dc.PushClip(clip);
        dc.PushTransform(new RotateTransform(-Roll, c.X, c.Y));
        double pitchShift = Math.Max(-r * 0.9, Math.Min(r * 0.9, Pitch / 45.0 * r));
        double horizonY = c.Y + pitchShift;
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0, 102, 255), Color.FromRgb(71, 168, 255), 90), null, new Rect(c.X - r * 2, c.Y - r * 2, r * 4, horizonY - (c.Y - r * 2)));
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(96, 48, 12), Color.FromRgb(55, 27, 9), 90), null, new Rect(c.X - r * 2, horizonY, r * 4, r * 4));
        dc.DrawLine(new Pen(Brushes.White, 2), new Point(c.X - r * 1.4, horizonY), new Point(c.X + r * 1.4, horizonY));

        var thin = new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), 1.2);
        var thick = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2.0);
        for (int deg = -30; deg <= 30; deg += 5)
        {
            if (deg == 0) continue;
            double y = horizonY - deg / 45.0 * r;
            double len = deg % 10 == 0 ? r * 0.44 : r * 0.24;
            dc.DrawLine(deg % 10 == 0 ? thick : thin, new Point(c.X - len, y), new Point(c.X + len, y));
            if (deg % 10 == 0)
            {
                var pitchLabelBrush = new SolidColorBrush(Color.FromArgb(220, 245, 250, 255));
                DrawText(dc, Math.Abs(deg).ToString(CultureInfo.InvariantCulture), 10, pitchLabelBrush, new Point(c.X - len - 21, y - 7), TextAlignment.Center);
                DrawText(dc, Math.Abs(deg).ToString(CultureInfo.InvariantCulture), 10, pitchLabelBrush, new Point(c.X + len + 13, y - 7), TextAlignment.Center);
            }
        }
        dc.Pop();
        dc.Pop();

        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(10, 12, 15)), 16), c, r + 8, r + 8);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(45, 62, 80)), 2), c, r, r);

        // Roll arc ticks and red pointer.
        for (int a = -60; a <= 60; a += 10)
        {
            double rad = (a - 90) * Math.PI / 180.0;
            double r1 = r + 6;
            double r2 = r + (a % 30 == 0 ? 22 : 14);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1),
                new Point(c.X + Math.Cos(rad) * r1, c.Y + Math.Sin(rad) * r1),
                new Point(c.X + Math.Cos(rad) * r2, c.Y + Math.Sin(rad) * r2));
        }
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r - 10), true, true);
            ctx.LineTo(new Point(c.X - 12, c.Y - r - 32), true, false);
            ctx.LineTo(new Point(c.X + 12, c.Y - r - 32), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 64, 48)), null, tri);

        // Fixed aircraft symbol.
        var yellow = new Pen(new SolidColorBrush(Color.FromRgb(255, 230, 0)), 4);
        dc.DrawLine(yellow, new Point(c.X - r * 0.60, c.Y), new Point(c.X - r * 0.16, c.Y));
        dc.DrawLine(yellow, new Point(c.X + r * 0.16, c.Y), new Point(c.X + r * 0.60, c.Y));
        dc.DrawLine(yellow, new Point(c.X, c.Y), new Point(c.X - r * 0.16, c.Y + r * 0.18));
        dc.DrawLine(yellow, new Point(c.X, c.Y), new Point(c.X + r * 0.16, c.Y + r * 0.18));
        dc.DrawLine(new Pen(Brushes.Yellow, 2), new Point(c.X - r * 0.16, c.Y + r * 0.18), new Point(c.X + r * 0.16, c.Y + r * 0.18));

        DrawSideScale(dc, new Rect(w * 0.08, h * 0.18, 28, h * 0.50), Roll, "R");
        DrawSideScale(dc, new Rect(w * 0.86, h * 0.18, 28, h * 0.50), Pitch, "P");

        DrawValueBox(dc, new Rect(w * 0.10, h * 0.80, 96, 40), Roll.ToString("0.0°"), Brushes.LawnGreen);
        DrawValueBox(dc, new Rect(w * 0.77, h * 0.80, 96, 40), Pitch.ToString("0.0°"), Brushes.LawnGreen);
        DrawValueBox(dc, new Rect(w * 0.355, h * 0.765, 178, 62), "HEADING\n" + Heading.ToString("0.0°"), Brushes.LawnGreen);
    }

    private static void DrawSideScale(DrawingContext dc, Rect rect, double value, string label)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(45, 62, 80)), 1);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(5, 12, 18)), pen, rect, 4, 4);
        for (int i = 0; i <= 8; i++)
        {
            double y = rect.Top + i * rect.Height / 8;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 91, 112)), 1), new Point(rect.Left + 7, y), new Point(rect.Left + 17, y));
        }
        double yy = rect.Top + rect.Height / 2 - Math.Max(-90, Math.Min(90, value)) / 180.0 * rect.Height;
        dc.DrawRectangle(Brushes.LawnGreen, null, new Rect(rect.Left + 3, yy - 5, rect.Width - 6, 10));
    }

    private static void DrawValueBox(DrawingContext dc, Rect rect, string text, Brush brush)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 5, 12, 18)), new Pen(new SolidColorBrush(Color.FromRgb(43, 60, 75)), 1), rect, 5, 5);
        var lines = text.Split('\n');
        if (lines.Length == 2)
        {
            DrawText(dc, lines[0], 13, new SolidColorBrush(Color.FromRgb(160, 178, 192)), new Point(rect.Left, rect.Top + 7), TextAlignment.Center, rect.Width);
            DrawText(dc, lines[1], 28, brush, new Point(rect.Left, rect.Top + 24), TextAlignment.Center, rect.Width, FontWeights.Bold);
        }
        else
        {
            DrawText(dc, text, 18, brush, new Point(rect.Left, rect.Top + 9), TextAlignment.Center, rect.Width, FontWeights.Bold);
        }
    }

    private static void DrawText(DrawingContext dc, string text, double size, Brush brush, Point point, TextAlignment align, double maxWidth = 120, FontWeight? weight = null)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip)
        { TextAlignment = align, MaxTextWidth = maxWidth };
        dc.DrawText(ft, point);
    }
}
