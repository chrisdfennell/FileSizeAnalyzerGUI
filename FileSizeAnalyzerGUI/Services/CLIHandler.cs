using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class CLIHandler
	{
		private readonly IFileSystemScannerService _scannerService;
		private readonly IDuplicateDetectionService _duplicateService;
		private readonly ExportService _exportService;
		private readonly CleanupRecommendationService _cleanupService;
		private readonly ILogger _logger;

		public CLIHandler(
			IFileSystemScannerService scannerService,
			IDuplicateDetectionService duplicateService,
			ExportService exportService,
			CleanupRecommendationService cleanupService,
			ILogger logger)
		{
			_scannerService = scannerService;
			_duplicateService = duplicateService;
			_exportService = exportService;
			_cleanupService = cleanupService;
			_logger = logger;
		}

		public async Task<int> ExecuteAsync(string[] args)
		{
			if (args.Length == 0)
			{
				ShowHelp();
				return 0;
			}

			var command = args[0].ToLower();

			try
			{
				return command switch
				{
					"scan" => await ScanCommand(args.Skip(1).ToArray()),
					"duplicates" => await DuplicatesCommand(args.Skip(1).ToArray()),
					"export" => ExportCommand(args.Skip(1).ToArray()),
					"recommend" => await RecommendCommand(args.Skip(1).ToArray()),
					"help" or "--help" or "-h" or "/?" => ShowHelp(),
					"version" or "--version" or "-v" => ShowVersion(),
					_ => ShowUnknownCommand(command)
				};
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error: {ex.Message}");
				_logger.Error($"CLI Error: {ex.Message}");
				return 1;
			}
		}

		private async Task<int> ScanCommand(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("Error: No path specified");
				Console.WriteLine("Usage: FileSizeAnalyzerGUI scan <path> [options]");
				Console.WriteLine("Options:");
				Console.WriteLine("  --output <file>     Save results to file");
				Console.WriteLine("  --top <n>           Show top N largest files (default: 20)");
				return 1;
			}

			var path = args[0];
			if (!Directory.Exists(path))
			{
				Console.Error.WriteLine($"Error: Path not found: {path}");
				return 1;
			}

			var outputFile = GetArgValue(args, "--output");
			var topN = int.Parse(GetArgValue(args, "--top", "20"));

			Console.WriteLine($"Scanning: {path}");
			Console.WriteLine("Please wait...");

			int fileCount = 0;
			long totalSize = 0;

			var nodeProgress = new Progress<FileSystemNode>(node =>
			{
				fileCount++;
				totalSize += node.Size;
				if (fileCount % 100 == 0)
				{
					Console.Write($"\rFiles: {fileCount:N0} | Size: {FormatSize(totalSize)}");
				}
			});

			var textProgress = new Progress<string>(text =>
			{
				// Optional: can display status text
			});

			var percentProgress = new Progress<double>(percent =>
			{
				// Optional: can display percentage
			});

			var options = new ScanOptions
			{
				SkipSystemFiles = true,
				SkipWindowsDirectory = true
			};

			var cts = new CancellationTokenSource();
			var result = await _scannerService.ScanDirectoryAsync(path, options, nodeProgress, textProgress, percentProgress, cts.Token);

			Console.WriteLine($"\n\nScan Complete!");
			Console.WriteLine($"Files: {result.AllFiles.Count(f => !f.IsDirectory):N0}");
			Console.WriteLine($"Directories: {result.AllFiles.Count(f => f.IsDirectory):N0}");
			var rootNode = result.RootNodes.FirstOrDefault();
			if (rootNode != null)
			{
				Console.WriteLine($"Total Size: {FormatSize(rootNode.Size)}");
			}

			// Show top largest files
			var largestFiles = result.AllFiles
				.Where(f => !f.IsDirectory)
				.OrderByDescending(f => f.Size)
				.Take(topN)
				.ToList();

			Console.WriteLine($"\nTop {topN} Largest Files:");
			Console.WriteLine(new string('-', 80));
			foreach (var file in largestFiles)
			{
				Console.WriteLine($"{FormatSize(file.Size),12} | {file.FullPath}");
			}

			// Save to file if specified
			if (!string.IsNullOrEmpty(outputFile))
			{
				await SaveScanResults(result, outputFile);
				Console.WriteLine($"\nResults saved to: {outputFile}");
			}

			return 0;
		}

		private async Task<int> DuplicatesCommand(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("Error: No path specified");
				Console.WriteLine("Usage: FileSizeAnalyzerGUI duplicates <path> [options]");
				Console.WriteLine("Options:");
				Console.WriteLine("  --min-size <size>   Minimum file size in bytes (default: 1048576 = 1MB)");
				Console.WriteLine("  --output <file>     Save results to file");
				return 1;
			}

			var path = args[0];
			if (!Directory.Exists(path))
			{
				Console.Error.WriteLine($"Error: Path not found: {path}");
				return 1;
			}

			var minSize = long.Parse(GetArgValue(args, "--min-size", "1048576"));
			var outputFile = GetArgValue(args, "--output");

			Console.WriteLine($"Scanning for duplicates in: {path}");
			Console.WriteLine($"Minimum file size: {FormatSize(minSize)}");
			Console.WriteLine("Please wait...");

			int fileCount = 0;
			var nodeProgress = new Progress<FileSystemNode>(node =>
			{
				fileCount++;
				if (fileCount % 100 == 0)
				{
					Console.Write($"\rScanning: {fileCount:N0} files");
				}
			});

			var textProgress = new Progress<string>(_ => { });
			var percentProgress = new Progress<double>(_ => { });

			var scanOptions = new ScanOptions
			{
				SkipSystemFiles = true,
				SkipWindowsDirectory = true
			};

			var cts = new CancellationTokenSource();
			var scanResult = await _scannerService.ScanDirectoryAsync(path, scanOptions, nodeProgress, textProgress, percentProgress, cts.Token);

			Console.Write("\r" + new string(' ', 50) + "\r");
			Console.WriteLine("Finding duplicates...");

			var duplicates = new List<DuplicateSet>();
			var dupProgress = new Progress<DuplicateSet>(dup =>
			{
				duplicates.Add(dup);
				Console.Write($"\rDuplicate groups found: {duplicates.Count}");
			});

			var dupOptions = new DuplicateDetectionOptions
			{
				MinDupSizeBytes = minSize,
				VerifyByteByByte = false
			};

			await Task.Run(() =>
			{
				_duplicateService.FindDuplicates(
					scanResult.AllFiles.Where(f => !f.IsDirectory && f.Size >= minSize).ToList(),
					dupProgress,
					cts.Token,
					dupOptions);
			});

			Console.WriteLine($"\n\nDuplicate Scan Complete!");
			Console.WriteLine($"Duplicate Groups Found: {duplicates.Count:N0}");

			long totalWasted = 0;
			int totalDupFiles = 0;

			foreach (var dup in duplicates)
			{
				totalDupFiles += dup.Files.Count;
				totalWasted += (dup.Files.Count - 1) * (dup.Files.FirstOrDefault()?.Size ?? 0);
			}

			Console.WriteLine($"Total Duplicate Files: {totalDupFiles:N0}");
			Console.WriteLine($"Wasted Space: {FormatSize(totalWasted)}");

			if (duplicates.Any())
			{
				Console.WriteLine($"\nTop 10 Largest Duplicate Groups:");
				Console.WriteLine(new string('-', 80));

				foreach (var dup in duplicates.OrderByDescending(d => (d.Files.Count - 1) * (d.Files.FirstOrDefault()?.Size ?? 0)).Take(10))
				{
					var size = dup.Files.FirstOrDefault()?.Size ?? 0;
					var wasted = (dup.Files.Count - 1) * size;
					Console.WriteLine($"{FormatSize(wasted),12} | {dup.FileName} ({dup.Files.Count} copies)");

					foreach (var file in dup.Files.Take(3))
					{
						Console.WriteLine($"             | - {file.FullPath}");
					}
					if (dup.Files.Count > 3)
					{
						Console.WriteLine($"             | - ... and {dup.Files.Count - 3} more");
					}
					Console.WriteLine();
				}
			}

			if (!string.IsNullOrEmpty(outputFile))
			{
				await SaveDuplicateResults(duplicates, outputFile);
				Console.WriteLine($"Results saved to: {outputFile}");
			}

			return 0;
		}

		private int ExportCommand(string[] args)
		{
			Console.WriteLine("Export command requires GUI mode.");
			Console.WriteLine("Please use the application's export features from the GUI.");
			return 1;
		}

		private async Task<int> RecommendCommand(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("Error: No path specified");
				Console.WriteLine("Usage: FileSizeAnalyzerGUI recommend <path>");
				return 1;
			}

			var path = args[0];
			if (!Directory.Exists(path))
			{
				Console.Error.WriteLine($"Error: Path not found: {path}");
				return 1;
			}

			Console.WriteLine($"Analyzing: {path}");
			Console.WriteLine("Please wait...");

			int fileCount = 0;
			var nodeProgress = new Progress<FileSystemNode>(node =>
			{
				fileCount++;
				if (fileCount % 100 == 0)
				{
					Console.Write($"\rFiles: {fileCount:N0}");
				}
			});

			var textProgress = new Progress<string>(_ => { });
			var percentProgress = new Progress<double>(_ => { });

			var options = new ScanOptions
			{
				SkipSystemFiles = true,
				SkipWindowsDirectory = true
			};

			var cts = new CancellationTokenSource();
			var scanResult = await _scannerService.ScanDirectoryAsync(path, options, nodeProgress, textProgress, percentProgress, cts.Token);

			Console.Write("\r" + new string(' ', 50) + "\r");
			Console.WriteLine("Generating cleanup recommendations...");

			var recommendations = _cleanupService.GenerateRecommendations(scanResult.AllFiles.ToList());
			var summary = _cleanupService.GetOverallSummary(recommendations);

			Console.WriteLine($"\n{summary}\n");

			if (recommendations.Any())
			{
				Console.WriteLine("Recommendations:");
				Console.WriteLine(new string('=', 80));

				foreach (var rec in recommendations)
				{
					Console.WriteLine($"\n[{rec.Priority.ToUpper()}] {rec.Category}");
					Console.WriteLine($"Description: {rec.Description}");
					Console.WriteLine($"Potential Savings: {rec.FormattedSavings}");
					Console.WriteLine($"Files Affected: {rec.FileCount:N0}");
					Console.WriteLine($"Action: {rec.Action}");

					if (rec.SampleFiles.Any())
					{
						Console.WriteLine("Sample Files:");
						foreach (var sample in rec.SampleFiles.Take(3))
						{
							Console.WriteLine($"  - {sample}");
						}
					}
				}
			}

			return 0;
		}

		private int ShowHelp()
		{
			Console.WriteLine("FileSizeAnalyzer Pro - CLI Mode");
			Console.WriteLine(new string('=', 80));
			Console.WriteLine("\nUsage: FileSizeAnalyzerGUI <command> [options]");
			Console.WriteLine("\nCommands:");
			Console.WriteLine("  scan <path>         Scan a directory and show file statistics");
			Console.WriteLine("  duplicates <path>   Find duplicate files");
			Console.WriteLine("  recommend <path>    Get cleanup recommendations");
			Console.WriteLine("  export              Export scan results (requires GUI)");
			Console.WriteLine("  version             Show version information");
			Console.WriteLine("  help                Show this help message");
			Console.WriteLine("\nExamples:");
			Console.WriteLine("  FileSizeAnalyzerGUI scan C:\\Users\\MyFolder");
			Console.WriteLine("  FileSizeAnalyzerGUI scan C:\\Data --output report.txt --top 50");
			Console.WriteLine("  FileSizeAnalyzerGUI duplicates C:\\Photos --min-size 10485760");
			Console.WriteLine("  FileSizeAnalyzerGUI recommend C:\\Downloads");
			Console.WriteLine("\nFor GUI mode, run without arguments or double-click the executable.");
			return 0;
		}

		private int ShowVersion()
		{
			Console.WriteLine("FileSizeAnalyzer Pro");
			Console.WriteLine("Version: 3.0.0");
			Console.WriteLine("Open Source Edition");
			Console.WriteLine("https://github.com/chrisdfennell/FileSizeAnalyzerGUI");
			return 0;
		}

		private int ShowUnknownCommand(string command)
		{
			Console.Error.WriteLine($"Unknown command: {command}");
			Console.WriteLine("Run 'FileSizeAnalyzerGUI help' for usage information");
			return 1;
		}

		private string GetArgValue(string[] args, string argName, string defaultValue = "")
		{
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
				{
					return args[i + 1];
				}
			}
			return defaultValue;
		}

		private async Task SaveScanResults(ScanResult result, string outputFile)
		{
			var content = new StringBuilder();
			content.AppendLine($"Scan Results - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			content.AppendLine(new string('=', 80));
			var rootNode = result.RootNodes.FirstOrDefault();
			if (rootNode != null)
			{
				content.AppendLine($"Path: {rootNode.FullPath}");
			}
			content.AppendLine($"Total Files: {result.AllFiles.Count(f => !f.IsDirectory):N0}");
			content.AppendLine($"Total Directories: {result.AllFiles.Count(f => f.IsDirectory):N0}");
			if (rootNode != null)
			{
				content.AppendLine($"Total Size: {FormatSize(rootNode.Size)}");
			}
			content.AppendLine();

			var largestFiles = result.AllFiles
				.Where(f => !f.IsDirectory)
				.OrderByDescending(f => f.Size)
				.Take(100);

			content.AppendLine("Largest Files:");
			content.AppendLine(new string('-', 80));
			foreach (var file in largestFiles)
			{
				content.AppendLine($"{FormatSize(file.Size),12} | {file.FullPath}");
			}

			await File.WriteAllTextAsync(outputFile, content.ToString());
		}

		private async Task SaveDuplicateResults(List<DuplicateSet> duplicates, string outputFile)
		{
			var content = new StringBuilder();
			content.AppendLine($"Duplicate Files Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			content.AppendLine(new string('=', 80));

			foreach (var dup in duplicates)
			{
				var size = dup.Files.FirstOrDefault()?.Size ?? 0;
				var wasted = (dup.Files.Count - 1) * size;
				content.AppendLine($"\n{dup.FileName} - {FormatSize(size)} x {dup.Files.Count} = {FormatSize(wasted)} wasted");
				content.AppendLine(new string('-', 80));

				foreach (var file in dup.Files)
				{
					content.AppendLine($"  {file.FullPath}");
				}
			}

			await File.WriteAllTextAsync(outputFile, content.ToString());
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
