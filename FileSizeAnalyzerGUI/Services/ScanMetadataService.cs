using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FileSizeAnalyzerGUI.Services
{
	public class FileMetadata
	{
		public string FullPath { get; set; } = string.Empty;
		public long Size { get; set; }
		public DateTime LastWriteTime { get; set; }
		public bool IsDirectory { get; set; }
		public string Hash { get; set; } = string.Empty; // For future use

		public bool HasChanged(FileInfo fileInfo)
		{
			return fileInfo.Length != Size || fileInfo.LastWriteTime != LastWriteTime;
		}

		public bool HasChanged(DirectoryInfo dirInfo)
		{
			return dirInfo.LastWriteTime != LastWriteTime;
		}
	}

	public class ScanMetadata
	{
		public string RootPath { get; set; } = string.Empty;
		public DateTime ScanDate { get; set; }
		public long TotalSize { get; set; }
		public int FileCount { get; set; }
		public int DirectoryCount { get; set; }
		public Dictionary<string, FileMetadata> Files { get; set; } = new Dictionary<string, FileMetadata>(StringComparer.OrdinalIgnoreCase);
	}

	public class ScanMetadataService
	{
		private readonly string _metadataPath;
		private readonly Dictionary<string, ScanMetadata> _scanCache = new(StringComparer.OrdinalIgnoreCase);

		public ScanMetadataService()
		{
			var appDataPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"FileSizeAnalyzer");
			Directory.CreateDirectory(appDataPath);
			_metadataPath = Path.Combine(appDataPath, "scan_metadata.json");
			LoadMetadata();
		}

		private void LoadMetadata()
		{
			try
			{
				if (File.Exists(_metadataPath))
				{
					var json = File.ReadAllText(_metadataPath);
					var metadata = JsonSerializer.Deserialize<List<ScanMetadata>>(json);
					if (metadata != null)
					{
						foreach (var scan in metadata)
						{
							_scanCache[scan.RootPath] = scan;
						}
					}
				}
			}
			catch
			{
				// If loading fails, start fresh
			}
		}

		public void SaveMetadata()
		{
			try
			{
				var metadata = _scanCache.Values.OrderByDescending(m => m.ScanDate).Take(20).ToList(); // Keep last 20 scans
				var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(_metadataPath, json);
			}
			catch
			{
				// Ignore save errors
			}
		}

		public ScanMetadata? GetPreviousScan(string rootPath)
		{
			return _scanCache.TryGetValue(rootPath, out var metadata) ? metadata : null;
		}

		public void SaveScan(string rootPath, List<FileSystemNode> allFiles, FileSystemNode rootNode)
		{
			var metadata = new ScanMetadata
			{
				RootPath = rootPath,
				ScanDate = DateTime.Now,
				TotalSize = rootNode.Size,
				FileCount = allFiles.Count(f => !f.IsDirectory),
				DirectoryCount = allFiles.Count(f => f.IsDirectory)
			};

			foreach (var file in allFiles)
			{
				metadata.Files[file.FullPath] = new FileMetadata
				{
					FullPath = file.FullPath,
					Size = file.Size,
					LastWriteTime = file.LastWriteTime,
					IsDirectory = file.IsDirectory
				};
			}

			_scanCache[rootPath] = metadata;
			SaveMetadata();
		}

		public List<ScanMetadata> GetScanHistory(string rootPath)
		{
			// Return all scans for this path from the metadata file
			// In a simple implementation, we only store one scan per path
			// But we can return it in a list for future expansion
			var scan = GetPreviousScan(rootPath);
			return scan != null ? new List<ScanMetadata> { scan } : new List<ScanMetadata>();
		}

		public ScanComparison CompareScan(ScanMetadata? previous, List<FileSystemNode> currentFiles)
		{
			var comparison = new ScanComparison();

			if (previous == null)
			{
				comparison.IsFirstScan = true;
				return comparison;
			}

			var currentPaths = new HashSet<string>(currentFiles.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
			var previousPaths = new HashSet<string>(previous.Files.Keys, StringComparer.OrdinalIgnoreCase);

			// Find new files
			comparison.AddedFiles = currentFiles
				.Where(f => !previousPaths.Contains(f.FullPath))
				.ToList();

			// Find deleted files
			comparison.DeletedPaths = previousPaths
				.Where(p => !currentPaths.Contains(p))
				.ToList();

			// Find modified files
			comparison.ModifiedFiles = currentFiles
				.Where(f => previous.Files.TryGetValue(f.FullPath, out var old) &&
							(old.Size != f.Size || old.LastWriteTime != f.LastWriteTime))
				.ToList();

			// Calculate space changes
			comparison.SpaceFreed = comparison.DeletedPaths
				.Sum(p => previous.Files.TryGetValue(p, out var meta) ? meta.Size : 0);

			comparison.SpaceAdded = comparison.AddedFiles.Sum(f => f.Size);

			comparison.NetSpaceChange = comparison.SpaceAdded - comparison.SpaceFreed;

			return comparison;
		}

		public List<string> GetRecentScanPaths()
		{
			return _scanCache.Values
				.OrderByDescending(m => m.ScanDate)
				.Select(m => m.RootPath)
				.ToList();
		}
	}

	public class ScanComparison
	{
		public bool IsFirstScan { get; set; }
		public List<FileSystemNode> AddedFiles { get; set; } = new();
		public List<FileSystemNode> ModifiedFiles { get; set; } = new();
		public List<string> DeletedPaths { get; set; } = new();
		public long SpaceFreed { get; set; }
		public long SpaceAdded { get; set; }
		public long NetSpaceChange { get; set; }

		public string GetSummary()
		{
			if (IsFirstScan)
				return "First scan - no comparison available";

			var parts = new List<string>();

			if (AddedFiles.Count > 0)
				parts.Add($"+{AddedFiles.Count} files");

			if (DeletedPaths.Count > 0)
				parts.Add($"-{DeletedPaths.Count} files");

			if (ModifiedFiles.Count > 0)
				parts.Add($"~{ModifiedFiles.Count} modified");

			var spaceChange = NetSpaceChange >= 0
				? $"+{FormatSize(NetSpaceChange)}"
				: $"-{FormatSize(Math.Abs(NetSpaceChange))}";

			parts.Add($"Space: {spaceChange}");

			return string.Join(" • ", parts);
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
