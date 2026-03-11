using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileSizeAnalyzerGUI.Services.Interfaces
{
	public interface IFileSystemScannerService
	{
		Task<ScanResult> ScanDirectoryAsync(
			string scanPath,
			ScanOptions options,
			IProgress<FileSystemNode> nodeProgress,
			IProgress<string> textProgress,
			IProgress<double> percentProgress,
			CancellationToken token);

		void FindEmptyFolders(
			FileSystemNode root,
			IProgress<FileSystemNode> progress,
			CancellationToken token);
	}
}
