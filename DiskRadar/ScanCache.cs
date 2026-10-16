using System.IO;

namespace DiskRadar;

/// <summary>Saves the last scanned tree so the app can show it instantly on the next start.</summary>
public static class ScanCache
{
    const int Version = 1;
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskRadar", "last-scan.bin");

    public static void Save(Node root, DateTime date)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Version);
                w.Write(date.Ticks);
                w.Write(root.FullPath);
                Write(w, root);
            }
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }

    static void Write(BinaryWriter w, Node n)
    {
        w.Write(n.Name);
        w.Write((byte)((n.IsDir ? 1 : 0) | (n.Note.StartsWith("Kısayol bağlantısı") ? 2 : 0)));
        w.Write(n.Size);
        w.Write(n.FileCount);
        w.Write(n.LastWrite.Ticks);
        w.Write(n.LastAccess.Ticks);
        w.Write(n.Children?.Count ?? -1);
        if (n.Children != null) foreach (var c in n.Children) Write(w, c);
    }

    public static (Node Root, DateTime Date)? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var r = new BinaryReader(fs);
            if (r.ReadInt32() != Version) return null;
            var date = new DateTime(r.ReadInt64());
            var rootPath = r.ReadString();
            return (Read(r, null, rootPath), date);
        }
        catch { return null; }
    }

    static Node Read(BinaryReader r, Node? parent, string rootPath)
    {
        var name = r.ReadString();
        var flags = r.ReadByte();
        var n = new Node
        {
            Name = name,
            FullPath = parent == null ? rootPath : Path.Join(parent.FullPath, name),
            Parent = parent,
            IsDir = (flags & 1) != 0,
            Size = r.ReadInt64(),
            FileCount = r.ReadInt32(),
            LastWrite = new DateTime(r.ReadInt64()),
            LastAccess = new DateTime(r.ReadInt64()),
        };
        SafetyRules.Classify(n, parent);
        if ((flags & 2) != 0) n.Note = "Kısayol bağlantısı (junction) – içeriği başka yerde sayılır. " + n.Note;
        int count = r.ReadInt32();
        if (count >= 0)
        {
            n.Children = new List<Node>(count);
            for (int i = 0; i < count; i++) n.Children.Add(Read(r, n, rootPath));
        }
        return n;
    }
}
