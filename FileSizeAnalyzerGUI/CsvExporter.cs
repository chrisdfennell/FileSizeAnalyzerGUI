using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileSizeAnalyzerGUI
{
    public static class CsvExporter
    {
        public static void ExportToCsv(List<FileSystemNode> files, string filePath)
        {
            var csvLines = new List<string> { "Path,Size,Creation Time,Last Write Time,Extension" };
            foreach (var file in files)
            {
                string line = $"\"{file.FullPath}\",\"{file.Size}\",\"{file.CreationTime:yyyy-MM-dd HH:mm:ss}\",\"{file.LastWriteTime:yyyy-MM-dd HH:mm:ss}\",\"{file.Extension}\"";
                csvLines.Add(line);
            }
            File.WriteAllLines(filePath, csvLines, Encoding.UTF8);
        }
    }
}
