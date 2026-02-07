using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public enum AutoSelectRule
	{
		None,
		KeepNewest,          // Keep file with most recent modification date
		KeepOldest,          // Keep file with oldest modification date
		KeepLargest,         // Keep largest file (useful for versions)
		KeepSmallest,        // Keep smallest file
		KeepShortestPath,    // Keep file with shortest path
		KeepInSpecificFolder // Keep file in a specific folder
	}

	public class DuplicateGroup
	{
		public string Hash { get; set; } = string.Empty;
		public List<FileSystemNode> Files { get; set; } = new();
		public long FileSize { get; set; }
		public long WastedSpace { get; set; }
		public FileSystemNode? KeepFile { get; set; }
		public List<FileSystemNode> DeleteFiles { get; set; } = new();
		public string KeepReason { get; set; } = string.Empty;
	}

	public class DuplicateManagementService
	{
		public List<DuplicateGroup> ApplyAutoSelectRules(
			List<DuplicateSet> duplicateSets,
			AutoSelectRule rule,
			string? preferredFolder = null)
		{
			var groups = new List<DuplicateGroup>();

			foreach (var dupSet in duplicateSets)
			{
				if (dupSet.Files.Count < 2) continue;

				var group = new DuplicateGroup
				{
					Hash = dupSet.FileName,
					Files = dupSet.Files.ToList(),
					FileSize = dupSet.Files.FirstOrDefault()?.Size ?? 0
				};

				group.WastedSpace = (group.Files.Count - 1) * group.FileSize;

				// Apply selection rule
				switch (rule)
				{
					case AutoSelectRule.KeepNewest:
						group.KeepFile = group.Files.OrderByDescending(f => f.LastWriteTime).First();
						group.KeepReason = $"Newest (modified {group.KeepFile.LastWriteTime:yyyy-MM-dd})";
						break;

					case AutoSelectRule.KeepOldest:
						group.KeepFile = group.Files.OrderBy(f => f.LastWriteTime).First();
						group.KeepReason = $"Oldest (modified {group.KeepFile.LastWriteTime:yyyy-MM-dd})";
						break;

					case AutoSelectRule.KeepLargest:
						group.KeepFile = group.Files.OrderByDescending(f => f.Size).First();
						group.KeepReason = $"Largest ({FormatSize(group.KeepFile.Size)})";
						break;

					case AutoSelectRule.KeepSmallest:
						group.KeepFile = group.Files.OrderBy(f => f.Size).First();
						group.KeepReason = $"Smallest ({FormatSize(group.KeepFile.Size)})";
						break;

					case AutoSelectRule.KeepShortestPath:
						group.KeepFile = group.Files.OrderBy(f => f.FullPath.Length).First();
						group.KeepReason = $"Shortest path ({group.KeepFile.FullPath.Length} chars)";
						break;

					case AutoSelectRule.KeepInSpecificFolder:
						if (!string.IsNullOrEmpty(preferredFolder))
						{
							var inFolder = group.Files.FirstOrDefault(f =>
								f.FullPath.StartsWith(preferredFolder, StringComparison.OrdinalIgnoreCase));

							if (inFolder != null)
							{
								group.KeepFile = inFolder;
								group.KeepReason = $"In preferred folder";
							}
							else
							{
								// Fallback to keeping first file
								group.KeepFile = group.Files.First();
								group.KeepReason = "No file in preferred folder (keeping first)";
							}
						}
						else
						{
							group.KeepFile = group.Files.First();
							group.KeepReason = "No preferred folder specified";
						}
						break;

					default:
						group.KeepFile = group.Files.First();
						group.KeepReason = "No rule applied";
						break;
				}

				// Set delete files (all except the one to keep)
				group.DeleteFiles = group.Files.Where(f => f != group.KeepFile).ToList();

				groups.Add(group);
			}

			return groups;
		}

		public string GetRuleDescription(AutoSelectRule rule)
		{
			return rule switch
			{
				AutoSelectRule.KeepNewest => "Keep the most recently modified file in each duplicate group",
				AutoSelectRule.KeepOldest => "Keep the oldest file (by modification date) in each duplicate group",
				AutoSelectRule.KeepLargest => "Keep the largest file in each duplicate group",
				AutoSelectRule.KeepSmallest => "Keep the smallest file in each duplicate group",
				AutoSelectRule.KeepShortestPath => "Keep the file with the shortest full path",
				AutoSelectRule.KeepInSpecificFolder => "Keep files in a specific folder, if available",
				_ => "No automatic selection"
			};
		}

		public DuplicateStatistics CalculateStatistics(List<DuplicateGroup> groups)
		{
			return new DuplicateStatistics
			{
				TotalGroups = groups.Count,
				TotalDuplicates = groups.Sum(g => g.Files.Count),
				TotalWastedSpace = groups.Sum(g => g.WastedSpace),
				FilesToDelete = groups.Sum(g => g.DeleteFiles.Count),
				SpaceToRecover = groups.Sum(g => g.DeleteFiles.Sum(f => f.Size)),
				AverageGroupSize = groups.Any() ? groups.Average(g => g.Files.Count) : 0,
				LargestWaste = groups.Any() ? groups.Max(g => g.WastedSpace) : 0
			};
		}

		public List<DuplicateGroup> FilterBySize(List<DuplicateGroup> groups, long minSize, long maxSize)
		{
			return groups.Where(g => g.FileSize >= minSize && g.FileSize <= maxSize).ToList();
		}

		public List<DuplicateGroup> FilterByExtension(List<DuplicateGroup> groups, string extension)
		{
			return groups.Where(g => g.Files.Any(f =>
				string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase)))
				.ToList();
		}

		public List<DuplicateGroup> FilterByPath(List<DuplicateGroup> groups, string pathContains)
		{
			return groups.Where(g => g.Files.Any(f =>
				f.FullPath.Contains(pathContains, StringComparison.OrdinalIgnoreCase)))
				.ToList();
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

	public class DuplicateStatistics
	{
		public int TotalGroups { get; set; }
		public int TotalDuplicates { get; set; }
		public long TotalWastedSpace { get; set; }
		public int FilesToDelete { get; set; }
		public long SpaceToRecover { get; set; }
		public double AverageGroupSize { get; set; }
		public long LargestWaste { get; set; }

		public string GetSummary()
		{
			var summary = $"Duplicate Groups: {TotalGroups:N0}\n";
			summary += $"Total Duplicate Files: {TotalDuplicates:N0}\n";
			summary += $"Wasted Space: {FormatSize(TotalWastedSpace)}\n";
			summary += $"Files Marked for Deletion: {FilesToDelete:N0}\n";
			summary += $"Space to Recover: {FormatSize(SpaceToRecover)}\n";
			summary += $"Average Duplicates per Group: {AverageGroupSize:F1}\n";
			summary += $"Largest Single Waste: {FormatSize(LargestWaste)}";
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
