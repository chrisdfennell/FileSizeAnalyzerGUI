using System;
using System.IO;
using System.Text;
using System.Linq;

namespace FileSizeAnalyzerGUI.Services
{
	public class FilePreviewResult
	{
		public bool CanPreview { get; set; }
		public string PreviewText { get; set; } = string.Empty;
		public string Message { get; set; } = string.Empty;
		public bool IsBinary { get; set; }
	}

	public class FilePreviewService
	{
		private const int MaxPreviewSize = 512 * 1024; // 512 KB max
		private const int PreviewLines = 100; // Max lines to preview

		private static readonly string[] TextExtensions =
		{
			".txt", ".log", ".md", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts",
			".cs", ".cpp", ".c", ".h", ".java", ".py", ".rb", ".php", ".sql", ".sh",
			".bat", ".cmd", ".ps1", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".gitignore",
			".cs", ".csproj", ".sln", ".config", ".csv", ".tsv"
		};

		private static readonly string[] ImageExtensions =
		{
			".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".ico"
		};

		private static readonly string[] CodeExtensions =
		{
			".cs", ".cpp", ".c", ".h", ".java", ".py", ".js", ".ts", ".html", ".css", ".sql"
		};

		public FilePreviewResult PreviewFile(string filePath)
		{
			var result = new FilePreviewResult();

			if (!File.Exists(filePath))
			{
				result.Message = "File not found.";
				return result;
			}

			var fileInfo = new FileInfo(filePath);
			var extension = fileInfo.Extension.ToLower();

			// Check file size
			if (fileInfo.Length > MaxPreviewSize)
			{
				result.Message = $"File too large to preview ({FormatSize(fileInfo.Length)}). " +
					$"Maximum preview size is {FormatSize(MaxPreviewSize)}.";
				return result;
			}

			// Check if it's a supported text file
			if (TextExtensions.Contains(extension))
			{
				return PreviewTextFile(filePath, fileInfo);
			}

			// Check if it's an image
			if (ImageExtensions.Contains(extension))
			{
				result.Message = $"Image file: {fileInfo.Name}\n" +
					$"Size: {FormatSize(fileInfo.Length)}\n" +
					$"Dimensions: (Use an image viewer to see full image)";
				result.CanPreview = true;
				return result;
			}

			// Try to detect if it's a text file by reading the first few bytes
			if (IsLikelyTextFile(filePath))
			{
				return PreviewTextFile(filePath, fileInfo);
			}

			// Binary file
			result.IsBinary = true;
			result.Message = $"Binary file: {fileInfo.Name}\n" +
				$"Size: {FormatSize(fileInfo.Length)}\n" +
				$"Type: {extension.TrimStart('.')}\n" +
				$"Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n\n" +
				GetBinaryFileInfo(filePath, fileInfo);
			result.CanPreview = true;
			return result;
		}

		private FilePreviewResult PreviewTextFile(string filePath, FileInfo fileInfo)
		{
			var result = new FilePreviewResult { CanPreview = true };

			try
			{
				// Try different encodings
				Encoding? encoding = DetectEncoding(filePath);
				var lines = File.ReadLines(filePath, encoding ?? Encoding.UTF8).Take(PreviewLines).ToList();

				var preview = new StringBuilder();
				preview.AppendLine($"File: {fileInfo.Name}");
				preview.AppendLine($"Size: {FormatSize(fileInfo.Length)}");
				preview.AppendLine($"Lines shown: {lines.Count} {(lines.Count >= PreviewLines ? $"(truncated, file may have more)" : "")}");
				preview.AppendLine($"Encoding: {encoding?.EncodingName ?? "UTF-8"}");
				preview.AppendLine(new string('-', 60));
				preview.AppendLine();

				foreach (var line in lines)
				{
					// Truncate very long lines
					var displayLine = line.Length > 200 ? line.Substring(0, 200) + "..." : line;
					preview.AppendLine(displayLine);
				}

				if (lines.Count >= PreviewLines)
				{
					preview.AppendLine();
					preview.AppendLine(new string('-', 60));
					preview.AppendLine($"... {(lines.Count >= PreviewLines ? "more content not shown" : "")}");
				}

				result.PreviewText = preview.ToString();
			}
			catch (Exception ex)
			{
				result.Message = $"Error reading file: {ex.Message}";
				result.CanPreview = false;
			}

			return result;
		}

		private bool IsLikelyTextFile(string filePath)
		{
			try
			{
				using var stream = File.OpenRead(filePath);
				var buffer = new byte[Math.Min(8192, stream.Length)];
				var bytesRead = stream.Read(buffer, 0, buffer.Length);

				// Check for null bytes (common in binary files)
				for (int i = 0; i < bytesRead; i++)
				{
					if (buffer[i] == 0)
						return false;
				}

				// Check for high proportion of printable ASCII characters
				int printableCount = 0;
				for (int i = 0; i < bytesRead; i++)
				{
					if ((buffer[i] >= 32 && buffer[i] <= 126) || buffer[i] == '\t' || buffer[i] == '\n' || buffer[i] == '\r')
					{
						printableCount++;
					}
				}

				return (double)printableCount / bytesRead > 0.85; // 85% printable = likely text
			}
			catch
			{
				return false;
			}
		}

		private Encoding? DetectEncoding(string filePath)
		{
			try
			{
				using var reader = new StreamReader(filePath, true);
				reader.Peek(); // Trigger encoding detection
				return reader.CurrentEncoding;
			}
			catch
			{
				return null;
			}
		}

		private string GetBinaryFileInfo(string filePath, FileInfo fileInfo)
		{
			var info = new StringBuilder();
			var extension = fileInfo.Extension.ToLower();

			// Provide info based on file type
			switch (extension)
			{
				case ".exe":
				case ".dll":
					info.AppendLine("Executable or library file");
					info.AppendLine("⚠️ Be careful when running executable files from unknown sources");
					break;

				case ".zip":
				case ".rar":
				case ".7z":
				case ".tar":
				case ".gz":
					info.AppendLine("Compressed archive file");
					info.AppendLine("Use an archive utility to extract contents");
					break;

				case ".pdf":
					info.AppendLine("PDF document");
					info.AppendLine("Use a PDF reader to view contents");
					break;

				case ".mp4":
				case ".avi":
				case ".mkv":
				case ".mov":
					info.AppendLine("Video file");
					info.AppendLine("Use a media player to view");
					break;

				case ".mp3":
				case ".wav":
				case ".flac":
					info.AppendLine("Audio file");
					info.AppendLine("Use a media player to listen");
					break;

				case ".doc":
				case ".docx":
				case ".xls":
				case ".xlsx":
				case ".ppt":
				case ".pptx":
					info.AppendLine("Microsoft Office document");
					info.AppendLine("Use Microsoft Office or compatible software to view");
					break;

				default:
					// Show hex dump of first 256 bytes
					try
					{
						using var stream = File.OpenRead(filePath);
						var buffer = new byte[Math.Min(256, stream.Length)];
						stream.Read(buffer, 0, buffer.Length);

						info.AppendLine("\nHex dump (first 256 bytes):");
						info.AppendLine();
						for (int i = 0; i < buffer.Length; i += 16)
						{
							info.Append($"{i:X4}: ");
							for (int j = 0; j < 16 && i + j < buffer.Length; j++)
							{
								info.Append($"{buffer[i + j]:X2} ");
							}
							info.AppendLine();
						}
					}
					catch (Exception ex)
					{
						info.AppendLine($"Unable to read file: {ex.Message}");
					}
					break;
			}

			return info.ToString();
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
