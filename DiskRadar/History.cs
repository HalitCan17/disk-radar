using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiskRadar;

public sealed class Snapshot
{
    public string Root { get; set; } = "";
    public DateTime Date { get; set; }
    public long Total { get; set; }
    public Dictionary<string, long> Folders { get; set; } = new();
}

public sealed class ChangeRow
{
    public string FolderPath { get; init; } = "";
    public long Before { get; init; }
    public long After { get; init; }
    public string BeforeText => Fmt.Size(Before);
    public string AfterText => Fmt.Size(After);
    public long Diff => After - Before;
    public string DiffText => (Diff >= 0 ? "+" : "−") + Fmt.Size(Math.Abs(Diff));
    public long AbsDiff => Math.Abs(Diff);
}

/// <summary>Stores folder sizes (4 levels deep) after each scan so the next scan can show what grew.</summary>
public static class History
{
    const int Depth = 4;
    const long MinChange = 10L << 20;
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskRadar", "history");

    static string Key(string root) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(root.TrimEnd('\\').ToLowerInvariant())))[..12];

    public static Snapshot? LoadLatest(string root)
    {
        try
        {
            if (!Directory.Exists(Dir)) return null;
            var file = Directory.GetFiles(Dir, Key(root) + "_*.json").OrderDescending().FirstOrDefault();
            return file == null ? null : JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    /// <summary>All saved scans of this location, oldest first (at most the last 60).</summary>
    public static List<Snapshot> LoadAll(string root)
    {
        var list = new List<Snapshot>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            foreach (var file in Directory.GetFiles(Dir, Key(root) + "_*.json").Order().TakeLast(60))
            {
                try { if (JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file)) is { } s) list.Add(s); }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public static Snapshot From(Node root)
    {
        var s = new Snapshot { Root = root.FullPath, Date = DateTime.Now, Total = root.Size };
        void Walk(Node n, int depth)
        {
            s.Folders[n.FullPath] = n.Size;
            if (depth >= Depth || n.Children == null) return;
            foreach (var c in n.Children) if (c.IsDir) Walk(c, depth + 1);
        }
        Walk(root, 0);
        return s;
    }

    public static void Save(Snapshot s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, $"{Key(s.Root)}_{s.Date:yyyyMMdd_HHmmss}.json"), JsonSerializer.Serialize(s));
        }
        catch { }
    }

    public static List<ChangeRow> Compare(Snapshot before, Snapshot after) =>
        before.Folders.Keys.Union(after.Folders.Keys)
            .Select(p => new ChangeRow
            {
                FolderPath = p,
                Before = before.Folders.GetValueOrDefault(p),
                After = after.Folders.GetValueOrDefault(p),
            })
            .Where(r => r.AbsDiff >= MinChange && r.FolderPath != after.Root)
            .OrderByDescending(r => r.AbsDiff)
            .Take(300)
            .ToList();
}
