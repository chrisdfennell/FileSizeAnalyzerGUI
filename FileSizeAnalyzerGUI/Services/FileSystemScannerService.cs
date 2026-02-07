using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class ScanOptions
	{
		public bool SkipSystemFiles { get; set; }
		public bool SkipWindowsDirectory { get; set; }
		public List<string> ExclusionPatterns { get; set; } = new List<string>();
		public bool UseParallelScanning { get; set; } = true; // Enable multi-threaded scanning by default
		public int MaxDegreeOfParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
	}

	public class ScanProgressData
	{
		public string CurrentPath { get; set; } = string.Empty;
		public int FilesProcessed { get; set; }
		public int DirectoriesProcessed { get; set; }
		public long BytesProcessed { get; set; }
		public double PercentComplete { get; set; }
		public TimeSpan? EstimatedTimeRemaining { get; set; }
		public DateTime StartTime { get; set; }
		public int ItemsPerSecond { get; set; }

		public string GetFormattedMessage()
		{
			var parts = new List<string>
			{
				$"Files: {FilesProcessed:N0}",
				$"Dirs: {DirectoriesProcessed:N0}"
			};

			if (BytesProcessed > 0)
			{
				parts.Add($"Size: {FormatSize(BytesProcessed)}");
			}

			if (PercentComplete > 0)
			{
				parts.Add($"{PercentComplete:F1}%");
			}

			if (EstimatedTimeRemaining.HasValue && EstimatedTimeRemaining.Value.TotalSeconds > 1)
			{
				parts.Add($"ETA: {FormatTimeSpan(EstimatedTimeRemaining.Value)}");
			}

			if (ItemsPerSecond > 0)
			{
				parts.Add($"{ItemsPerSecond:N0} items/sec");
			}

			var statusLine = string.Join(" • ", parts);

			if (!string.IsNullOrEmpty(CurrentPath))
			{
				return $"{statusLine}\n{CurrentPath}";
			}

			return statusLine;
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

		private static string FormatTimeSpan(TimeSpan ts)
		{
			if (ts.TotalHours >= 1)
				return $"{(int)ts.TotalHours}h {ts.Minutes}m";
			if (ts.TotalMinutes >= 1)
				return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
			return $"{(int)ts.TotalSeconds}s";
		}
	}

	public class ScanResult
	{
		public List<FileSystemNode> AllFiles { get; set; } = new List<FileSystemNode>();
		public List<FileSystemNode> RootNodes { get; set; } = new List<FileSystemNode>();
		public StringBuilder Errors { get; set; } = new StringBuilder();
	}

	public class FileSystemScannerService : IFileSystemScannerService
	{
		private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		private readonly ILogger _logger;
		private DateTime _lastStatusUpdateTime;
		private ScanProgressData _progressData;
		private int _totalItemsEstimate;
		private readonly object _progressLock = new object();

		public FileSystemScannerService(ILogger logger)
		{
			_logger = logger;
		}

		public async Task<ScanResult> ScanDirectoryAsync(
			string scanPath,
			ScanOptions options,
			IProgress<FileSystemNode> nodeProgress,
			IProgress<string> textProgress,
			IProgress<double> percentProgress,
			CancellationToken token)
		{
			_logger.Info($"Starting scan of directory: {scanPath}");
			var result = new ScanResult();
			_lastStatusUpdateTime = DateTime.MinValue;

			// Initialize progress tracking
			_progressData = new ScanProgressData
			{
				StartTime = DateTime.Now,
				CurrentPath = scanPath
			};
			_totalItemsEstimate = EstimateItemCount(scanPath);

			var nodeMap = new Dictionary<string, FileSystemNode>();

			await Task.Run(() =>
			{
				ScanDirectoryRecursive(
					scanPath,
					null,
					nodeProgress,
					textProgress,
					percentProgress,
					token,
					options,
					result,
					nodeMap);
			}, token);

			return result;
		}

		private int EstimateItemCount(string path)
		{
			// Quick estimate to get initial item count for better progress calculation
			try
			{
				var dirInfo = new DirectoryInfo(path);
				int count = 0;

				// Sample a few directories to estimate average items per directory
				var stack = new Stack<DirectoryInfo>();
				stack.Push(dirInfo);
				int sampledDirs = 0;
				int maxSample = 50; // Sample up to 50 directories for estimate

				while (stack.Count > 0 && sampledDirs < maxSample)
				{
					var current = stack.Pop();
					try
					{
						var items = current.EnumerateFileSystemInfos().Take(100).ToList();
						count += items.Count;
						sampledDirs++;

						foreach (var item in items.OfType<DirectoryInfo>().Take(3))
						{
							if (stack.Count < maxSample)
								stack.Push(item);
						}
					}
					catch
					{
						// Ignore errors during estimation
					}
				}

				// Extrapolate from sample (rough estimate)
				return Math.Max(100, count * 10);
			}
			catch
			{
				return 1000; // Default estimate
			}
		}

		private long ScanDirectoryRecursive(
			string path,
			FileSystemNode parent,
			IProgress<FileSystemNode> nodeProgress,
			IProgress<string> textProgress,
			IProgress<double> percentProgress,
			CancellationToken token,
			ScanOptions options,
			ScanResult result,
			Dictionary<string, FileSystemNode> nodeMap)
		{
			if (token.IsCancellationRequested) return 0;

			if (IsExcluded(path, options.ExclusionPatterns))
			{
				return 0;
			}

			if (options.SkipWindowsDirectory && path.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
			{
				return 0;
			}

			// Update progress data
			lock (_progressLock)
			{
				_progressData.CurrentPath = path;
				_progressData.DirectoriesProcessed++;

				// Update progress every 100ms to avoid UI overhead
				if ((DateTime.Now - _lastStatusUpdateTime).TotalMilliseconds > 100)
				{
					UpdateProgressMetrics();
					textProgress.Report(_progressData.GetFormattedMessage());
					percentProgress.Report(_progressData.PercentComplete);
					_lastStatusUpdateTime = DateTime.Now;
				}
			}

			DirectoryInfo dirInfo;
			try
			{
				dirInfo = new DirectoryInfo(path);
				if (parent != null && options.SkipSystemFiles && dirInfo.Attributes.HasFlag(FileAttributes.System))
				{
					return 0;
				}
			}
			catch (Exception ex)
			{
				var errorMsg = $"Error accessing '{path}': {ex.Message}";
				result.Errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
				return 0;
			}

			var currentNode = new FileSystemNode
			{
				FullPath = dirInfo.FullName,
				IsDirectory = true,
				Parent = parent,
				CreationTime = dirInfo.CreationTime,
				LastWriteTime = dirInfo.LastWriteTime,
				Icon = IconManager.GetIcon(dirInfo.FullName, true)
			};

			if (parent == null)
			{
				result.RootNodes.Add(currentNode);
				nodeMap[currentNode.FullPath] = currentNode;
			}

			nodeProgress.Report(currentNode);

			long currentSize = 0;

			try
			{
				foreach (var file in dirInfo.EnumerateFiles())
				{
					if (token.IsCancellationRequested) return 0;

					if (options.SkipSystemFiles && file.Attributes.HasFlag(FileAttributes.System))
					{
						continue;
					}

					if (IsExcluded(file.FullName, options.ExclusionPatterns))
					{
						continue;
					}

					currentSize += file.Length;

					const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

					var fileNode = new FileSystemNode
					{
						FullPath = file.FullName,
						IsDirectory = false,
						Parent = currentNode,
						Size = file.Length,
						CreationTime = file.CreationTime,
						LastWriteTime = file.LastWriteTime,
						Icon = IconManager.GetIcon(file.FullName, false),
						IsCloudOnly = file.Attributes.HasFlag(RecallOnDataAccess),
						FormattedSize = FormatSize(file.Length)
					};

					result.AllFiles.Add(fileNode);
					nodeProgress.Report(fileNode);

					// Track file progress
					lock (_progressLock)
					{
						_progressData.FilesProcessed++;
						_progressData.BytesProcessed += file.Length;
					}
				}
			}
			catch (Exception ex)
			{
				var errorMsg = $"Error reading files in '{path}': {ex.Message}";
				result.Errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
			}

			try
			{
				var subDirs = dirInfo.EnumerateDirectories().ToList();

				if (options.UseParallelScanning && subDirs.Count > 1)
				{
					// Parallel scanning for better performance
					var subDirSizes = new long[subDirs.Count];
					var parallelOptions = new ParallelOptions
					{
						MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
						CancellationToken = token
					};

					Parallel.For(0, subDirs.Count, parallelOptions, i =>
					{
						subDirSizes[i] = ScanDirectoryRecursive(
							subDirs[i].FullName,
							currentNode,
							nodeProgress,
							textProgress,
							percentProgress,
							token,
							options,
							result,
							nodeMap);
					});

					currentSize += subDirSizes.Sum();
				}
				else
				{
					// Sequential scanning (fallback or for small directory counts)
					foreach (var subDir in subDirs)
					{
						currentSize += ScanDirectoryRecursive(
							subDir.FullName,
							currentNode,
							nodeProgress,
							textProgress,
							percentProgress,
							token,
							options,
							result,
							nodeMap);
					}
				}
			}
			catch (Exception ex)
			{
				var errorMsg = $"Error reading subdirectories in '{path}': {ex.Message}";
				result.Errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
			}

			currentNode.Size = currentSize;
			currentNode.FormattedSize = FormatSize(currentSize);

			return currentSize;
		}

		private void UpdateProgressMetrics()
		{
			// Calculate progress percentage based on items processed vs estimate
			int totalItemsProcessed = _progressData.FilesProcessed + _progressData.DirectoriesProcessed;

			// Adjust estimate if we've exceeded it (scan is larger than expected)
			if (totalItemsProcessed > _totalItemsEstimate)
			{
				_totalItemsEstimate = totalItemsProcessed + Math.Max(100, totalItemsProcessed / 10);
			}

			_progressData.PercentComplete = _totalItemsEstimate > 0
				? Math.Min(99.0, (totalItemsProcessed * 100.0) / _totalItemsEstimate)
				: 0;

			// Calculate ETA based on current rate
			var elapsed = DateTime.Now - _progressData.StartTime;
			if (elapsed.TotalSeconds > 2 && totalItemsProcessed > 10)
			{
				double itemsPerSecond = totalItemsProcessed / elapsed.TotalSeconds;
				_progressData.ItemsPerSecond = (int)itemsPerSecond;

				if (itemsPerSecond > 0)
				{
					int remainingItems = Math.Max(0, _totalItemsEstimate - totalItemsProcessed);
					double secondsRemaining = remainingItems / itemsPerSecond;
					_progressData.EstimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining);
				}
			}
		}

		private bool IsExcluded(string path, List<string> exclusionPatterns)
		{
			if (exclusionPatterns == null || !exclusionPatterns.Any()) return false;

			foreach (var pattern in exclusionPatterns)
			{
				if (string.IsNullOrWhiteSpace(pattern)) continue;

				try
				{
					var regexPattern = "^" + Regex.Escape(pattern)
						.Replace("\\*", ".*")
						.Replace("\\?", ".") + "$";
					if (Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase))
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private string FormatSize(long bytes)
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

		public void FindEmptyFolders(FileSystemNode root, IProgress<FileSystemNode> progress, CancellationToken token)
		{
			if (root == null) return;
			var stack = new Stack<FileSystemNode>();
			stack.Push(root);
			while (stack.Count > 0)
			{
				if (token.IsCancellationRequested) return;
				var current = stack.Pop();
				if (current.IsDirectory && (current.Children == null || !current.Children.Any()))
				{
					progress.Report(current);
				}

				if (current.Children == null) continue;
				foreach (var child in current.Children.Reverse())
				{
					stack.Push(child);
				}
			}
		}
	}
}
