using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;

namespace DiskRadar;

/// <summary>Writes whatever a DataGrid shows (its bound columns, in current sort order) as an Excel-friendly CSV.</summary>
public static class CsvExport
{
    public static int Write(DataGrid grid, string file)
    {
        var cols = grid.Columns.OfType<DataGridBoundColumn>()
            .Select(c => (Header: c.Header?.ToString() ?? "", Path: (c.Binding as Binding)?.Path.Path))
            .Where(c => c.Path != null).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", cols.Select(c => Esc(c.Header))));
        int rows = 0;
        foreach (var item in grid.Items)
        {
            var t = item.GetType();
            sb.AppendLine(string.Join(";", cols.Select(c => Esc(t.GetProperty(c.Path!)?.GetValue(item)?.ToString() ?? ""))));
            rows++;
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        return rows;
    }

    static string Esc(string s) => "\"" + s.Replace("\"", "\"\"").Replace("📁 ", "").Replace("📄 ", "") + "\"";
}
