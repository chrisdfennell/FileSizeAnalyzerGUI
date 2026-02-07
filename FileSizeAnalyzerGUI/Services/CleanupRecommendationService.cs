using System;
using System.Collections.Generic;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class CleanupRecommendation
	{
		public string Category { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public long PotentialSavings { get; set; }
		public string FormattedSavings { get; set; } = string.Empty;
		public int FileCount { get; set; }
		public string Priority { get; set; } = string.Empty; // "High", "Medium", "Low"
		public string Action { get; set; } = string.Empty;
		public List<string> SampleFiles { get; set; } = new();
	}

	public class CleanupRecommendationService
	{
		private readonly AdvancedAnalysisService _advancedAnalysisService;
		private readonly TemporaryFilesService _tempFilesService;

		public CleanupRecommendationService(
			AdvancedAnalysisService advancedAnalysisService,
			TemporaryFilesService tempFilesService)
		{
			_advancedAnalysisService = advancedAnalysisService;
			_tempFilesService = tempFilesService;
		}

		public List<CleanupRecommendation> GenerateRecommendations(List<FileSystemNode> allFiles)
		{
			var recommendations = new List<CleanupRecommendation>();

			// 1. Duplicate files
			recommendations.Add(AnalyzeDuplicates(allFiles));

			// 2. Temporary files
			recommendations.AddRange(AnalyzeTemporaryFiles(allFiles));

			// 3. Old large files
			recommendations.Add(AnalyzeOldLargeFiles(allFiles));

			// 4. Stale files
			recommendations.Add(AnalyzeStaleFiles(allFiles));

			// 5. Large media files
			recommendations.Add(AnalyzeLargeMedia(allFiles));

			// 6. Empty directories
			recommendations.Add(AnalyzeEmptyDirectories(allFiles));

			// 7. Download folder cleanup
			recommendations.Add(AnalyzeDownloadsFolder(allFiles));

			// 8. Large log files
			recommendations.Add(AnalyzeLargeLogFiles(allFiles));

			// Filter out recommendations with no savings and sort by potential savings
			return recommendations
				.Where(r => r.PotentialSavings > 0)
				.OrderByDescending(r => r.PotentialSavings)
				.ToList();
		}

		private CleanupRecommendation AnalyzeDuplicates(List<FileSystemNode> allFiles)
		{
			// This is a simplified version - in a real implementation, you'd use the duplicate detection service
			var duplicateGroups = allFiles
				.Where(f => !f.IsDirectory)
				.GroupBy(f => f.Size)
				.Where(g => g.Count() > 1 && g.Key > 1024 * 1024) // Files > 1MB with same size
				.ToList();

			var potentialSavings = duplicateGroups.Sum(g => (g.Count() - 1) * g.Key);
			var fileCount = duplicateGroups.Sum(g => g.Count() - 1);

			return new CleanupRecommendation
			{
				Category = "Duplicate Files",
				Description = "Files with identical sizes that may be duplicates",
				PotentialSavings = potentialSavings,
				FormattedSavings = FormatSize(potentialSavings),
				FileCount = fileCount,
				Priority = potentialSavings > 1024 * 1024 * 1024 ? "High" : potentialSavings > 100 * 1024 * 1024 ? "Medium" : "Low",
				Action = "Review and delete duplicate copies, keeping one master copy",
				SampleFiles = duplicateGroups
					.SelectMany(g => g.Take(1))
					.Select(f => f.FullPath)
					.Take(5)
					.ToList()
			};
		}

		private List<CleanupRecommendation> AnalyzeTemporaryFiles(List<FileSystemNode> allFiles)
		{
			var tempCategories = _tempFilesService.AnalyzeTemporaryFiles(allFiles);
			var recommendations = new List<CleanupRecommendation>();

			foreach (var category in tempCategories.Where(c => c.TotalSize > 0))
			{
				recommendations.Add(new CleanupRecommendation
				{
					Category = $"Temporary: {category.Name}",
					Description = category.Description,
					PotentialSavings = category.TotalSize,
					FormattedSavings = FormatSize(category.TotalSize),
					FileCount = category.Files.Count,
					Priority = category.IsSafeToDelete ? "High" : "Medium",
					Action = category.IsSafeToDelete
						? "Safe to delete - these are temporary files"
						: "Review before deleting - may still be in use",
					SampleFiles = category.Files.Take(5).Select(f => f.FullPath).ToList()
				});
			}

			return recommendations;
		}

		private CleanupRecommendation AnalyzeOldLargeFiles(List<FileSystemNode> allFiles)
		{
			var oldLargeFiles = _advancedAnalysisService.FindLargeRarelyUsedFiles(
				allFiles,
				minSizeBytes: 100 * 1024 * 1024, // 100MB
				daysThreshold: 365); // 1 year

			var totalSize = oldLargeFiles.Sum(f => f.File.Size);

			return new CleanupRecommendation
			{
				Category = "Old Large Files",
				Description = "Large files (100MB+) not accessed in over a year",
				PotentialSavings = totalSize,
				FormattedSavings = FormatSize(totalSize),
				FileCount = oldLargeFiles.Count,
				Priority = totalSize > 10L * 1024 * 1024 * 1024 ? "High" : "Medium", // 10GB
				Action = "Consider archiving to external storage or cloud backup",
				SampleFiles = oldLargeFiles.Take(5).Select(f => f.File.FullPath).ToList()
			};
		}

		private CleanupRecommendation AnalyzeStaleFiles(List<FileSystemNode> allFiles)
		{
			var staleFiles = _advancedAnalysisService.FindStaleFiles(allFiles, daysThreshold: 730); // 2 years
			var totalSize = staleFiles.Sum(f => f.File.Size);

			return new CleanupRecommendation
			{
				Category = "Very Old Files",
				Description = "Files not modified in over 2 years",
				PotentialSavings = totalSize,
				FormattedSavings = FormatSize(totalSize),
				FileCount = staleFiles.Count,
				Priority = "Low",
				Action = "Review and archive or delete if no longer needed",
				SampleFiles = staleFiles.Take(5).Select(f => f.File.FullPath).ToList()
			};
		}

		private CleanupRecommendation AnalyzeLargeMedia(List<FileSystemNode> allFiles)
		{
			var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				".mp4", ".avi", ".mkv", ".mov", ".wmv", ".m4v", ".mpg", ".mpeg"
			};

			var largeMedia = allFiles
				.Where(f => !f.IsDirectory &&
					   f.Size >= 500 * 1024 * 1024 && // 500MB+
					   mediaExtensions.Contains(f.Extension ?? ""))
				.ToList();

			var totalSize = largeMedia.Sum(f => f.Size);

			return new CleanupRecommendation
			{
				Category = "Large Video Files",
				Description = "Video files larger than 500MB",
				PotentialSavings = totalSize,
				FormattedSavings = FormatSize(totalSize),
				FileCount = largeMedia.Count,
				Priority = totalSize > 20L * 1024 * 1024 * 1024 ? "High" : "Medium", // 20GB
				Action = "Consider compressing videos or moving to external storage",
				SampleFiles = largeMedia.Take(5).Select(f => f.FullPath).ToList()
			};
		}

		private CleanupRecommendation AnalyzeEmptyDirectories(List<FileSystemNode> allFiles)
		{
			var emptyDirs = allFiles
				.Where(f => f.IsDirectory && f.Size == 0)
				.ToList();

			return new CleanupRecommendation
			{
				Category = "Empty Folders",
				Description = "Empty directories that can be removed",
				PotentialSavings = 0, // No disk space savings, but cleanup benefit
				FormattedSavings = "0 B (organizational cleanup)",
				FileCount = emptyDirs.Count,
				Priority = "Low",
				Action = "Delete empty folders to clean up directory structure",
				SampleFiles = emptyDirs.Take(5).Select(f => f.FullPath).ToList()
			};
		}

		private CleanupRecommendation AnalyzeDownloadsFolder(List<FileSystemNode> allFiles)
		{
			var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
			var now = DateTime.Now;

			var oldDownloads = allFiles
				.Where(f => !f.IsDirectory &&
					   f.FullPath.StartsWith(downloadsPath, StringComparison.OrdinalIgnoreCase) &&
					   (now - f.LastWriteTime).Days > 90)
				.ToList();

			var totalSize = oldDownloads.Sum(f => f.Size);

			return new CleanupRecommendation
			{
				Category = "Old Downloads",
				Description = "Files in Downloads folder older than 90 days",
				PotentialSavings = totalSize,
				FormattedSavings = FormatSize(totalSize),
				FileCount = oldDownloads.Count,
				Priority = totalSize > 1024 * 1024 * 1024 ? "High" : "Medium", // 1GB
				Action = "Review downloads folder and delete or move files to appropriate locations",
				SampleFiles = oldDownloads.Take(5).Select(f => f.FullPath).ToList()
			};
		}

		private CleanupRecommendation AnalyzeLargeLogFiles(List<FileSystemNode> allFiles)
		{
			var now = DateTime.Now;
			var largeLogs = allFiles
				.Where(f => !f.IsDirectory &&
					   (f.Extension?.Equals(".log", StringComparison.OrdinalIgnoreCase) == true ||
					    f.Extension?.Equals(".etl", StringComparison.OrdinalIgnoreCase) == true) &&
					   f.Size >= 10 * 1024 * 1024 && // 10MB+
					   (now - f.LastWriteTime).Days > 30)
				.ToList();

			var totalSize = largeLogs.Sum(f => f.Size);

			return new CleanupRecommendation
			{
				Category = "Large Log Files",
				Description = "Log files larger than 10MB and older than 30 days",
				PotentialSavings = totalSize,
				FormattedSavings = FormatSize(totalSize),
				FileCount = largeLogs.Count,
				Priority = totalSize > 5L * 1024 * 1024 * 1024 ? "High" : "Medium", // 5GB
				Action = "Archive old log files or delete if no longer needed for troubleshooting",
				SampleFiles = largeLogs.Take(5).Select(f => f.FullPath).ToList()
			};
		}

		public string GetOverallSummary(List<CleanupRecommendation> recommendations)
		{
			if (!recommendations.Any())
			{
				return "✓ Your system looks clean! No major cleanup opportunities found.";
			}

			var totalSavings = recommendations.Sum(r => r.PotentialSavings);
			var highPriority = recommendations.Count(r => r.Priority == "High");
			var mediumPriority = recommendations.Count(r => r.Priority == "Medium");

			var summary = $"Found {recommendations.Count} cleanup opportunities:\n";
			summary += $"• Total potential savings: {FormatSize(totalSavings)}\n";

			if (highPriority > 0)
			{
				summary += $"• {highPriority} high priority item(s)\n";
			}
			if (mediumPriority > 0)
			{
				summary += $"• {mediumPriority} medium priority item(s)\n";
			}

			summary += "\nTop recommendation: ";
			var top = recommendations.First();
			summary += $"{top.Category} - {top.FormattedSavings} ({top.FileCount:N0} files)";

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
