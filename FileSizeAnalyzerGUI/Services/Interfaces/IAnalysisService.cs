using System;
using System.Collections.Generic;
using System.Threading;

namespace FileSizeAnalyzerGUI.Services.Interfaces
{
	public interface IAnalysisService
	{
		void GetFileTypeStats(
			List<FileSystemNode> files,
			IProgress<FileTypeStats> progress,
			CancellationToken token);

		void GetFileAgeStats(
			List<FileSystemNode> files,
			IProgress<FileAgeStats> progress,
			CancellationToken token);
	}
}
