using System;
using System.Collections.Generic;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class DashboardData
	{
		public long TotalSize { get; set; }
		public long UsedSpace { get; set; }
		public long FreeSpace { get; set; }
		public double UsagePercentage { get; set; }
		public int TotalFiles { get; set; }
		public int TotalDirectories { get; set; }
		public List<FileTypeDistribution> FileTypeData { get; set; } = new();
		public List<FolderSizeData> LargestFolders { get; set; } = new();
		public List<TrendPoint> SizeHistory { get; set; } = new();
		public string FormattedTotalSize { get; set; } = string.Empty;
		public string FormattedUsedSpace { get; set; } = string.Empty;
		public string FormattedFreeSpace { get; set; } = string.Empty;
	}

	public class FileTypeDistribution
	{
		public string Extension { get; set; } = string.Empty;
		public long Size { get; set; }
		public int FileCount { get; set; }
		public double Percentage { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
		public string Label { get; set; } = string.Empty;
	}

	public class FolderSizeData
	{
		public string Path { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public long Size { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
	}

	public class TrendPoint
	{
		public DateTime Date { get; set; }
		public long Size { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
	}

	public class DashboardService
	{
		private readonly ScanMetadataService _metadataService;

		public DashboardService(ScanMetadataService metadataService)
		{
			_metadataService = metadataService;
		}

		public DashboardData GenerateDashboard(List<FileSystemNode> allFiles, FileSystemNode? rootNode)
		{
			var dashboard = new DashboardData();

			if (rootNode == null || !allFiles.Any())
			{
				return dashboard;
			}

			// Basic stats
			dashboard.TotalSize = rootNode.Size;
			dashboard.FormattedTotalSize = FormatSize(dashboard.TotalSize);
			dashboard.TotalFiles = allFiles.Count(f => !f.IsDirectory);
			dashboard.TotalDirectories = allFiles.Count(f => f.IsDirectory);

			// Drive space info
			try
			{
				var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(rootNode.FullPath) ?? "C:\\");
				dashboard.FreeSpace = drive.AvailableFreeSpace;
				dashboard.UsedSpace = drive.TotalSize - drive.AvailableFreeSpace;
				dashboard.UsagePercentage = (double)dashboard.UsedSpace / drive.TotalSize * 100;
				dashboard.FormattedUsedSpace = FormatSize(dashboard.UsedSpace);
				dashboard.FormattedFreeSpace = FormatSize(dashboard.FreeSpace);
			}
			catch
			{
				// If we can't get drive info, use scanned data
				dashboard.UsedSpace = dashboard.TotalSize;
				dashboard.FormattedUsedSpace = dashboard.FormattedTotalSize;
			}

			// File type distribution (top 10)
			var fileTypeGroups = allFiles
				.Where(f => !f.IsDirectory)
				.GroupBy(f => string.IsNullOrEmpty(f.Extension) ? "(no extension)" : f.Extension)
				.Select(g => new
				{
					Extension = g.Key,
					Size = g.Sum(f => f.Size),
					Count = g.Count()
				})
				.OrderByDescending(x => x.Size)
				.Take(10)
				.ToList();

			var totalFileSize = fileTypeGroups.Sum(x => x.Size);
			foreach (var group in fileTypeGroups)
			{
				var percentage = totalFileSize > 0 ? (double)group.Size / totalFileSize * 100 : 0;
				dashboard.FileTypeData.Add(new FileTypeDistribution
				{
					Extension = group.Extension,
					Size = group.Size,
					FileCount = group.Count,
					Percentage = percentage,
					FormattedSize = FormatSize(group.Size),
					Label = $"{group.Extension} ({percentage:F1}%)"
				});
			}

			// Largest folders (top 10)
			var folders = allFiles
				.Where(f => f.IsDirectory && f.Size > 0)
				.OrderByDescending(f => f.Size)
				.Take(10);

			foreach (var folder in folders)
			{
				dashboard.LargestFolders.Add(new FolderSizeData
				{
					Path = folder.FullPath,
					Name = System.IO.Path.GetFileName(folder.FullPath),
					Size = folder.Size,
					FormattedSize = FormatSize(folder.Size)
				});
			}

			// Size history from scan metadata
			try
			{
				var history = _metadataService.GetScanHistory(rootNode.FullPath);
				dashboard.SizeHistory = history
					.OrderBy(s => s.ScanDate)
					.Select(s => new TrendPoint
					{
						Date = s.ScanDate,
						Size = s.TotalSize,
						FormattedSize = FormatSize(s.TotalSize)
					})
					.ToList();
			}
			catch
			{
				// If no history available, just use current scan
				dashboard.SizeHistory.Add(new TrendPoint
				{
					Date = DateTime.Now,
					Size = dashboard.TotalSize,
					FormattedSize = dashboard.FormattedTotalSize
				});
			}

			return dashboard;
		}

		public string GetDashboardSummary(DashboardData dashboard)
		{
			var summary = $"Total Scanned: {dashboard.FormattedTotalSize}\n";
			summary += $"Files: {dashboard.TotalFiles:N0} | Directories: {dashboard.TotalDirectories:N0}\n";

			if (dashboard.UsagePercentage > 0)
			{
				summary += $"\nDisk Usage: {dashboard.UsagePercentage:F1}%\n";
				summary += $"Used: {dashboard.FormattedUsedSpace} | Free: {dashboard.FormattedFreeSpace}\n";
			}

			if (dashboard.FileTypeData.Any())
			{
				summary += $"\nTop File Type: {dashboard.FileTypeData.First().Extension} ";
				summary += $"({dashboard.FileTypeData.First().FormattedSize})\n";
			}

			if (dashboard.LargestFolders.Any())
			{
				summary += $"Largest Folder: {dashboard.LargestFolders.First().Name} ";
				summary += $"({dashboard.LargestFolders.First().FormattedSize})\n";
			}

			return summary;
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
	}
}
