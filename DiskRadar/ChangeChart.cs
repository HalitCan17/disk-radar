using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DiskRadar;

/// <summary>Line chart of total scanned size per scan date.</summary>
public sealed class ChangeChart : Canvas
{
    List<(DateTime Date, long Total)> points = new();
    static readonly Brush Line = Frozen("#4F8CFF");
    static readonly Brush Axis = Frozen("#808080");

    static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public ChangeChart()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Render();
    }

    public void SetData(List<(DateTime, long)> data) { points = data; Render(); }

    void Render()
    {
        Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < 50 || h < 50) return;
        if (points.Count < 2)
        {
            Add(new TextBlock { Text = "Grafik için aynı konumun en az 2 taraması gerekir.", Foreground = Axis }, 8, h / 2 - 10);
            return;
        }

        const double left = 80, right = 16, top = 12, bottom = 26;
        long min = points.Min(p => p.Total), max = points.Max(p => p.Total);
        if (max == min) { max += 1L << 30; min = Math.Max(0, min - (1L << 30)); }
        double t0 = points[0].Date.Ticks, t1 = points[^1].Date.Ticks;
        double X(DateTime d) => left + (w - left - right) * (t1 == t0 ? 0.5 : (d.Ticks - t0) / (t1 - t0));
        double Y(long v) => top + (h - top - bottom) * (1 - (double)(v - min) / (max - min));

        Children.Add(new System.Windows.Shapes.Line { X1 = left, Y1 = top, X2 = left, Y2 = h - bottom, Stroke = Axis, StrokeThickness = 1 });
        Children.Add(new System.Windows.Shapes.Line { X1 = left, Y1 = h - bottom, X2 = w - right, Y2 = h - bottom, Stroke = Axis, StrokeThickness = 1 });
        Add(Label(Fmt.Size(max)), 4, top - 8);
        Add(Label(Fmt.Size(min)), 4, h - bottom - 10);
        Add(Label(points[0].Date.ToString("dd.MM.yyyy")), left, h - bottom + 4);
        var last = Label(points[^1].Date.ToString("dd.MM.yyyy"));
        Add(last, w - right - 70, h - bottom + 4);

        var poly = new Polyline { Stroke = Line, StrokeThickness = 2 };
        foreach (var p in points) poly.Points.Add(new Point(X(p.Date), Y(p.Total)));
        Children.Add(poly);
        foreach (var p in points)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Line, ToolTip = $"{p.Date:dd.MM.yyyy HH:mm}\n{Fmt.Size(p.Total)}" };
            Add(dot, X(p.Date) - 4, Y(p.Total) - 4);
        }
    }

    static TextBlock Label(string s) => new() { Text = s, FontSize = 11, Foreground = Axis };

    void Add(UIElement e, double x, double y)
    {
        SetLeft(e, x);
        SetTop(e, y);
        Children.Add(e);
    }
}
