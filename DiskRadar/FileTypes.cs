using System.IO;

namespace DiskRadar;

public sealed class TypeRow
{
    public string Category { get; init; } = "";
    public int Count { get; set; }
    public long SizeValue { get; set; }
    public string SizeText => Fmt.Size(SizeValue);
    public double Percent { get; set; }
    public string PercentText => Percent.ToString("0.0", Fmt.Tr) + " %";
    public string TopExtensions { get; set; } = "";
}

/// <summary>Groups every file by kind (video, photo, archive…) using its extension.</summary>
public static class FileTypes
{
    public const string Other = "Diğer";

    static readonly Dictionary<string, string> Map = Build(new()
    {
        ["Video"] = "mp4 mkv avi mov wmv flv webm m4v mpg mpeg 3gp ts m2ts",
        ["Fotoğraf & görsel"] = "jpg jpeg png gif bmp heic heif webp tif tiff raw cr2 cr3 nef arw dng psd svg ico",
        ["Müzik & ses"] = "mp3 wav flac aac ogg m4a wma opus aiff",
        ["Belgeler"] = "pdf doc docx xls xlsx xlsm ppt pptx txt rtf odt ods odp csv md epub",
        ["Arşiv (zip, rar…)"] = "zip rar 7z tar gz tgz bz2 xz cab zst",
        ["Kurulum & disk kalıbı"] = "iso img msi msix msixbundle appx appxbundle dmg",
        ["Sanal disk (VM, WSL, Docker)"] = "vhd vhdx vmdk vdi qcow2 avhdx",
        ["Program & kütüphane"] = "exe dll sys ocx so lib pdb node winmd mui",
        ["Kaynak kod"] = "cs js ts tsx jsx py java kt cpp c h hpp go rs php rb swift json xml html htm css scss sql yaml yml xaml razor cshtml",
        ["Veritabanı & e-posta & yedek"] = "db sqlite sqlite3 mdf ldf ndf bak pst ost edb accdb mdb",
        ["Günlük & geçici"] = "log tmp temp etl dmp mdmp old",
        ["Oyun & paket verisi"] = "pak vpk bundle assets bsa ba2 ucas utoc forge",
    });

    static Dictionary<string, string> Build(Dictionary<string, string> groups)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cat, exts) in groups)
            foreach (var e in exts.Split(' ', StringSplitOptions.RemoveEmptyEntries)) d["." + e] = cat;
        return d;
    }

    public static string CategoryOf(Node n) => Map.GetValueOrDefault(Path.GetExtension(n.Name), Other);

    public static List<TypeRow> Summarize(Node root)
    {
        var rows = new Dictionary<string, TypeRow>();
        var exts = new Dictionary<string, Dictionary<string, long>>();
        foreach (var n in Scanner.All(root))
        {
            if (n.IsDir) continue;
            var cat = CategoryOf(n);
            if (!rows.TryGetValue(cat, out var r)) { rows[cat] = r = new TypeRow { Category = cat }; exts[cat] = new(); }
            r.Count++;
            r.SizeValue += n.Size;
            var ext = Path.GetExtension(n.Name).ToLowerInvariant();
            if (ext.Length == 0) ext = "(uzantısız)";
            exts[cat][ext] = exts[cat].GetValueOrDefault(ext) + n.Size;
        }
        long total = Math.Max(1, rows.Values.Sum(r => r.SizeValue));
        foreach (var r in rows.Values)
        {
            r.Percent = 100.0 * r.SizeValue / total;
            r.TopExtensions = string.Join(", ", exts[r.Category].OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => $"{kv.Key} {Fmt.Size(kv.Value)}"));
        }
        return rows.Values.OrderByDescending(r => r.SizeValue).ToList();
    }

    public static List<Node> Files(Node root, string category, int max) =>
        Scanner.All(root).Where(n => !n.IsDir && CategoryOf(n) == category)
               .OrderByDescending(n => n.Size).Take(max).ToList();
}
