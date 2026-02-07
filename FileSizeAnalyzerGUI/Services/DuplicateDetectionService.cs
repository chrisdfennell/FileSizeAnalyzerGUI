using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class DuplicateDetectionOptions
	{
		public long MinDupSizeBytes { get; set; } = Constants.DuplicateDetection.DefaultMinDupSizeBytes;
		public bool VerifyByteByByte { get; set; } = Constants.DuplicateDetection.DefaultVerifyByteByByte;
		public long ForceVerifyAboveBytes { get; set; } = Constants.DuplicateDetection.DefaultForceVerifyAboveBytes;
	}

	public class DuplicateDetectionService : IDuplicateDetectionService
	{
		private readonly HashCache _hashCache;
		private readonly StringBuilder _errors;
		private readonly ILogger _logger;

		public DuplicateDetectionService(HashCache hashCache, StringBuilder errors, ILogger logger)
		{
			_hashCache = hashCache;
			_errors = errors;
			_logger = logger;
		}

		public void FindDuplicates(
			List<FileSystemNode> files,
			IProgress<DuplicateSet> progress,
			CancellationToken token,
			DuplicateDetectionOptions options)
		{
			_logger.Info($"Starting duplicate detection on {files.Count} files");
			var localFiles = files.Where(f => !f.IsCloudOnly).ToList();
			if (localFiles.Count < 2) return;

			var filesBySize = new Dictionary<long, List<FileSystemNode>>();
			foreach (var file in localFiles)
			{
				if (token.IsCancellationRequested) return;
				if (file.Size < options.MinDupSizeBytes) continue;

				if (!filesBySize.TryGetValue(file.Size, out var list))
				{
					list = new List<FileSystemNode>();
					filesBySize[file.Size] = list;
				}
				list.Add(file);
			}

			if (filesBySize.Count == 0) return;

			int degree = Math.Max(1, Environment.ProcessorCount / 2);

			foreach (var sizeGroup in filesBySize.Values)
			{
				if (token.IsCancellationRequested) return;
				if (sizeGroup.Count < 2) continue;

				var filesByPartialHash = new ConcurrentDictionary<string, List<FileSystemNode>>(StringComparer.OrdinalIgnoreCase);

				Parallel.ForEach(
					sizeGroup,
					new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = token },
					file =>
					{
						var ph = ComputePartialHash(file.FullPath);
						if (string.IsNullOrEmpty(ph)) return;
						var list = filesByPartialHash.GetOrAdd(ph, _ => new List<FileSystemNode>());
						lock (list) list.Add(file);
					});

				foreach (var partialHashGroup in filesByPartialHash.Values)
				{
					if (token.IsCancellationRequested) return;
					if (partialHashGroup.Count < 2) continue;

					var filesByFullHash = new ConcurrentDictionary<string, List<FileSystemNode>>(StringComparer.OrdinalIgnoreCase);

					Parallel.ForEach(
						partialHashGroup,
						new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = token },
						file =>
						{
							string fullHash = ComputeFastHash(file.FullPath);
							if (string.IsNullOrEmpty(fullHash)) return;
							var list = filesByFullHash.GetOrAdd(fullHash, _ => new List<FileSystemNode>());
							lock (list) list.Add(file);
						});

					foreach (var confirmedGroup in filesByFullHash.Values)
					{
						if (token.IsCancellationRequested) return;
						if (confirmedGroup.Count < 2) continue;

						var anchor = confirmedGroup[0];
						var finalGroup = new List<FileSystemNode> { anchor };

						for (int i = 1; i < confirmedGroup.Count; i++)
						{
							var cand = confirmedGroup[i];
							bool mustVerify = options.VerifyByteByByte || anchor.Size >= options.ForceVerifyAboveBytes;
							if (!mustVerify || AreFilesIdentical(anchor.FullPath, cand.FullPath))
							{
								finalGroup.Add(cand);
							}
						}

						if (finalGroup.Count > 1)
						{
							var first = finalGroup.First();
							var duplicateSet = new DuplicateSet
							{
								FileName = Path.GetFileName(first.FullPath),
								Count = finalGroup.Count,
								FormattedSize = FormatSize(first.Size * finalGroup.Count),
								Icon = first.Icon,
								Files = new ObservableCollection<FileSystemNode>(finalGroup)
							};
							progress.Report(duplicateSet);
						}
					}
				}
			}
		}

		private bool AreFilesIdentical(string path1, string path2)
		{
			const int bufferSize = Constants.DuplicateDetection.ComparisonBufferSize;
			try
			{
				using var fs1 = OpenFastReadStream(path1);
				using var fs2 = OpenFastReadStream(path2);

				if (fs1.Length != fs2.Length) return false;

				var buffer1 = new byte[bufferSize];
				var buffer2 = new byte[bufferSize];

				while (true)
				{
					int bytesRead1 = fs1.Read(buffer1, 0, bufferSize);
					int bytesRead2 = fs2.Read(buffer2, 0, bufferSize);

					if (bytesRead1 != bytesRead2) return false;
					if (bytesRead1 == 0) return true;

					for (int i = 0; i < bytesRead1; i++)
					{
						if (buffer1[i] != buffer2[i]) return false;
					}
				}
			}
			catch (Exception ex)
			{
				var errorMsg = $"Could not compare files '{Path.GetFileName(path1)}' and '{Path.GetFileName(path2)}': {ex.Message}";
				_errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
				return false;
			}
		}

		private string? ComputePartialHash(string filePath)
		{
			try
			{
				using var stream = OpenFastReadStream(filePath);
				if (stream.Length == 0) return "EMPTY";

				if (stream.Length <= Constants.DuplicateDetection.PartialHashThreshold)
				{
					var small = new XxHash64();
					small.Append(stream);
					byte[] hashBytes = new byte[8];
					small.GetHashAndReset(hashBytes);
					return BytesToHex(hashBytes);
				}

				const int window = Constants.DuplicateDetection.PartialHashWindowSize;
				var hasher = new XxHash64();
				var buffer = new byte[window];

				long[] offsets =
				{
					0,
					Math.Max(0, (stream.Length / 2) - window / 2),
					Math.Max(0, stream.Length - window)
				};

				foreach (var off in offsets)
				{
					stream.Position = off;
					int read = stream.Read(buffer, 0, window);
					if (read > 0) hasher.Append(new ReadOnlySpan<byte>(buffer, 0, read));
				}

				byte[] hashBytes2 = new byte[8];
				hasher.GetHashAndReset(hashBytes2);
				return BytesToHex(hashBytes2);
			}
			catch (Exception ex)
			{
				var errorMsg = $"Could not perform partial hash on file {filePath}: {ex.Message}";
				_errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
				return null;
			}
		}

		private string ComputeFastHash(string filePath)
		{
			try
			{
				var fi = new FileInfo(filePath);
				var mtimeUtc = fi.LastWriteTimeUtc;

				if (_hashCache.TryGet(filePath, fi.Length, mtimeUtc, out var cached))
					return cached;

				using var stream = OpenFastReadStream(filePath);
				if (stream.Length == 0)
				{
					_hashCache.Put(filePath, 0L, mtimeUtc, "EMPTY");
					return "EMPTY";
				}

				var hasher = new XxHash64();
				hasher.Append(stream);
				byte[] hashBytes = new byte[8];
				hasher.GetHashAndReset(hashBytes);
				var hash = BytesToHex(hashBytes);

				_hashCache.Put(filePath, fi.Length, mtimeUtc, hash);
				return hash;
			}
			catch (Exception ex)
			{
				var errorMsg = $"Could not hash file {filePath}: {ex.Message}";
				_errors.AppendLine(errorMsg);
				_logger.Warning(errorMsg, ex);
				return Guid.NewGuid().ToString();
			}
		}

		private static FileStream OpenFastReadStream(string path) =>
			new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
						   bufferSize: Constants.FileIO.StreamBufferSize, options: FileOptions.SequentialScan);

		private static string BytesToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");

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
	}

	public class HashCache
	{
		private readonly string _cachePath =
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
						 "FileSizeAnalyzer", Constants.HashCache.CacheFileName);

		private readonly Dictionary<string, (long size, DateTime mtimeUtc, string hash)> _map =
			new(StringComparer.OrdinalIgnoreCase);

		public HashCache()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
				if (!File.Exists(_cachePath)) return;
				foreach (var line in File.ReadAllLines(_cachePath))
				{
					var parts = line.Split('|');
					if (parts.Length != 4) continue;
					if (!long.TryParse(parts[1], out var size)) continue;
					if (!long.TryParse(parts[2], out var ticks)) continue;
					_map[parts[0]] = (size, new DateTime(ticks, DateTimeKind.Utc), parts[3]);
				}
			}
			catch { }
		}

		public bool TryGet(string path, long size, DateTime mtimeUtc, out string hash)
		{
			if (_map.TryGetValue(path, out var v) && v.size == size && v.mtimeUtc == mtimeUtc)
			{
				hash = v.hash; return true;
			}
			hash = null;
			return false;
		}

		public void Put(string path, long size, DateTime mtimeUtc, string hash)
		{
			_map[path] = (size, mtimeUtc, hash);
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
				using var w = new StreamWriter(_cachePath, false, Encoding.UTF8);
				foreach (var kvp in _map)
				{
					var (size, mt, h) = kvp.Value;
					w.WriteLine($"{kvp.Key}|{size}|{mt.Ticks}|{h}");
				}
			}
			catch { }
		}
	}
}
