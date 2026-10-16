using System.IO;

namespace DiskRadar;

public sealed class ProjectRow
{
    public Node Node { get; init; } = null!;
    public string Name => Node.Name;
    public string FolderPath => Node.FullPath;
    public string Kind { get; init; } = "";
    public long SizeValue => Node.Size;
    public string SizeText => Node.SizeText;
    public long RebuildValue { get; set; }
    public string RebuildText => RebuildValue > 0 ? Fmt.Size(RebuildValue) : "-";
    public string LastUsedText => Node.LastUsedText;
    public int DaysUnused => Node.DaysUnused;
    public string UnusedText => Node.UnusedText;
    public List<Node> Rebuildable { get; } = new();
}

/// <summary>Finds software projects and the folders inside them that their tools can regenerate (bin, obj, node_modules…).</summary>
public static class Projects
{
    // marker file/folder → project kind
    static readonly (Func<string, bool> Match, string Kind)[] Markers =
    {
        (n => n.EndsWith(".sln") || n.EndsWith(".slnx") || n.EndsWith(".csproj") || n.EndsWith(".vbproj") || n.EndsWith(".fsproj"), ".NET"),
        (n => n == "package.json", "Node.js"),
        (n => n == "pom.xml" || n == "build.gradle" || n == "build.gradle.kts", "Java/Kotlin"),
        (n => n == "cargo.toml", "Rust"),
        (n => n == "pyproject.toml" || n == "requirements.txt" || n == "setup.py", "Python"),
        (n => n == "go.mod", "Go"),
        (n => n == "composer.json", "PHP"),
        (n => n == "pubspec.yaml", "Flutter"),
        (n => n == "cmakelists.txt", "C/C++"),
    };

    // regenerable folder name → kinds it belongs to (null = any project)
    static readonly Dictionary<string, string[]?> Rebuildable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node_modules"] = null, [".vs"] = null, ["__pycache__"] = null, [".pytest_cache"] = null,
        ["bin"] = new[] { ".NET" }, ["obj"] = new[] { ".NET" },
        [".venv"] = new[] { "Python" }, ["venv"] = new[] { "Python" },
        ["dist"] = new[] { "Node.js", "Python" }, [".next"] = new[] { "Node.js" }, [".nuxt"] = new[] { "Node.js" },
        [".angular"] = new[] { "Node.js" }, [".parcel-cache"] = new[] { "Node.js" },
        ["target"] = new[] { "Java/Kotlin", "Rust" }, [".gradle"] = new[] { "Java/Kotlin" },
        ["build"] = new[] { "Java/Kotlin", "Flutter", "C/C++" }, [".dart_tool"] = new[] { "Flutter" },
    };

    const string Note = "Derleme çıktısı / bağımlılık. Projeden yeniden oluşturulabilir (ör. dotnet build, npm install).";

    public static List<ProjectRow> Find(Node root)
    {
        var result = new List<ProjectRow>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsDir || n.Children == null) continue;
            if (n.Safety is Safety.System or Safety.Program || Rebuildable.ContainsKey(n.Name)) continue;

            var kinds = Kinds(n);
            if (kinds.Count > 0)
            {
                var row = new ProjectRow { Node = n, Kind = string.Join(", ", kinds) };
                Collect(n, kinds, row);
                row.RebuildValue = row.Rebuildable.Sum(r => r.Size);
                result.Add(row);
                continue;   // nested projects (e.g. projects inside a solution) are part of this one
            }
            foreach (var c in n.Children) if (c.IsDir) stack.Push(c);
        }
        return result.OrderByDescending(r => r.RebuildValue).ThenByDescending(r => r.SizeValue).ToList();
    }

    static HashSet<string> Kinds(Node dir)
    {
        var kinds = new HashSet<string>();
        foreach (var c in dir.Children!)
        {
            if (c.IsDir) continue;
            var name = c.Name.ToLowerInvariant();
            foreach (var (match, kind) in Markers) if (match(name)) kinds.Add(kind);
        }
        return kinds;
    }

    static void Collect(Node dir, HashSet<string> kinds, ProjectRow row)
    {
        if (dir.Children == null) return;
        foreach (var c in dir.Children)
        {
            if (!c.IsDir) continue;
            var sub = Kinds(c);
            var all = sub.Count > 0 ? new HashSet<string>(kinds.Concat(sub)) : kinds;
            if (Rebuildable.TryGetValue(c.Name, out var need) && (need == null || need.Any(all.Contains)))
            {
                row.Rebuildable.Add(c);
                Mark(c);
                continue;
            }
            Collect(c, all, row);
        }
    }

    static void Mark(Node n)
    {
        foreach (var x in Scanner.All(n))
            if (x.Safety is Safety.Unknown or Safety.Personal) { x.Safety = Safety.Cleanable; x.Note = Note; }
    }
}
