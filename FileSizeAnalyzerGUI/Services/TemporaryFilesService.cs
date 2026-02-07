using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class TemporaryFileCategory
	{
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public List<FileSystemNode> Files { get; set; } = new();
		public long TotalSize { get; set; }
		public bool IsSafeToDelete { get; set; } = true;
	}

	public class TemporaryFilesService
	{
		private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".tmp", ".temp", ".bak", ".backup", ".old", ".$$$", ".~",
			".cache", ".crdownload", ".part", ".partial"
		};

		private static readonly HashSet<string> TempFolderNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"temp", "tmp", "cache", "Cache", "Temporary Internet Files",
			".cache", "thumbnails", ".thumbnails"
		};

		private static readonly List<string> BrowserCachePaths = new()
		{
			@"AppData\Local\Google\Chrome\User Data\Default\Cache",
			@"AppData\Local\Google\Chrome\User Data\Default\Code Cache",
			@"AppData\Local\Microsoft\Edge\User Data\Default\Cache",
			@"AppData\Local\Mozilla\Firefox\Profiles",
			@"AppData\Roaming\Opera Software\Opera Stable\Cache"
		};

		private static readonly List<string> SystemTempPaths = new()
		{
			Path.GetTempPath(),
			@"C:\Windows\Temp",
			@"C:\Temp"
		};

		public List<TemporaryFileCategory> AnalyzeTemporaryFiles(List<FileSystemNode> allFiles)
		{
			var categories = new List<TemporaryFileCategory>();

			// Category 1: Temp file extensions
			var tempExtensionFiles = allFiles
				.Where(f => !f.IsDirectory && TempExtensions.Contains(f.Extension ?? ""))
				.ToList();

			if (tempExtensionFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Temporary Files",
					Description = "Files with temporary extensions (.tmp, .temp, .bak, etc.)",
					Files = tempExtensionFiles,
					TotalSize = tempExtensionFiles.Sum(f => f.Size),
					IsSafeToDelete = true
				});
			}

			// Category 2: Browser caches
			var browserCacheFiles = allFiles
				.Where(f => !f.IsDirectory && BrowserCachePaths.Any(p => f.FullPath.Contains(p, StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (browserCacheFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Browser Caches",
					Description = "Cached files from web browsers",
					Files = browserCacheFiles,
					TotalSize = browserCacheFiles.Sum(f => f.Size),
					IsSafeToDelete = true
				});
			}

			// Category 3: System temp folders
			var systemTempFiles = allFiles
				.Where(f => !f.IsDirectory && SystemTempPaths.Any(p => f.FullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (systemTempFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "System Temporary Files",
					Description = "Files in Windows temp directories",
					Files = systemTempFiles,
					TotalSize = systemTempFiles.Sum(f => f.Size),
					IsSafeToDelete = true
				});
			}

			// Category 4: Cache folders
			var cacheFolderFiles = allFiles
				.Where(f => !f.IsDirectory && IsInCacheFolder(f.FullPath))
				.ToList();

			if (cacheFolderFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Cache Folders",
					Description = "Files in folders named 'cache' or similar",
					Files = cacheFolderFiles,
					TotalSize = cacheFolderFiles.Sum(f => f.Size),
					IsSafeToDelete = false // More cautious with these
				});
			}

			// Category 5: Windows Update cache
			var windowsUpdateFiles = allFiles
				.Where(f => !f.IsDirectory &&
					   (f.FullPath.Contains(@"C:\Windows\SoftwareDistribution", StringComparison.OrdinalIgnoreCase) ||
					    f.FullPath.Contains(@"C:\Windows\Downloaded Program Files", StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (windowsUpdateFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Windows Update Cache",
					Description = "Windows Update downloaded files",
					Files = windowsUpdateFiles,
					TotalSize = windowsUpdateFiles.Sum(f => f.Size),
					IsSafeToDelete = false // Requires admin rights
				});
			}

			// Category 6: Thumbnail cache
			var thumbnailFiles = allFiles
				.Where(f => !f.IsDirectory &&
					   (f.FullPath.EndsWith("thumbs.db", StringComparison.OrdinalIgnoreCase) ||
					    f.FullPath.EndsWith("ehthumbs.db", StringComparison.OrdinalIgnoreCase) ||
					    f.FullPath.Contains(@"\Thumbs.db", StringComparison.OrdinalIgnoreCase)))
				.ToList();

			if (thumbnailFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Thumbnail Databases",
					Description = "Windows thumbnail cache files",
					Files = thumbnailFiles,
					TotalSize = thumbnailFiles.Sum(f => f.Size),
					IsSafeToDelete = true
				});
			}

			// Category 7: Log files (older than 30 days)
			var oldLogFiles = allFiles
				.Where(f => !f.IsDirectory &&
					   (f.Extension?.Equals(".log", StringComparison.OrdinalIgnoreCase) == true ||
					    f.Extension?.Equals(".etl", StringComparison.OrdinalIgnoreCase) == true) &&
					   (DateTime.Now - f.LastWriteTime).TotalDays > 30)
				.ToList();

			if (oldLogFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Old Log Files",
					Description = "Log files older than 30 days",
					Files = oldLogFiles,
					TotalSize = oldLogFiles.Sum(f => f.Size),
					IsSafeToDelete = false // Users might want to review
				});
			}

			// Category 8: Recycle Bin (if scanned)
			var recycleBinFiles = allFiles
				.Where(f => !f.IsDirectory && f.FullPath.Contains(@"$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (recycleBinFiles.Any())
			{
				categories.Add(new TemporaryFileCategory
				{
					Name = "Recycle Bin",
					Description = "Files in the Recycle Bin",
					Files = recycleBinFiles,
					TotalSize = recycleBinFiles.Sum(f => f.Size),
					IsSafeToDelete = true
				});
			}

			return categories.OrderByDescending(c => c.TotalSize).ToList();
		}

		private bool IsInCacheFolder(string path)
		{
			var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return parts.Any(part => TempFolderNames.Contains(part));
		}

		public string GetCleanupSummary(List<TemporaryFileCategory> categories)
		{
			if (!categories.Any())
				return "No temporary files found.";

			var totalSize = categories.Sum(c => c.TotalSize);
			var totalFiles = categories.Sum(c => c.Files.Count);
			var safeToDeleteSize = categories.Where(c => c.IsSafeToDelete).Sum(c => c.TotalSize);

			return $"Found {totalFiles:N0} temporary files totaling {FormatSize(totalSize)}\n" +
				   $"Safe to delete: {FormatSize(safeToDeleteSize)}";
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
