using System;
using System.Collections.Generic;
using System.Linq;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class FilterCriteria
	{
		public string? SizeFilter { get; set; }
		public string[]? Extensions { get; set; }
		public string? DateFilter { get; set; }
		public DateTime? StartDate { get; set; }
		public DateTime? EndDate { get; set; }
	}

	public class FilterService : IFilterService
	{
		public IEnumerable<FileSystemNode> ApplyFilters(IEnumerable<FileSystemNode> files, FilterCriteria criteria)
		{
			var filteredFiles = files;

			if (!string.IsNullOrEmpty(criteria.SizeFilter) && criteria.SizeFilter != "All Sizes")
			{
				long minSize = criteria.SizeFilter switch
				{
					"> 1MB" => 1_048_576,
					"> 10MB" => 10_485_760,
					"> 100MB" => 104_857_600,
					"> 500MB" => 524_288_000,
					"> 1GB" => 1_073_741_824,
					"> 5GB" => 5_368_709_120,
					"> 10GB" => 10_737_418_240,
					_ => 0
				};
				filteredFiles = filteredFiles.Where(f => f.Size >= minSize);
			}

			if (criteria.Extensions != null && criteria.Extensions.Any())
			{
				filteredFiles = filteredFiles.Where(f => criteria.Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));
			}

			if (!string.IsNullOrEmpty(criteria.DateFilter) && criteria.DateFilter != "All Dates")
			{
				var now = DateTime.Now;
				filteredFiles = criteria.DateFilter switch
				{
					"Last Month" => filteredFiles.Where(f => f.LastWriteTime >= now.AddMonths(-1)),
					"Last Year" => filteredFiles.Where(f => f.LastWriteTime >= now.AddYears(-1)),
					"Older Than 1 Year" => filteredFiles.Where(f => f.LastWriteTime < now.AddYears(-1)),
					"Custom Range" when criteria.StartDate.HasValue && criteria.EndDate.HasValue =>
						filteredFiles.Where(f => f.LastWriteTime >= criteria.StartDate.Value && f.LastWriteTime <= criteria.EndDate.Value),
					_ => filteredFiles
				};
			}

			return filteredFiles;
		}
	}
}
