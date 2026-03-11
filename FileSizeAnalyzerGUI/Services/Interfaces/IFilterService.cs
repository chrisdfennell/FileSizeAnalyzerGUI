using System.Collections.Generic;

namespace FileSizeAnalyzerGUI.Services.Interfaces
{
	public interface IFilterService
	{
		IEnumerable<FileSystemNode> ApplyFilters(
			IEnumerable<FileSystemNode> files,
			FilterCriteria criteria);
	}
}
