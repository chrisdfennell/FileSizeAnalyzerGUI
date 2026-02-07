using System;
using System.Collections.Generic;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class StaleFileInfo
	{
		public FileSystemNode File { get; set; } = null!;
		public int DaysSinceLastAccess { get; set; }
		public int DaysSinceLastModified { get; set; }
		public string Category { get; set; } = string.Empty;
	}

	public class LargeRarelyUsedFile
	{
		public FileSystemNode File { get; set; } = null!;
		public int DaysSinceLastAccess { get; set; }
		public long PotentialSavings { get; set; }
		public string Recommendation { get; set; } = string.Empty;
	}

	public class AdvancedAnalysisService
	{
		public List<StaleFileInfo> FindStaleFiles(List<FileSystemNode> allFiles, int daysThreshold = 365)
		{
			var staleFiles = new List<StaleFileInfo>();
			var now = DateTime.Now;

			foreach (var file in allFiles.Where(f => !f.IsDirectory))
			{
				try
				{
					var daysSinceAccess = (now - file.LastWriteTime).Days;
					var daysSinceModified = (now - file.LastWriteTime).Days;

					if (daysSinceAccess >= daysThreshold || daysSinceModified >= daysThreshold)
					{
						var category = daysSinceAccess switch
						{
							>= 1095 => "3+ years",
							>= 730 => "2-3 years",
							>= 365 => "1-2 years",
							>= 180 => "6-12 months",
							_ => "< 6 months"
						};

						staleFiles.Add(new StaleFileInfo
						{
							File = file,
							DaysSinceLastAccess = daysSinceAccess,
							DaysSinceLastModified = daysSinceModified,
							Category = category
						});
					}
				}
				catch
				{
					// Skip files we can't access
				}
			}

			return staleFiles.OrderByDescending(f => f.DaysSinceLastAccess).ToList();
		}

		public List<LargeRarelyUsedFile> FindLargeRarelyUsedFiles(
			List<FileSystemNode> allFiles,
			long minSizeBytes = 100 * 1024 * 1024, // 100 MB default
			int daysThreshold = 180) // 6 months default
		{
			var largeRarelyUsed = new List<LargeRarelyUsedFile>();
			var now = DateTime.Now;

			foreach (var file in allFiles.Where(f => !f.IsDirectory && f.Size >= minSizeBytes))
			{
				try
				{
					var daysSinceAccess = (now - file.LastWriteTime).Days;

					if (daysSinceAccess >= daysThreshold)
					{
						var recommendation = file.Size switch
						{
							>= 1024 * 1024 * 1024 => "Consider archiving to external storage",
							>= 500 * 1024 * 1024 => "Good candidate for compression or archival",
							>= 100 * 1024 * 1024 => "Review if still needed",
							_ => "Monitor usage"
						};

						largeRarelyUsed.Add(new LargeRarelyUsedFile
						{
							File = file,
							DaysSinceLastAccess = daysSinceAccess,
							PotentialSavings = file.Size,
							Recommendation = recommendation
						});
					}
				}
				catch
				{
					// Skip files we can't access
				}
			}

			return largeRarelyUsed.OrderByDescending(f => f.File.Size).ToList();
		}

		public Dictionary<string, long> GetSpaceSavingsOpportunities(List<FileSystemNode> allFiles)
		{
			var opportunities = new Dictionary<string, long>();
			var now = DateTime.Now;

			// Old files (1+ years)
			opportunities["Old Files (1+ years)"] = allFiles
				.Where(f => !f.IsDirectory && (now - f.LastWriteTime).Days >= 365)
				.Sum(f => f.Size);

			// Large files (100MB+)
			opportunities["Large Files (100MB+)"] = allFiles
				.Where(f => !f.IsDirectory && f.Size >= 100 * 1024 * 1024)
				.Sum(f => f.Size);

			// Very large files (1GB+)
			opportunities["Very Large Files (1GB+)"] = allFiles
				.Where(f => !f.IsDirectory && f.Size >= 1024 * 1024 * 1024)
				.Sum(f => f.Size);

			// Old and large (best candidates)
			opportunities["Old & Large (1yr, 100MB+)"] = allFiles
				.Where(f => !f.IsDirectory &&
					   f.Size >= 100 * 1024 * 1024 &&
					   (now - f.LastWriteTime).Days >= 365)
				.Sum(f => f.Size);

			return opportunities;
		}

		public List<FileSystemNode> ApplyQuickFilter(
			List<FileSystemNode> allFiles,
			string filterType,
			object? parameter = null)
		{
			var now = DateTime.Now;

			return filterType switch
			{
				"ModifiedToday" => allFiles.Where(f => !f.IsDirectory && (now - f.LastWriteTime).Days == 0).ToList(),
				"ModifiedThisWeek" => allFiles.Where(f => !f.IsDirectory && (now - f.LastWriteTime).Days <= 7).ToList(),
				"ModifiedThisMonth" => allFiles.Where(f => !f.IsDirectory && (now - f.LastWriteTime).Days <= 30).ToList(),
				"LargerThan100MB" => allFiles.Where(f => !f.IsDirectory && f.Size >= 100 * 1024 * 1024).ToList(),
				"LargerThan1GB" => allFiles.Where(f => !f.IsDirectory && f.Size >= 1024 * 1024 * 1024).ToList(),
				"Videos" => allFiles.Where(f => !f.IsDirectory && IsVideoFile(f.Extension)).ToList(),
				"Images" => allFiles.Where(f => !f.IsDirectory && IsImageFile(f.Extension)).ToList(),
				"Documents" => allFiles.Where(f => !f.IsDirectory && IsDocumentFile(f.Extension)).ToList(),
				"Archives" => allFiles.Where(f => !f.IsDirectory && IsArchiveFile(f.Extension)).ToList(),
				"Executables" => allFiles.Where(f => !f.IsDirectory && IsExecutableFile(f.Extension)).ToList(),
				_ => allFiles
			};
		}

		private bool IsVideoFile(string? extension)
		{
			if (string.IsNullOrEmpty(extension)) return false;
			string[] videoExts = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg" };
			return videoExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}

		private bool IsImageFile(string? extension)
		{
			if (string.IsNullOrEmpty(extension)) return false;
			string[] imageExts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg", ".ico", ".heic" };
			return imageExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}

		private bool IsDocumentFile(string? extension)
		{
			if (string.IsNullOrEmpty(extension)) return false;
			string[] docExts = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods" };
			return docExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}

		private bool IsArchiveFile(string? extension)
		{
			if (string.IsNullOrEmpty(extension)) return false;
			string[] archiveExts = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab" };
			return archiveExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}

		private bool IsExecutableFile(string? extension)
		{
			if (string.IsNullOrEmpty(extension)) return false;
			string[] exeExts = { ".exe", ".dll", ".sys", ".msi", ".bat", ".cmd", ".ps1", ".vbs" };
			return exeExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}
	}
}
