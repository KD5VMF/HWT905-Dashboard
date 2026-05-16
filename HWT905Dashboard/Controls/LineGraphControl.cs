using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace HWT905Dashboard.Controls;

public sealed class LineGraphControl : FrameworkElement
{
    private readonly Queue<double> _a = new();
    private readonly Queue<double> _b = new();
    private readonly Queue<double> _c = new();

    public string Title { get; set; } = "GRAPH";
    public double RangeMin { get; set; } = -1;
    public double RangeMax { get; set; } = 1;
    public int MaxPoints { get; set; } = 240;
    public string SeriesAName { get; set; } = "X";
    public string SeriesBName { get; set; } = "Y";
    public string SeriesCName { get; set; } = "Z";

    public void Add(double a, double b, double c)
    {
        Enqueue(_a, a);
        Enqueue(_b, b);
        Enqueue(_c, c);
        InvalidateVisual();
    }

    public void Clear()
    {
        _a.Clear(); _b.Clear(); _c.Clear();
        InvalidateVisual();
    }

    private void Enqueue(Queue<double> q, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        q.Enqueue(value);
        while (q.Count > MaxPoints) q.Dequeue();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 50 || h < 50) return;

        var bg = new LinearGradientBrush(Color.FromRgb(5, 13, 20), Color.FromRgb(11, 22, 31), 90);
        dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, w, h), 8, 8);
        DrawText(dc, Title, 15, Brushes.White, new Point(10, 8), FontWeights.Bold);

        Rect plot = new(42, 42, Math.Max(10, w - 54), Math.Max(10, h - 60));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 120, 145, 165)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 150, 172, 190)), 1);

        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Top + i * plot.Height / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            double v = RangeMax - (RangeMax - RangeMin) * i / 4.0;
            DrawText(dc, v.ToString("0.#", CultureInfo.InvariantCulture), 12, new SolidColorBrush(Color.FromRgb(200, 212, 225)), new Point(4, y - 9));
        }
        for (int i = 0; i <= 4; i++)
        {
            double x = plot.Left + i * plot.Width / 4;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            string label = i == 4 ? "0s" : $"-{(4 - i) * 15}s";
            DrawText(dc, label, 12, new SolidColorBrush(Color.FromRgb(200, 212, 225)), new Point(x - 12, plot.Bottom + 4));
        }
        dc.DrawRectangle(null, axisPen, plot);

        DrawSeries(dc, plot, _a.ToArray(), new SolidColorBrush(Color.FromRgb(255, 65, 55)));
        DrawSeries(dc, plot, _b.ToArray(), new SolidColorBrush(Color.FromRgb(110, 255, 50)));
        DrawSeries(dc, plot, _c.ToArray(), new SolidColorBrush(Color.FromRgb(50, 145, 255)));

        DrawLegend(dc, w);
    }

    private void DrawSeries(DrawingContext dc, Rect plot, double[] values, Brush brush)
    {
        if (values.Length < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                double x = plot.Left + (values.Length == 1 ? 0 : i * plot.Width / (values.Length - 1));
                double norm = (values[i] - RangeMin) / Math.Max(0.001, RangeMax - RangeMin);
                norm = Math.Max(0, Math.Min(1, norm));
                double y = plot.Bottom - norm * plot.Height;
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(brush, 2), geo);
    }

    private void DrawLegend(DrawingContext dc, double w)
    {
        double x = Math.Max(160, w - 190);
        DrawLegendItem(dc, x, 13, SeriesAName, new SolidColorBrush(Color.FromRgb(255, 65, 55)));
        DrawLegendItem(dc, x + 58, 13, SeriesBName, new SolidColorBrush(Color.FromRgb(110, 255, 50)));
        DrawLegendItem(dc, x + 116, 13, SeriesCName, new SolidColorBrush(Color.FromRgb(50, 145, 255)));
    }

    private static void DrawLegendItem(DrawingContext dc, double x, double y, string name, Brush brush)
    {
        dc.DrawLine(new Pen(brush, 3), new Point(x, y + 8), new Point(x + 14, y + 8));
        DrawText(dc, name, 12, brush, new Point(x + 19, y), FontWeights.Bold);
    }

    private static void DrawText(DrawingContext dc, string text, double size, Brush brush, Point point, FontWeight? weight = null)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
        dc.DrawText(ft, point);
    }
}
