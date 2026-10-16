using System.Globalization;
using System.Windows.Media;

namespace DiskRadar;

public enum Safety { Unknown, System, Program, Cleanable, Personal }

public sealed class Node
{
    public string Name = "";
    public string FullPath = "";
    public bool IsDir;
    public long Size;
    public int FileCount;
    public DateTime LastWrite;
    public DateTime LastAccess;
    public Node? Parent;
    public List<Node>? Children;
    public Safety Safety;
    public string Note = "";
    public string Group { get; set; } = "";   // duplicate group label

    // Properties below are for data binding
    public string DisplayName => (IsDir ? "📁 " : "📄 ") + Name;
    public string ParentPath => Parent?.FullPath ?? "";
    public long SizeValue => Size;
    public string SizeText => Fmt.Size(Size);
    public int Files => FileCount;
    public double Percent => Parent == null || Parent.Size == 0 ? 0 : 100.0 * Size / Parent.Size;
    public string PercentText => Percent.ToString("0.0", Fmt.Tr) + " %";

    public DateTime LastUsed =>
        Scanner.AccessReliable && LastAccess > LastWrite ? LastAccess : LastWrite;
    public string LastUsedText => LastUsed == DateTime.MinValue ? "-" : LastUsed.ToString("dd.MM.yyyy", Fmt.Tr);
    public int DaysUnused => LastUsed == DateTime.MinValue ? 99999 : (int)(DateTime.Now - LastUsed).TotalDays;
    public string UnusedText => Fmt.Days(DaysUnused);

    public string SafetyText => Safety switch
    {
        Safety.System => "Sistem – dokunma",
        Safety.Program => "Program",
        Safety.Cleanable => "Silinebilir",
        Safety.Personal => "Kişisel",
        _ => "Bilinmiyor",
    };
    public Brush SafetyBrush => Palette.Pill[(int)Safety];
    public Brush TreemapBrush => IsDir ? Palette.Map[(int)Safety] : Palette.MapFile[(int)Safety];
}

public static class Palette
{
    static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
    // Index order matches the Safety enum
    public static readonly Brush[] Pill = { B("#00000000"), B("#F8D7DA"), B("#FFF3CD"), B("#D4EDDA"), B("#E2E8F0") };
    public static readonly Brush[] Map = { B("#B0BEC5"), B("#E57373"), B("#FFD54F"), B("#81C784"), B("#90A4AE") };
    public static readonly Brush[] MapFile = { B("#CFD8DC"), B("#EF9A9A"), B("#FFE082"), B("#A5D6A7"), B("#B0BEC5") };
}

public static class Fmt
{
    public static readonly CultureInfo Tr = new("tr-TR");

    public static string Size(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : "0.0", Tr) + " " + u[i];
    }

    public static string Days(int d)
    {
        if (d >= 99999) return "-";
        if (d < 1) return "bugün";
        if (d < 31) return $"{d} gün";
        if (d < 365) return $"{d / 30} ay";
        return (d / 365.0).ToString("0.0", Tr) + " yıl";
    }
}
