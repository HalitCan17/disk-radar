using System.IO;
using System.Security.Cryptography;

namespace DiskRadar;

/// <summary>Finds files with identical content: same size → same first 64 KB → same full SHA-256.</summary>
public static class Duplicates
{
    public static long BytesRead;
    const long MinSize = 1L << 20;   // ignore files under 1 MB
    const int Head = 64 * 1024;

    public static List<Node> Find(Node root, CancellationToken ct, out long wasted, out int groups)
    {
        BytesRead = 0; wasted = 0; groups = 0;
        var result = new List<Node>();
        var bySize = Scanner.All(root)
            .Where(n => !n.IsDir && n.Size >= MinSize && n.Safety != Safety.System)
            .GroupBy(n => n.Size)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var sizeGroup in bySize)
        {
            foreach (var headGroup in sizeGroup.GroupBy(n => Hash(n.FullPath, Head, ct)).Where(g => g.Key != null && g.Count() > 1))
            foreach (var same in headGroup.GroupBy(n => Hash(n.FullPath, long.MaxValue, ct)).Where(g => g.Key != null && g.Count() > 1))
            {
                var list = same.ToList();
                long extra = sizeGroup.Key * (list.Count - 1);
                groups++; wasted += extra;
                foreach (var n in list)
                {
                    n.Group = $"#{groups} · {list.Count} kopya · {Fmt.Size(extra)} fazla";
                    result.Add(n);
                }
            }
        }
        return result;
    }

    static string? Hash(string path, long limit, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            long left = limit;
            int r;
            while (left > 0 && (r = fs.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buf, 0, r);
                left -= r;
                Interlocked.Add(ref BytesRead, r);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }
}
