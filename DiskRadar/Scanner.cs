using System.IO;
using Microsoft.Win32;

namespace DiskRadar;

public static class Scanner
{
    public static long ItemCount;
    public static long BytesScanned;
    public static volatile string Current = "";

    /// <summary>NTFS "last access" timestamps are only meaningful when Windows updates them.</summary>
    public static readonly bool AccessReliable = DetectLastAccess();

    static bool DetectLastAccess()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            var v = k?.GetValue("NtfsDisableLastAccessUpdate");
            if (v is int i) return (i & 1) == 0;
        }
        catch { }
        return false;
    }

    static readonly EnumerationOptions Opts = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
    };

    public static Node Scan(string root, CancellationToken ct)
    {
        ItemCount = 0;
        BytesScanned = 0;
        var di = new DirectoryInfo(root);
        var node = new Node { Name = di.FullName, FullPath = di.FullName, IsDir = true, LastWrite = di.LastWriteTime };
        SafetyRules.Classify(node, null);
        ScanDir(node, di, 0, ct);
        return node;
    }

    static void ScanDir(Node node, DirectoryInfo di, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Current = node.FullPath;
        var kids = new List<Node>();
        var subdirs = new List<(Node, DirectoryInfo)>();
        try
        {
            foreach (var fsi in di.EnumerateFileSystemInfos("*", Opts))
            {
                // Directory access times are skipped: listing a folder updates its own access time.
                var c = new Node { Name = fsi.Name, FullPath = fsi.FullName, Parent = node, LastWrite = fsi.LastWriteTime };
                if (fsi is DirectoryInfo sd)
                {
                    c.IsDir = true;
                    SafetyRules.Classify(c, node);
                    if ((fsi.Attributes & FileAttributes.ReparsePoint) == 0) subdirs.Add((c, sd));
                    else c.Note = "Kısayol bağlantısı (junction) – içeriği başka yerde sayılır. " + c.Note;
                }
                else
                {
                    c.Size = ((FileInfo)fsi).Length;
                    Interlocked.Add(ref BytesScanned, c.Size);
                    c.FileCount = 1;
                    c.LastAccess = fsi.LastAccessTime;
                    SafetyRules.Classify(c, node);
                }
                kids.Add(c);
                Interlocked.Increment(ref ItemCount);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { }

        if (depth < 3 && subdirs.Count > 1)
            Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = ct }, s => ScanDir(s.Item1, s.Item2, depth + 1, ct));
        else
            foreach (var s in subdirs) ScanDir(s.Item1, s.Item2, depth + 1, ct);

        long size = 0; int files = 0;
        DateTime lw = node.LastWrite, la = DateTime.MinValue;
        foreach (var k in kids)
        {
            size += k.Size; files += k.FileCount;
            if (k.LastWrite > lw) lw = k.LastWrite;
            if (k.LastAccess > la) la = k.LastAccess;
        }
        kids.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Children = kids;
        node.Size = size; node.FileCount = files; node.LastWrite = lw; node.LastAccess = la;
    }

    public static IEnumerable<Node> All(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            if (n.Children != null) foreach (var c in n.Children) stack.Push(c);
        }
    }

    public static List<Node> BiggestFiles(Node root, int count) =>
        All(root).Where(n => !n.IsDir).OrderByDescending(n => n.Size).Take(count).ToList();

    /// <summary>Largest folders/files untouched for at least <paramref name="days"/>; stops descending once a whole folder qualifies.</summary>
    public static List<Node> Unused(Node root, int days, long minSize)
    {
        var result = new List<Node>();
        var stack = new Stack<Node>();
        if (root.Children != null) foreach (var c in root.Children) stack.Push(c);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Size < minSize) continue;
            if (n.DaysUnused >= days) { result.Add(n); continue; }
            if (n.Children != null) foreach (var c in n.Children) stack.Push(c);
        }
        result.Sort((a, b) => b.Size.CompareTo(a.Size));
        return result;
    }

    public static List<Node> Search(Node root, string text, int max) =>
        All(root).Where(n => n.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                 .OrderByDescending(n => n.Size).Take(max).ToList();

    /// <summary>Top-most items labelled "cleanable" (children of a cleanable folder are not repeated).</summary>
    public static List<Node> Cleanable(Node root)
    {
        var result = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Safety == Safety.Cleanable) { if (n.Size >= 1 << 20) result.Add(n); continue; }
            if (n.Children != null) foreach (var c in n.Children) if (c.IsDir || c.Safety == Safety.Cleanable) stack.Push(c);
        }
        result.Sort((a, b) => b.Size.CompareTo(a.Size));
        return result;
    }

    public static Node? Find(Node root, string path)
    {
        path = path.TrimEnd('\\');
        var r = root.FullPath.TrimEnd('\\');
        if (!path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
        Node? n = root;
        foreach (var part in path[r.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            n = n.Children?.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
        }
        return n;
    }
}
