using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace HWT905Dashboard.Controls;

public sealed class Orientation3DControl : FrameworkElement
{
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }

    public void SetValues(double roll, double pitch, double yaw)
    {
        Roll = roll;
        Pitch = pitch;
        Yaw = yaw;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 50 || h < 50) return;

        dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(5, 13, 20), Color.FromRgb(11, 22, 31), 90), null, new Rect(0, 0, w, h), 8, 8);

        Point center = new(w / 2, h / 2 + 12);
        double scale = Math.Min(w, h) * 0.28;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 105, 130, 155)), 1);
        Vec[] cube =
        {
            new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1),
            new(-1,-1, 1), new(1,-1, 1), new(1,1, 1), new(-1,1, 1)
        };
        int[,] edges =
        {
            {0,1},{1,2},{2,3},{3,0}, {4,5},{5,6},{6,7},{7,4}, {0,4},{1,5},{2,6},{3,7}
        };
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            var p1 = Project(cube[edges[i, 0]], center, scale);
            var p2 = Project(cube[edges[i, 1]], center, scale);
            dc.DrawLine(gridPen, p1, p2);
        }

        // Fixed Z axis for visual reference.
        DrawAxis(dc, center, Project(new Vec(0, 0, 1.55), center, scale), Brushes.DodgerBlue, "Z");

        // Rotated aircraft axes.
        Vec x = Rotate(new Vec(1.55, 0, 0));
        Vec y = Rotate(new Vec(0, 1.55, 0));
        Vec z = Rotate(new Vec(0, 0, -1.35));
        DrawAxis(dc, center, Project(x, center, scale), Brushes.Red, "X");
        DrawAxis(dc, center, Project(y, center, scale), Brushes.LawnGreen, "Y");
        DrawAxis(dc, center, Project(z, center, scale), Brushes.Blue, "");

        // Little body disk and wings.
        Point nose = Project(Rotate(new Vec(0.45, 0, 0)), center, scale);
        Point tail = Project(Rotate(new Vec(-0.45, 0, 0)), center, scale);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(205, 214, 224)), 5), tail, nose);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(180, 190, 200)), new Pen(new SolidColorBrush(Color.FromRgb(80, 90, 100)), 1), center, 12, 12);
    }

    private Vec Rotate(Vec v)
    {
        double yaw = Yaw * Math.PI / 180.0;
        double pitch = Pitch * Math.PI / 180.0;
        double roll = Roll * Math.PI / 180.0;

        // Z yaw
        double x1 = v.X * Math.Cos(yaw) - v.Y * Math.Sin(yaw);
        double y1 = v.X * Math.Sin(yaw) + v.Y * Math.Cos(yaw);
        double z1 = v.Z;
        // Y pitch
        double x2 = x1 * Math.Cos(pitch) + z1 * Math.Sin(pitch);
        double y2 = y1;
        double z2 = -x1 * Math.Sin(pitch) + z1 * Math.Cos(pitch);
        // X roll
        double x3 = x2;
        double y3 = y2 * Math.Cos(roll) - z2 * Math.Sin(roll);
        double z3 = y2 * Math.Sin(roll) + z2 * Math.Cos(roll);
        return new Vec(x3, y3, z3);
    }

    private static Point Project(Vec v, Point center, double scale)
    {
        // Isometric-ish projection.
        double x = (v.X - v.Y) * 0.82;
        double y = (v.X + v.Y) * 0.38 - v.Z * 0.85;
        return new Point(center.X + x * scale, center.Y + y * scale);
    }

    private static void DrawAxis(DrawingContext dc, Point start, Point end, Brush brush, string label)
    {
        var pen = new Pen(brush, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, start, end);
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 1)
        {
            double ux = dx / len;
            double uy = dy / len;
            Point a = new(end.X - ux * 14 - uy * 7, end.Y - uy * 14 + ux * 7);
            Point b = new(end.X - ux * 14 + uy * 7, end.Y - uy * 14 - ux * 7);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(end, true, true);
                ctx.LineTo(a, true, false);
                ctx.LineTo(b, true, false);
            }
            dc.DrawGeometry(brush, null, g);
        }
        if (!string.IsNullOrEmpty(label))
            DrawText(dc, label, 23, brush, new Point(end.X + 8, end.Y - 24));
    }

    private static void DrawText(DrawingContext dc, string text, double size, Brush brush, Point p)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
        dc.DrawText(ft, p);
    }

    private readonly struct Vec
    {
        public Vec(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }
}
