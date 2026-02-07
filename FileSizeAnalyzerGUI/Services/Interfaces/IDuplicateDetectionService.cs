using System;
using System.Collections.Generic;
using System.Threading;

namespace FileSizeAnalyzerGUI.Services.Interfaces
{
	public interface IDuplicateDetectionService
	{
		void FindDuplicates(
			List<FileSystemNode> files,
			IProgress<DuplicateSet> progress,
			CancellationToken token,
			DuplicateDetectionOptions options);
	}
}
