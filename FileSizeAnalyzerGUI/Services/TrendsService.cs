using System;
using System.Collections.Generic;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class TrendDataPoint
	{
		public DateTime Date { get; set; }
		public long TotalSize { get; set; }
		public int FileCount { get; set; }
		public int DirectoryCount { get; set; }
		public string FormattedSize { get; set; } = string.Empty;
		public long SizeChange { get; set; }
		public string ChangeDirection { get; set; } = string.Empty; // "↑", "↓", "→"
	}

	public class TrendAnalysis
	{
		public List<TrendDataPoint> DataPoints { get; set; } = new();
		public long TotalGrowth { get; set; }
		public double AverageDailyGrowth { get; set; }
		public double GrowthRate { get; set; } // Percentage
		public DateTime? ProjectedFullDate { get; set; }
		public string Summary { get; set; } = string.Empty;
		public string Recommendation { get; set; } = string.Empty;
	}

	public class SpaceBreakdown
	{
		public Dictionary<string, long> ByExtension { get; set; } = new();
		public Dictionary<string, long> ByAgeCategory { get; set; } = new();
		public Dictionary<string, long> ByCategory { get; set; } = new();
	}

	public class TrendsService
	{
		private readonly ScanMetadataService _metadataService;

		public TrendsService(ScanMetadataService metadataService)
		{
			_metadataService = metadataService;
		}

		public TrendAnalysis AnalyzeTrends(string scanPath)
		{
			var scans = _metadataService.GetScanHistory(scanPath);
			if (scans == null || scans.Count < 2)
			{
				return new TrendAnalysis
				{
					Summary = "Not enough scan history to analyze trends. Perform multiple scans over time to see trends.",
					Recommendation = "Run scans regularly to track disk space usage over time."
				};
			}

			var orderedScans = scans.OrderBy(s => s.ScanDate).ToList();
			var dataPoints = new List<TrendDataPoint>();

			for (int i = 0; i < orderedScans.Count; i++)
			{
				var scan = orderedScans[i];
				long sizeChange = 0;
				string changeDirection = "→";

				if (i > 0)
				{
					sizeChange = scan.TotalSize - orderedScans[i - 1].TotalSize;
					changeDirection = sizeChange > 0 ? "↑" : sizeChange < 0 ? "↓" : "→";
				}

				dataPoints.Add(new TrendDataPoint
				{
					Date = scan.ScanDate,
					TotalSize = scan.TotalSize,
					FileCount = scan.FileCount,
					DirectoryCount = scan.DirectoryCount,
					FormattedSize = FormatSize(scan.TotalSize),
					SizeChange = sizeChange,
					ChangeDirection = changeDirection
				});
			}

			// Calculate analytics
			var firstScan = orderedScans.First();
			var lastScan = orderedScans.Last();
			var totalGrowth = lastScan.TotalSize - firstScan.TotalSize;
			var totalDays = (lastScan.ScanDate - firstScan.ScanDate).TotalDays;
			var averageDailyGrowth = totalDays > 0 ? totalGrowth / totalDays : 0;
			var growthRate = firstScan.TotalSize > 0 ? (totalGrowth * 100.0 / firstScan.TotalSize) : 0;

			var analysis = new TrendAnalysis
			{
				DataPoints = dataPoints,
				TotalGrowth = totalGrowth,
				AverageDailyGrowth = averageDailyGrowth,
				GrowthRate = growthRate
			};

			// Generate summary
			var summary = new List<string>();
			summary.Add($"Scans analyzed: {orderedScans.Count} over {totalDays:F0} days");
			summary.Add($"Total growth: {FormatSize(Math.Abs(totalGrowth))} ({(totalGrowth >= 0 ? "+" : "-")}{Math.Abs(growthRate):F2}%)");

			if (averageDailyGrowth > 0)
			{
				summary.Add($"Average daily growth: {FormatSize((long)averageDailyGrowth)}/day");

				// Project when disk might be full (assuming 1TB total capacity for estimation)
				const long estimatedCapacity = 1024L * 1024 * 1024 * 1024; // 1TB
				var remainingSpace = estimatedCapacity - lastScan.TotalSize;
				if (remainingSpace > 0 && averageDailyGrowth > 0)
				{
					var daysUntilFull = remainingSpace / averageDailyGrowth;
					if (daysUntilFull < 365)
					{
						analysis.ProjectedFullDate = DateTime.Now.AddDays(daysUntilFull);
						summary.Add($"⚠️ At current rate, disk could be full in ~{daysUntilFull:F0} days");
					}
				}
			}
			else if (averageDailyGrowth < 0)
			{
				summary.Add($"Average daily reduction: {FormatSize((long)Math.Abs(averageDailyGrowth))}/day");
			}

			analysis.Summary = string.Join("\n", summary);

			// Generate recommendation
			if (growthRate > 50)
			{
				analysis.Recommendation = "⚠️ High growth rate detected. Consider:\n" +
					"- Running duplicate detection\n" +
					"- Cleaning up temporary files\n" +
					"- Archiving old files\n" +
					"- Reviewing large rarely-used files";
			}
			else if (growthRate > 20)
			{
				analysis.Recommendation = "Moderate growth detected. Regular cleanup recommended.";
			}
			else if (growthRate < -10)
			{
				analysis.Recommendation = "✓ Space usage decreasing. Keep up the good cleanup practices!";
			}
			else
			{
				analysis.Recommendation = "Space usage is stable. Continue monitoring.";
			}

			return analysis;
		}

		public SpaceBreakdown GetSpaceBreakdown(List<FileSystemNode> allFiles)
		{
			var breakdown = new SpaceBreakdown();

			// Group by extension
			var byExtension = allFiles
				.Where(f => !f.IsDirectory)
				.GroupBy(f => string.IsNullOrEmpty(f.Extension) ? "(no extension)" : f.Extension)
				.Select(g => new { Extension = g.Key, Size = g.Sum(f => f.Size) })
				.OrderByDescending(x => x.Size)
				.Take(20);

			foreach (var item in byExtension)
			{
				breakdown.ByExtension[item.Extension] = item.Size;
			}

			// Group by age
			var now = DateTime.Now;
			var ageCategories = new Dictionary<string, Func<FileSystemNode, bool>>
			{
				["Today"] = f => (now - f.LastWriteTime).Days == 0,
				["This Week"] = f => (now - f.LastWriteTime).Days <= 7,
				["This Month"] = f => (now - f.LastWriteTime).Days <= 30,
				["Last 3 Months"] = f => (now - f.LastWriteTime).Days <= 90,
				["Last 6 Months"] = f => (now - f.LastWriteTime).Days <= 180,
				["Last Year"] = f => (now - f.LastWriteTime).Days <= 365,
				["1-2 Years"] = f => (now - f.LastWriteTime).Days > 365 && (now - f.LastWriteTime).Days <= 730,
				["2+ Years"] = f => (now - f.LastWriteTime).Days > 730
			};

			foreach (var category in ageCategories)
			{
				var size = allFiles.Where(f => !f.IsDirectory && category.Value(f)).Sum(f => f.Size);
				if (size > 0)
				{
					breakdown.ByAgeCategory[category.Key] = size;
				}
			}

			// Categorize by type
			var categories = new Dictionary<string, HashSet<string>>
			{
				["Videos"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg" },
				["Images"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg", ".ico", ".heic" },
				["Documents"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf" },
				["Archives"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso" },
				["Code"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".cpp", ".c", ".h", ".java", ".py", ".js", ".ts", ".html", ".css", ".sql" },
				["Audio"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" },
				["Executables"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".sys", ".msi" }
			};

			foreach (var category in categories)
			{
				var size = allFiles
					.Where(f => !f.IsDirectory && category.Value.Contains(f.Extension ?? ""))
					.Sum(f => f.Size);

				if (size > 0)
				{
					breakdown.ByCategory[category.Key] = size;
				}
			}

			// Calculate "Other"
			var categorizedSize = breakdown.ByCategory.Values.Sum();
			var totalSize = allFiles.Where(f => !f.IsDirectory).Sum(f => f.Size);
			var otherSize = totalSize - categorizedSize;
			if (otherSize > 0)
			{
				breakdown.ByCategory["Other"] = otherSize;
			}

			return breakdown;
		}

		public List<string> GetGrowthPredictions(TrendAnalysis analysis)
		{
			var predictions = new List<string>();

			if (analysis.DataPoints.Count < 2)
			{
				predictions.Add("Need more data for predictions");
				return predictions;
			}

			// 30-day prediction
			var predicted30Day = analysis.DataPoints.Last().TotalSize + (long)(analysis.AverageDailyGrowth * 30);
			predictions.Add($"In 30 days: ~{FormatSize(predicted30Day)} ({FormatSize((long)(analysis.AverageDailyGrowth * 30))} change)");

			// 90-day prediction
			var predicted90Day = analysis.DataPoints.Last().TotalSize + (long)(analysis.AverageDailyGrowth * 90);
			predictions.Add($"In 90 days: ~{FormatSize(predicted90Day)} ({FormatSize((long)(analysis.AverageDailyGrowth * 90))} change)");

			// 1-year prediction
			var predicted1Year = analysis.DataPoints.Last().TotalSize + (long)(analysis.AverageDailyGrowth * 365);
			predictions.Add($"In 1 year: ~{FormatSize(predicted1Year)} ({FormatSize((long)(analysis.AverageDailyGrowth * 365))} change)");

			return predictions;
		}

		private static string FormatSize(long bytes)
		{
			if (bytes < 0) return "-" + FormatSize(-bytes);
			if (bytes == 0) return "0 B";
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
