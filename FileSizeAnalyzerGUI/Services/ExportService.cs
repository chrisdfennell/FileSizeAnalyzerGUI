using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileSizeAnalyzerGUI.Services
{
	public class ExportOptions
	{
		public bool IncludeFileList { get; set; } = true;
		public bool IncludeDuplicates { get; set; } = true;
		public bool IncludeFileTypes { get; set; } = true;
		public bool IncludeLargestFiles { get; set; } = true;
		public bool IncludeTemporaryFiles { get; set; } = true;
		public bool IncludeStaleFiles { get; set; } = true;
		public int MaxFilesToExport { get; set; } = 10000;
	}

	public class ExportData
	{
		public string ScanPath { get; set; } = string.Empty;
		public DateTime ScanDate { get; set; }
		public long TotalSize { get; set; }
		public int TotalFiles { get; set; }
		public int TotalDirectories { get; set; }
		public List<FileExportInfo> Files { get; set; } = new();
		public List<DuplicateGroupExport> DuplicateGroups { get; set; } = new();
		public List<FileTypeStatsExport> FileTypeStats { get; set; } = new();
		public List<FileExportInfo> LargestFiles { get; set; } = new();
		public List<TemporaryFileCategoryExport> TemporaryFiles { get; set; } = new();
		public List<StaleFileExport> StaleFiles { get; set; } = new();
	}

	public class FileExportInfo
	{
		public string Path { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public long Size { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
		public string Extension { get; set; } = string.Empty;
		public DateTime LastModified { get; set; }
		public bool IsDirectory { get; set; }
	}

	public class DuplicateGroupExport
	{
		public string Hash { get; set; } = string.Empty;
		public int FileCount { get; set; }
		public long FileSize { get; set; }
		public long TotalWastedSpace { get; set; }
		public List<string> FilePaths { get; set; } = new();
	}

	public class FileTypeStatsExport
	{
		public string Extension { get; set; } = string.Empty;
		public int Count { get; set; }
		public long TotalSize { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
	}

	public class TemporaryFileCategoryExport
	{
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public int FileCount { get; set; }
		public long TotalSize { get; set; }
		public bool IsSafeToDelete { get; set; }
	}

	public class StaleFileExport
	{
		public string Path { get; set; } = string.Empty;
		public long Size { get; set; }
		public int DaysSinceLastAccess { get; set; }
		public string Category { get; set; } = string.Empty;
	}

	public class ExportService
	{
		public string ExportToJson(ExportData data)
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			};

			return JsonSerializer.Serialize(data, options);
		}

		public string ExportToCsv(ExportData data, ExportOptions options)
		{
			var csv = new StringBuilder();

			// File list
			if (options.IncludeFileList && data.Files.Any())
			{
				csv.AppendLine("=== FILE LIST ===");
				csv.AppendLine("Path,Name,Size (Bytes),Formatted Size,Extension,Last Modified,Is Directory");

				foreach (var file in data.Files.Take(options.MaxFilesToExport))
				{
					csv.AppendLine($"\"{EscapeCsv(file.Path)}\",\"{EscapeCsv(file.Name)}\",{file.Size},\"{file.FormattedSize}\",\"{file.Extension}\",{file.LastModified:yyyy-MM-dd HH:mm:ss},{file.IsDirectory}");
				}
				csv.AppendLine();
			}

			// Duplicates
			if (options.IncludeDuplicates && data.DuplicateGroups.Any())
			{
				csv.AppendLine("=== DUPLICATE FILES ===");
				csv.AppendLine("Hash,File Count,File Size,Total Wasted Space,File Paths");

				foreach (var group in data.DuplicateGroups)
				{
					csv.AppendLine($"\"{group.Hash}\",{group.FileCount},{group.FileSize},{group.TotalWastedSpace},\"{EscapeCsv(string.Join("; ", group.FilePaths))}\"");
				}
				csv.AppendLine();
			}

			// File types
			if (options.IncludeFileTypes && data.FileTypeStats.Any())
			{
				csv.AppendLine("=== FILE TYPE STATISTICS ===");
				csv.AppendLine("Extension,Count,Total Size (Bytes),Formatted Size");

				foreach (var stat in data.FileTypeStats)
				{
					csv.AppendLine($"\"{stat.Extension}\",{stat.Count},{stat.TotalSize},\"{stat.FormattedSize}\"");
				}
				csv.AppendLine();
			}

			// Largest files
			if (options.IncludeLargestFiles && data.LargestFiles.Any())
			{
				csv.AppendLine("=== LARGEST FILES ===");
				csv.AppendLine("Path,Size (Bytes),Formatted Size,Last Modified");

				foreach (var file in data.LargestFiles)
				{
					csv.AppendLine($"\"{EscapeCsv(file.Path)}\",{file.Size},\"{file.FormattedSize}\",{file.LastModified:yyyy-MM-dd HH:mm:ss}");
				}
				csv.AppendLine();
			}

			// Temporary files
			if (options.IncludeTemporaryFiles && data.TemporaryFiles.Any())
			{
				csv.AppendLine("=== TEMPORARY FILES ===");
				csv.AppendLine("Category,Description,File Count,Total Size (Bytes),Safe to Delete");

				foreach (var category in data.TemporaryFiles)
				{
					csv.AppendLine($"\"{EscapeCsv(category.Name)}\",\"{EscapeCsv(category.Description)}\",{category.FileCount},{category.TotalSize},{category.IsSafeToDelete}");
				}
				csv.AppendLine();
			}

			// Stale files
			if (options.IncludeStaleFiles && data.StaleFiles.Any())
			{
				csv.AppendLine("=== STALE FILES ===");
				csv.AppendLine("Path,Size (Bytes),Days Since Last Access,Category");

				foreach (var file in data.StaleFiles)
				{
					csv.AppendLine($"\"{EscapeCsv(file.Path)}\",{file.Size},{file.DaysSinceLastAccess},\"{file.Category}\"");
				}
				csv.AppendLine();
			}

			return csv.ToString();
		}

		public string ExportToHtml(ExportData data, ExportOptions options)
		{
			var html = new StringBuilder();
			html.AppendLine("<!DOCTYPE html>");
			html.AppendLine("<html>");
			html.AppendLine("<head>");
			html.AppendLine("    <meta charset='UTF-8'>");
			html.AppendLine("    <title>File Size Analyzer Report</title>");
			html.AppendLine("    <style>");
			html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
			html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
			html.AppendLine("        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }");
			html.AppendLine("        h2 { color: #34495e; margin-top: 30px; border-bottom: 2px solid #ecf0f1; padding-bottom: 8px; }");
			html.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
			html.AppendLine("        th { background: #3498db; color: white; padding: 12px; text-align: left; font-weight: bold; }");
			html.AppendLine("        td { padding: 10px; border-bottom: 1px solid #ecf0f1; }");
			html.AppendLine("        tr:hover { background: #f8f9fa; }");
			html.AppendLine("        .summary { background: #ecf0f1; padding: 20px; border-radius: 5px; margin: 20px 0; }");
			html.AppendLine("        .summary-item { margin: 10px 0; font-size: 16px; }");
			html.AppendLine("        .label { font-weight: bold; color: #2c3e50; }");
			html.AppendLine("        .value { color: #3498db; }");
			html.AppendLine("        .warning { background: #fff3cd; border-left: 4px solid #ffc107; padding: 10px; margin: 10px 0; }");
			html.AppendLine("        .safe { color: #27ae60; }");
			html.AppendLine("        .unsafe { color: #e74c3c; }");
			html.AppendLine("    </style>");
			html.AppendLine("</head>");
			html.AppendLine("<body>");
			html.AppendLine("    <div class='container'>");
			html.AppendLine($"        <h1>File Size Analyzer Report</h1>");
			html.AppendLine("        <div class='summary'>");
			html.AppendLine($"            <div class='summary-item'><span class='label'>Scan Path:</span> <span class='value'>{data.ScanPath}</span></div>");
			html.AppendLine($"            <div class='summary-item'><span class='label'>Scan Date:</span> <span class='value'>{data.ScanDate:yyyy-MM-dd HH:mm:ss}</span></div>");
			html.AppendLine($"            <div class='summary-item'><span class='label'>Total Size:</span> <span class='value'>{FormatSize(data.TotalSize)}</span></div>");
			html.AppendLine($"            <div class='summary-item'><span class='label'>Total Files:</span> <span class='value'>{data.TotalFiles:N0}</span></div>");
			html.AppendLine($"            <div class='summary-item'><span class='label'>Total Directories:</span> <span class='value'>{data.TotalDirectories:N0}</span></div>");
			html.AppendLine("        </div>");

			// File types
			if (options.IncludeFileTypes && data.FileTypeStats.Any())
			{
				html.AppendLine("        <h2>File Type Statistics</h2>");
				html.AppendLine("        <table>");
				html.AppendLine("            <tr><th>Extension</th><th>Count</th><th>Total Size</th><th>% of Total</th></tr>");
				foreach (var stat in data.FileTypeStats.Take(20))
				{
					double percentage = data.TotalSize > 0 ? (stat.TotalSize * 100.0 / data.TotalSize) : 0;
					html.AppendLine($"            <tr><td>{stat.Extension}</td><td>{stat.Count:N0}</td><td>{stat.FormattedSize}</td><td>{percentage:F2}%</td></tr>");
				}
				html.AppendLine("        </table>");
			}

			// Duplicates
			if (options.IncludeDuplicates && data.DuplicateGroups.Any())
			{
				html.AppendLine("        <h2>Duplicate Files</h2>");
				html.AppendLine($"        <div class='warning'>Found {data.DuplicateGroups.Count:N0} duplicate groups wasting {FormatSize(data.DuplicateGroups.Sum(g => g.TotalWastedSpace))}</div>");
				html.AppendLine("        <table>");
				html.AppendLine("            <tr><th>Files</th><th>Size Each</th><th>Wasted Space</th><th>Locations</th></tr>");
				foreach (var group in data.DuplicateGroups.Take(50))
				{
					html.AppendLine($"            <tr><td>{group.FileCount}</td><td>{FormatSize(group.FileSize)}</td><td>{FormatSize(group.TotalWastedSpace)}</td><td>{string.Join("<br/>", group.FilePaths.Take(3))}{(group.FilePaths.Count > 3 ? "<br/>..." : "")}</td></tr>");
				}
				html.AppendLine("        </table>");
			}

			// Largest files
			if (options.IncludeLargestFiles && data.LargestFiles.Any())
			{
				html.AppendLine("        <h2>Largest Files</h2>");
				html.AppendLine("        <table>");
				html.AppendLine("            <tr><th>File Path</th><th>Size</th><th>Last Modified</th></tr>");
				foreach (var file in data.LargestFiles.Take(50))
				{
					html.AppendLine($"            <tr><td>{file.Path}</td><td>{file.FormattedSize}</td><td>{file.LastModified:yyyy-MM-dd HH:mm:ss}</td></tr>");
				}
				html.AppendLine("        </table>");
			}

			// Temporary files
			if (options.IncludeTemporaryFiles && data.TemporaryFiles.Any())
			{
				html.AppendLine("        <h2>Temporary Files</h2>");
				html.AppendLine("        <table>");
				html.AppendLine("            <tr><th>Category</th><th>Files</th><th>Total Size</th><th>Safe to Delete</th></tr>");
				foreach (var category in data.TemporaryFiles)
				{
					string safeClass = category.IsSafeToDelete ? "safe" : "unsafe";
					string safeText = category.IsSafeToDelete ? "✓ Yes" : "✗ Review First";
					html.AppendLine($"            <tr><td>{category.Name}<br/><small>{category.Description}</small></td><td>{category.FileCount:N0}</td><td>{FormatSize(category.TotalSize)}</td><td class='{safeClass}'>{safeText}</td></tr>");
				}
				html.AppendLine("        </table>");
			}

			// Stale files
			if (options.IncludeStaleFiles && data.StaleFiles.Any())
			{
				html.AppendLine("        <h2>Stale Files (Not Modified in 1+ Year)</h2>");
				html.AppendLine("        <table>");
				html.AppendLine("            <tr><th>File Path</th><th>Size</th><th>Days Since Last Access</th><th>Category</th></tr>");
				foreach (var file in data.StaleFiles.Take(100))
				{
					html.AppendLine($"            <tr><td>{file.Path}</td><td>{FormatSize(file.Size)}</td><td>{file.DaysSinceLastAccess:N0}</td><td>{file.Category}</td></tr>");
				}
				html.AppendLine("        </table>");
			}

			html.AppendLine("    </div>");
			html.AppendLine("</body>");
			html.AppendLine("</html>");

			return html.ToString();
		}

		private string EscapeCsv(string value)
		{
			if (string.IsNullOrEmpty(value)) return value;
			return value.Replace("\"", "\"\"");
		}

		private static string FormatSize(long bytes)
		{
			if (bytes < 0) return "0 B";
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			int order = 0;
			double size = bytes;
			while (size >= 1024 && order < sizes.Length - 1)
			{
				order++;
				size /= 1024;
			}
			return $"{size:0.##} {sizes[order]}";
		}

		public ExportData PrepareExportData(
			string scanPath,
			FileSystemNode rootNode,
			List<FileSystemNode> allFiles,
			List<DuplicateSet> duplicates,
			List<FileTypeStats> fileTypes,
			List<FileSystemNode> largestFiles,
			List<TemporaryFileCategory> temporaryFiles,
			List<StaleFileInfo> staleFiles,
			ExportOptions options)
		{
			var data = new ExportData
			{
				ScanPath = scanPath,
				ScanDate = DateTime.Now,
				TotalSize = rootNode?.Size ?? 0,
				TotalFiles = allFiles.Count(f => !f.IsDirectory),
				TotalDirectories = allFiles.Count(f => f.IsDirectory)
			};

			// Files
			if (options.IncludeFileList)
			{
				data.Files = allFiles.Take(options.MaxFilesToExport).Select(f => new FileExportInfo
				{
					Path = f.FullPath,
					Name = Path.GetFileName(f.FullPath),
					Size = f.Size,
					FormattedSize = f.FormattedSize,
					Extension = f.Extension ?? "",
					LastModified = f.LastWriteTime,
					IsDirectory = f.IsDirectory
				}).ToList();
			}

			// Duplicates
			if (options.IncludeDuplicates)
			{
				data.DuplicateGroups = duplicates.Select(d => new DuplicateGroupExport
				{
					Hash = d.FileName,  // Using FileName as identifier
					FileCount = d.Files.Count,
					FileSize = d.Files.FirstOrDefault()?.Size ?? 0,
					TotalWastedSpace = (d.Files.Count - 1) * (d.Files.FirstOrDefault()?.Size ?? 0),
					FilePaths = d.Files.Select(f => f.FullPath).ToList()
				}).ToList();
			}

			// File types
			if (options.IncludeFileTypes)
			{
				data.FileTypeStats = fileTypes.Select(ft => new FileTypeStatsExport
				{
					Extension = ft.Extension,
					Count = ft.FileCount,
					TotalSize = ft.TotalSize,
					FormattedSize = FormatSize(ft.TotalSize)
				}).ToList();
			}

			// Largest files
			if (options.IncludeLargestFiles)
			{
				data.LargestFiles = largestFiles.Select(f => new FileExportInfo
				{
					Path = f.FullPath,
					Name = Path.GetFileName(f.FullPath),
					Size = f.Size,
					FormattedSize = f.FormattedSize,
					Extension = f.Extension ?? "",
					LastModified = f.LastWriteTime,
					IsDirectory = f.IsDirectory
				}).ToList();
			}

			// Temporary files
			if (options.IncludeTemporaryFiles)
			{
				data.TemporaryFiles = temporaryFiles.Select(tc => new TemporaryFileCategoryExport
				{
					Name = tc.Name,
					Description = tc.Description,
					FileCount = tc.Files.Count,
					TotalSize = tc.TotalSize,
					IsSafeToDelete = tc.IsSafeToDelete
				}).ToList();
			}

			// Stale files
			if (options.IncludeStaleFiles)
			{
				data.StaleFiles = staleFiles.Select(sf => new StaleFileExport
				{
					Path = sf.File.FullPath,
					Size = sf.File.Size,
					DaysSinceLastAccess = sf.DaysSinceLastAccess,
					Category = sf.Category
				}).ToList();
			}

			return data;
		}
	}
}
