using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class AnalysisService : IAnalysisService
	{
		public void GetFileTypeStats(List<FileSystemNode> files, IProgress<FileTypeStats> progress, CancellationToken token)
		{
			if (files == null) return;

			var stats = files.GroupBy(f => f.Extension)
				 .Select(g => new FileTypeStats
				 {
					 Extension = string.IsNullOrEmpty(g.Key) ? "No Extension" : g.Key,
					 TotalSize = g.Sum(f => f.Size),
					 FileCount = g.Count()
				 }).OrderByDescending(s => s.TotalSize);

			foreach (var stat in stats)
			{
				if (token.IsCancellationRequested) return;
				progress.Report(stat);
			}
		}

		public void GetFileAgeStats(List<FileSystemNode> files, IProgress<FileAgeStats> progress, CancellationToken token)
		{
			if (files == null) return;
			var now = DateTime.Now;
			var stats = new Dictionary<string, FileAgeStats>
			{
				{ "Last Month", new FileAgeStats { Category = "Last Month" } },
				{ "Last Year", new FileAgeStats { Category = "Last Year" } },
				{ "Older Than 1 Year", new FileAgeStats { Category = "Older Than 1 Year" } }
			};

			foreach (var file in files)
			{
				if (token.IsCancellationRequested) return;
				if (file.LastWriteTime >= now.AddMonths(-1))
				{
					stats["Last Month"].AddFile(file.Size);
				}
				else if (file.LastWriteTime >= now.AddYears(-1))
				{
					stats["Last Year"].AddFile(file.Size);
				}
				else
				{
					stats["Older Than 1 Year"].AddFile(file.Size);
				}
			}

			foreach (var stat in stats.Values.Where(s => s.FileCount > 0))
			{
				if (token.IsCancellationRequested) return;
				progress.Report(stat);
			}
		}
	}
}
