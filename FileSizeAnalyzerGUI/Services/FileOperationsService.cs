using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public enum FileOperationType
	{
		Delete,
		Move,
		Compress,
		CreateHardLink
	}

	public class FileOperation
	{
		public FileOperationType Type { get; set; }
		public List<string> SourcePaths { get; set; } = new();
		public string? DestinationPath { get; set; }
		public DateTime Timestamp { get; set; } = DateTime.Now;
		public bool UseRecycleBin { get; set; } = true;
		public long TotalSize { get; set; }
		public int FileCount { get; set; }
		public bool Success { get; set; }
		public string? ErrorMessage { get; set; }
	}

	public class FileOperationResult
	{
		public bool Success { get; set; }
		public int SuccessCount { get; set; }
		public int FailedCount { get; set; }
		public long TotalSize { get; set; }
		public List<string> Errors { get; set; } = new();
		public TimeSpan Duration { get; set; }
	}

	public class FileOperationsService
	{
		private readonly Stack<FileOperation> _operationHistory = new();
		private const int MaxHistorySize = 50;
		private readonly ILogger _logger;

		public FileOperationsService(ILogger logger)
		{
			_logger = logger;
		}

		#region Delete Operations

		public async Task<FileOperationResult> DeleteFilesAsync(List<string> filePaths, bool useRecycleBin = true, IProgress<int>? progress = null)
		{
			var result = new FileOperationResult();
			var startTime = DateTime.Now;

			try
			{
				var validFiles = filePaths.Where(File.Exists).ToList();
				result.TotalSize = validFiles.Sum(f => new FileInfo(f).Length);

				for (int i = 0; i < validFiles.Count; i++)
				{
					var file = validFiles[i];
					try
					{
						if (useRecycleBin)
						{
							FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
						}
						else
						{
							File.Delete(file);
						}

						result.SuccessCount++;
						_logger.Info($"Deleted: {file}");
					}
					catch (Exception ex)
					{
						result.FailedCount++;
						result.Errors.Add($"{file}: {ex.Message}");
						_logger.Error($"Failed to delete {file}: {ex.Message}");
					}

					progress?.Report((i + 1) * 100 / validFiles.Count);
					await Task.Delay(1); // Allow UI to update
				}

				// Record operation for potential undo (only if recycle bin was used)
				if (useRecycleBin && result.SuccessCount > 0)
				{
					RecordOperation(new FileOperation
					{
						Type = FileOperationType.Delete,
						SourcePaths = validFiles.Take(result.SuccessCount).ToList(),
						UseRecycleBin = true,
						TotalSize = result.TotalSize,
						FileCount = result.SuccessCount,
						Success = true
					});
				}

				result.Success = result.FailedCount == 0;
			}
			catch (Exception ex)
			{
				result.Success = false;
				result.Errors.Add($"Operation failed: {ex.Message}");
				_logger.Error($"Delete operation failed: {ex.Message}");
			}

			result.Duration = DateTime.Now - startTime;
			return result;
		}

		public async Task<FileOperationResult> DeleteDirectoriesAsync(List<string> directoryPaths, bool useRecycleBin = true, IProgress<int>? progress = null)
		{
			var result = new FileOperationResult();
			var startTime = DateTime.Now;

			try
			{
				var validDirs = directoryPaths.Where(Directory.Exists).ToList();

				for (int i = 0; i < validDirs.Count; i++)
				{
					var dir = validDirs[i];
					try
					{
						if (useRecycleBin)
						{
							FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
						}
						else
						{
							Directory.Delete(dir, true);
						}

						result.SuccessCount++;
						_logger.Info($"Deleted directory: {dir}");
					}
					catch (Exception ex)
					{
						result.FailedCount++;
						result.Errors.Add($"{dir}: {ex.Message}");
						_logger.Error($"Failed to delete directory {dir}: {ex.Message}");
					}

					progress?.Report((i + 1) * 100 / validDirs.Count);
					await Task.Delay(1);
				}

				result.Success = result.FailedCount == 0;
			}
			catch (Exception ex)
			{
				result.Success = false;
				result.Errors.Add($"Operation failed: {ex.Message}");
				_logger.Error($"Delete directories operation failed: {ex.Message}");
			}

			result.Duration = DateTime.Now - startTime;
			return result;
		}

		#endregion

		#region Move Operations

		public async Task<FileOperationResult> MoveFilesAsync(List<string> sourcePaths, string destinationFolder, IProgress<int>? progress = null)
		{
			var result = new FileOperationResult();
			var startTime = DateTime.Now;

			try
			{
				// Create destination folder if it doesn't exist
				if (!Directory.Exists(destinationFolder))
				{
					Directory.CreateDirectory(destinationFolder);
				}

				var validFiles = sourcePaths.Where(File.Exists).ToList();
				result.TotalSize = validFiles.Sum(f => new FileInfo(f).Length);

				for (int i = 0; i < validFiles.Count; i++)
				{
					var sourceFile = validFiles[i];
					try
					{
						var fileName = Path.GetFileName(sourceFile);
						var destFile = Path.Combine(destinationFolder, fileName);

						// Handle duplicate names
						int counter = 1;
						while (File.Exists(destFile))
						{
							var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
							var extension = Path.GetExtension(fileName);
							destFile = Path.Combine(destinationFolder, $"{nameWithoutExt}_{counter}{extension}");
							counter++;
						}

						File.Move(sourceFile, destFile);
						result.SuccessCount++;
						_logger.Info($"Moved: {sourceFile} -> {destFile}");
					}
					catch (Exception ex)
					{
						result.FailedCount++;
						result.Errors.Add($"{sourceFile}: {ex.Message}");
						_logger.Error($"Failed to move {sourceFile}: {ex.Message}");
					}

					progress?.Report((i + 1) * 100 / validFiles.Count);
					await Task.Delay(1);
				}

				if (result.SuccessCount > 0)
				{
					RecordOperation(new FileOperation
					{
						Type = FileOperationType.Move,
						SourcePaths = validFiles.Take(result.SuccessCount).ToList(),
						DestinationPath = destinationFolder,
						TotalSize = result.TotalSize,
						FileCount = result.SuccessCount,
						Success = true
					});
				}

				result.Success = result.FailedCount == 0;
			}
			catch (Exception ex)
			{
				result.Success = false;
				result.Errors.Add($"Operation failed: {ex.Message}");
				_logger.Error($"Move operation failed: {ex.Message}");
			}

			result.Duration = DateTime.Now - startTime;
			return result;
		}

		#endregion

		#region Compress Operations

		public async Task<FileOperationResult> CompressFilesAsync(List<string> filePaths, string zipPath, IProgress<int>? progress = null)
		{
			var result = new FileOperationResult();
			var startTime = DateTime.Now;

			try
			{
				var validFiles = filePaths.Where(File.Exists).ToList();
				result.TotalSize = validFiles.Sum(f => new FileInfo(f).Length);

				using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
				{
					for (int i = 0; i < validFiles.Count; i++)
					{
						var file = validFiles[i];
						try
						{
							var entryName = Path.GetFileName(file);
							archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
							result.SuccessCount++;
							_logger.Info($"Added to archive: {file}");
						}
						catch (Exception ex)
						{
							result.FailedCount++;
							result.Errors.Add($"{file}: {ex.Message}");
							_logger.Error($"Failed to compress {file}: {ex.Message}");
						}

						progress?.Report((i + 1) * 100 / validFiles.Count);
						await Task.Delay(1);
					}
				}

				if (result.SuccessCount > 0)
				{
					RecordOperation(new FileOperation
					{
						Type = FileOperationType.Compress,
						SourcePaths = validFiles.Take(result.SuccessCount).ToList(),
						DestinationPath = zipPath,
						TotalSize = result.TotalSize,
						FileCount = result.SuccessCount,
						Success = true
					});
				}

				result.Success = result.FailedCount == 0;
			}
			catch (Exception ex)
			{
				result.Success = false;
				result.Errors.Add($"Operation failed: {ex.Message}");
				_logger.Error($"Compress operation failed: {ex.Message}");
			}

			result.Duration = DateTime.Now - startTime;
			return result;
		}

		#endregion

		#region Utility Methods

		public long CalculateTotalSize(List<string> paths)
		{
			long total = 0;
			foreach (var path in paths)
			{
				try
				{
					if (File.Exists(path))
					{
						total += new FileInfo(path).Length;
					}
					else if (Directory.Exists(path))
					{
						total += GetDirectorySize(path);
					}
				}
				catch
				{
					// Skip files we can't access
				}
			}
			return total;
		}

		private long GetDirectorySize(string path)
		{
			try
			{
				var dirInfo = new DirectoryInfo(path);
				return dirInfo.EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length);
			}
			catch
			{
				return 0;
			}
		}

		public string FormatSize(long bytes)
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

		#endregion

		#region Operation History

		private void RecordOperation(FileOperation operation)
		{
			_operationHistory.Push(operation);

			// Limit history size
			if (_operationHistory.Count > MaxHistorySize)
			{
				var temp = _operationHistory.Take(MaxHistorySize).ToList();
				_operationHistory.Clear();
				temp.Reverse();
				foreach (var op in temp)
				{
					_operationHistory.Push(op);
				}
			}
		}

		public List<FileOperation> GetOperationHistory()
		{
			return _operationHistory.ToList();
		}

		public void ClearHistory()
		{
			_operationHistory.Clear();
		}

		#endregion
	}
}
