using System.Globalization;
using Microsoft.Win32;

namespace DiskRadar;

public sealed class InstalledApp
{
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Location { get; init; } = "";
    public long SizeValue { get; set; }
    public string SizeText => SizeValue > 0 ? Fmt.Size(SizeValue) : "-";
    public DateTime? Installed { get; init; }
    public string InstalledText => Installed?.ToString("dd.MM.yyyy") ?? "-";
    public DateTime? LastUsed { get; set; }
    public string LastUsedText => LastUsed?.ToString("dd.MM.yyyy") ?? "-";
    public int DaysUnused => LastUsed == null ? 99999 : (int)(DateTime.Now - LastUsed.Value).TotalDays;
    public string UnusedText => Fmt.Days(DaysUnused);

    const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Reads the Windows uninstall list; when the install folder was scanned, its real size and file dates are used.</summary>
    public static List<InstalledApp> Load(Node? scanned)
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default),
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var list = baseKey.OpenSubKey(Key);
                if (list == null) continue;
                foreach (var sub in list.GetSubKeyNames())
                {
                    using var k = list.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") != null) continue;   // updates/patches
                    if (apps.ContainsKey(name)) continue;

                    var loc = (k.GetValue("InstallLocation") as string)?.Trim('"', ' ') ?? "";
                    DateTime? installed = DateTime.TryParseExact(k.GetValue("InstallDate") as string, "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                    var app = new InstalledApp
                    {
                        Name = name,
                        Publisher = k.GetValue("Publisher") as string ?? "",
                        Location = loc,
                        Installed = installed,
                        SizeValue = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0,
                    };
                    if (scanned != null && loc.Length > 3 && Scanner.Find(scanned, loc) is { IsDir: true } node)
                    {
                        app.SizeValue = node.Size;
                        app.LastUsed = node.LastUsed;
                    }
                    apps[name] = app;
                }
            }
            catch { }
        }
        return apps.Values.OrderByDescending(a => a.SizeValue).ToList();
    }
}
