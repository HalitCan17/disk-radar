using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiskRadar;

/// <summary>Squarified treemap of one folder's direct children.</summary>
public sealed class Treemap : Canvas
{
    Node? folder;
    public event Action<Node>? NodeClicked;

    public Treemap()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Render();
    }

    public void Show(Node? f) { folder = f; Render(); }

    void Render()
    {
        Children.Clear();
        if (folder?.Children == null || ActualWidth < 10 || ActualHeight < 10) return;
        var items = folder.Children.Where(c => c.Size > 0).Take(400).ToList();
        double total = items.Sum(n => (double)n.Size);
        if (total == 0) return;

        double area = ActualWidth * ActualHeight;
        var list = items.Select(n => (n, n.Size / total * area)).ToList();
        var rects = new List<(Node, Rect)>();
        Squarify(list, new Rect(0, 0, ActualWidth, ActualHeight), rects);

        foreach (var (n, r) in rects)
        {
            if (r.Width < 2 || r.Height < 2) continue;
            var b = new Border
            {
                Width = r.Width - 1,
                Height = r.Height - 1,
                Background = n.TreemapBrush,
                ToolTip = $"{n.Name}\n{n.SizeText}  ·  {n.SafetyText}\nSon kullanım: {n.LastUsedText} ({n.UnusedText})\n{n.Note}".TrimEnd(),
                Cursor = n.IsDir ? Cursors.Hand : Cursors.Arrow,
            };
            if (r.Width > 70 && r.Height > 34)
                b.Child = new TextBlock
                {
                    Text = n.Name + "\n" + n.SizeText,
                    Margin = new Thickness(4),
                    FontSize = 11,
                    Foreground = Brushes.Black,   // tiles are light in both themes
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
            b.MouseLeftButtonUp += (_, _) => NodeClicked?.Invoke(n);
            SetLeft(b, r.X);
            SetTop(b, r.Y);
            Children.Add(b);
        }
    }

    static void Squarify(List<(Node n, double a)> items, Rect r, List<(Node, Rect)> output)
    {
        int i = 0;
        while (i < items.Count && r.Width > 0 && r.Height > 0)
        {
            double side = Math.Min(r.Width, r.Height);
            int j = i; double sum = 0, best = double.MaxValue;
            while (j < items.Count)
            {
                double s = sum + items[j].a;
                double w = Worst(items, i, j, s, side);
                if (w > best) break;
                best = w; sum = s; j++;
            }
            if (r.Width >= r.Height)
            {
                double colW = sum / r.Height, y = r.Y;
                for (int k = i; k < j; k++) { double h = items[k].a / colW; output.Add((items[k].n, new Rect(r.X, y, colW, h))); y += h; }
                r = new Rect(r.X + colW, r.Y, Math.Max(0, r.Width - colW), r.Height);
            }
            else
            {
                double rowH = sum / r.Width, x = r.X;
                for (int k = i; k < j; k++) { double w = items[k].a / rowH; output.Add((items[k].n, new Rect(x, r.Y, w, rowH))); x += w; }
                r = new Rect(r.X, r.Y + rowH, r.Width, Math.Max(0, r.Height - rowH));
            }
            i = j;
        }
    }

    static double Worst(List<(Node n, double a)> items, int from, int to, double sum, double side)
    {
        double max = 0, min = double.MaxValue;
        for (int k = from; k <= to; k++) { max = Math.Max(max, items[k].a); min = Math.Min(min, items[k].a); }
        double s2 = sum * sum, w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }
}
