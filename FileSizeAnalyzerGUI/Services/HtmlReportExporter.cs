using FileSizeAnalyzerGUI;
using System.IO;
using System.Text;
using System.Text.Json;

internal static class HtmlReportExporter
{
    public static void Export(
        string outPath,
        FileSystemNode rootNode,
        List<DuplicateSet> duplicateGroups,
        string lastRule,
        string? scanPath = null)
    {
        // Build a lookup: full path -> duplicate group id (acts as "hash" for the HTML)
        var dupLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < (duplicateGroups?.Count ?? 0); i++)
        {
            var set = duplicateGroups[i];
            var key = $"dup{i + 1}";
            if (set?.Files == null) continue;
            foreach (var f in set.Files)
            {
                if (!string.IsNullOrEmpty(f?.FullPath))
                    dupLookup[f.FullPath] = key;
            }
        }

        // Flatten all files from the tree
        var rows = new List<object>(capacity: 4096);
        foreach (var f in EnumerateFiles(rootNode))
        {
            dupLookup.TryGetValue(f.FullPath, out var groupKey);
            rows.Add(new
            {
                path = f.FullPath,
                size = f.Size,
                modified = f.LastWriteTime.ToString("o"),
                hash = groupKey // null for non-duplicates; OK for the HTML script
            });
        }

        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });

        // Load template and inject tokens
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var templatePath = Path.Combine(baseDir, "ReportTemplate.html");
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Report template not found at: {templatePath}");

        var html = File.ReadAllText(templatePath, Encoding.UTF8);
        html = html.Replace("__SCAN_JSON__", json);
        if (!string.IsNullOrWhiteSpace(scanPath))
            html = html.Replace("__SCAN_PATH__", HtmlSafe(scanPath));
        if (!string.IsNullOrWhiteSpace(lastRule))
            html = html.Replace("__LAST_RULE__", HtmlSafe(lastRule));

        File.WriteAllText(outPath, html, Encoding.UTF8);
    }

    private static IEnumerable<FileSystemNode> EnumerateFiles(FileSystemNode node)
    {
        if (node == null) yield break;
        var stack = new Stack<FileSystemNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur.IsDirectory)
            {
                if (cur.Children != null)
                    for (int i = 0; i < cur.Children.Count; i++)
                        stack.Push(cur.Children[i]);
            }
            else
            {
                yield return cur;
            }
        }
    }

    private static string HtmlSafe(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&#39;");
}