using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileSizeAnalyzerGUI.Services
{
	public class SearchCriteria
	{
		public string? NamePattern { get; set; }
		public bool UseRegex { get; set; }
		public bool CaseSensitive { get; set; }
		public long? MinSize { get; set; }
		public long? MaxSize { get; set; }
		public DateTime? ModifiedAfter { get; set; }
		public DateTime? ModifiedBefore { get; set; }
		public DateTime? CreatedAfter { get; set; }
		public DateTime? CreatedBefore { get; set; }
		public string? Extension { get; set; }
		public bool? IsDirectory { get; set; }
		public string? PathContains { get; set; }
	}

	public class SavedSearch
	{
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public SearchCriteria Criteria { get; set; } = new SearchCriteria();
		public DateTime SavedDate { get; set; } = DateTime.Now;
	}

	public class SearchService
	{
		private readonly List<SavedSearch> _savedSearches = new();
		private readonly Queue<SearchCriteria> _searchHistory = new();
		private const int MaxHistorySize = 20;

		public List<FileSystemNode> Search(List<FileSystemNode> files, SearchCriteria criteria)
		{
			var results = files.AsEnumerable();

			// Name/pattern matching
			if (!string.IsNullOrWhiteSpace(criteria.NamePattern))
			{
				if (criteria.UseRegex)
				{
					try
					{
						var regexOptions = criteria.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
						var regex = new Regex(criteria.NamePattern, regexOptions);
						results = results.Where(f => regex.IsMatch(System.IO.Path.GetFileName(f.FullPath)));
					}
					catch (ArgumentException)
					{
						// Invalid regex - treat as literal
						results = ApplyLiteralSearch(results, criteria.NamePattern, criteria.CaseSensitive);
					}
				}
				else
				{
					// Wildcard search
					results = ApplyWildcardSearch(results, criteria.NamePattern, criteria.CaseSensitive);
				}
			}

			// Size filters
			if (criteria.MinSize.HasValue)
			{
				results = results.Where(f => f.Size >= criteria.MinSize.Value);
			}

			if (criteria.MaxSize.HasValue)
			{
				results = results.Where(f => f.Size <= criteria.MaxSize.Value);
			}

			// Date filters - Modified
			if (criteria.ModifiedAfter.HasValue)
			{
				results = results.Where(f => f.LastWriteTime >= criteria.ModifiedAfter.Value);
			}

			if (criteria.ModifiedBefore.HasValue)
			{
				results = results.Where(f => f.LastWriteTime <= criteria.ModifiedBefore.Value);
			}

			// Date filters - Created
			if (criteria.CreatedAfter.HasValue)
			{
				results = results.Where(f => f.CreationTime >= criteria.CreatedAfter.Value);
			}

			if (criteria.CreatedBefore.HasValue)
			{
				results = results.Where(f => f.CreationTime <= criteria.CreatedBefore.Value);
			}

			// Extension filter
			if (!string.IsNullOrWhiteSpace(criteria.Extension))
			{
				var ext = criteria.Extension.StartsWith(".") ? criteria.Extension : "." + criteria.Extension;
				var comparison = criteria.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
				results = results.Where(f => string.Equals(f.Extension, ext, comparison));
			}

			// Type filter
			if (criteria.IsDirectory.HasValue)
			{
				results = results.Where(f => f.IsDirectory == criteria.IsDirectory.Value);
			}

			// Path contains
			if (!string.IsNullOrWhiteSpace(criteria.PathContains))
			{
				var comparison = criteria.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
				results = results.Where(f => f.FullPath.Contains(criteria.PathContains, comparison));
			}

			return results.ToList();
		}

		private IEnumerable<FileSystemNode> ApplyLiteralSearch(IEnumerable<FileSystemNode> files, string pattern, bool caseSensitive)
		{
			var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			return files.Where(f => System.IO.Path.GetFileName(f.FullPath).Contains(pattern, comparison));
		}

		private IEnumerable<FileSystemNode> ApplyWildcardSearch(IEnumerable<FileSystemNode> files, string pattern, bool caseSensitive)
		{
			// Convert wildcard pattern to regex
			// * becomes .* and ? becomes .
			var regexPattern = "^" + Regex.Escape(pattern)
				.Replace("\\*", ".*")
				.Replace("\\?", ".") + "$";

			var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
			var regex = new Regex(regexPattern, regexOptions);

			return files.Where(f => regex.IsMatch(System.IO.Path.GetFileName(f.FullPath)));
		}

		public void AddToHistory(SearchCriteria criteria)
		{
			// Remove duplicates and add to front
			_searchHistory.Enqueue(criteria);

			while (_searchHistory.Count > MaxHistorySize)
			{
				_searchHistory.Dequeue();
			}
		}

		public List<SearchCriteria> GetSearchHistory()
		{
			return _searchHistory.Reverse().ToList();
		}

		public void SaveSearch(SavedSearch search)
		{
			// Remove existing search with same name
			_savedSearches.RemoveAll(s => s.Name.Equals(search.Name, StringComparison.OrdinalIgnoreCase));
			_savedSearches.Add(search);
		}

		public void DeleteSavedSearch(string name)
		{
			_savedSearches.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		}

		public List<SavedSearch> GetSavedSearches()
		{
			return _savedSearches.OrderBy(s => s.Name).ToList();
		}

		public SavedSearch? GetSavedSearch(string name)
		{
			return _savedSearches.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		}

		// Quick search templates
		public static SearchCriteria CreateQuickSearch(string type)
		{
			return type switch
			{
				"LargeFiles" => new SearchCriteria { MinSize = 100 * 1024 * 1024 }, // 100MB+
				"HugeFiles" => new SearchCriteria { MinSize = 1024 * 1024 * 1024 }, // 1GB+
				"RecentFiles" => new SearchCriteria { ModifiedAfter = DateTime.Now.AddDays(-7) }, // Last 7 days
				"OldFiles" => new SearchCriteria { ModifiedBefore = DateTime.Now.AddYears(-1) }, // Older than 1 year
				"TodayFiles" => new SearchCriteria { ModifiedAfter = DateTime.Today },
				"VideosLarge" => new SearchCriteria
				{
					MinSize = 50 * 1024 * 1024,
					NamePattern = "*.mp4|*.avi|*.mkv|*.mov|*.wmv",
					UseRegex = false
				},
				"ImagesLarge" => new SearchCriteria
				{
					MinSize = 10 * 1024 * 1024,
					NamePattern = "*.jpg|*.jpeg|*.png|*.gif|*.bmp",
					UseRegex = false
				},
				_ => new SearchCriteria()
			};
		}

		public string GetSearchSummary(SearchCriteria criteria)
		{
			var parts = new List<string>();

			if (!string.IsNullOrWhiteSpace(criteria.NamePattern))
			{
				var type = criteria.UseRegex ? "regex" : "wildcard";
				parts.Add($"Name ({type}): {criteria.NamePattern}");
			}

			if (criteria.MinSize.HasValue || criteria.MaxSize.HasValue)
			{
				if (criteria.MinSize.HasValue && criteria.MaxSize.HasValue)
				{
					parts.Add($"Size: {FormatSize(criteria.MinSize.Value)} - {FormatSize(criteria.MaxSize.Value)}");
				}
				else if (criteria.MinSize.HasValue)
				{
					parts.Add($"Size: ≥ {FormatSize(criteria.MinSize.Value)}");
				}
				else
				{
					parts.Add($"Size: ≤ {FormatSize(criteria.MaxSize!.Value)}");
				}
			}

			if (criteria.ModifiedAfter.HasValue || criteria.ModifiedBefore.HasValue)
			{
				if (criteria.ModifiedAfter.HasValue && criteria.ModifiedBefore.HasValue)
				{
					parts.Add($"Modified: {criteria.ModifiedAfter.Value:yyyy-MM-dd} to {criteria.ModifiedBefore.Value:yyyy-MM-dd}");
				}
				else if (criteria.ModifiedAfter.HasValue)
				{
					parts.Add($"Modified: after {criteria.ModifiedAfter.Value:yyyy-MM-dd}");
				}
				else
				{
					parts.Add($"Modified: before {criteria.ModifiedBefore!.Value:yyyy-MM-dd}");
				}
			}

			if (!string.IsNullOrWhiteSpace(criteria.Extension))
			{
				parts.Add($"Extension: {criteria.Extension}");
			}

			if (criteria.IsDirectory.HasValue)
			{
				parts.Add(criteria.IsDirectory.Value ? "Type: Folders only" : "Type: Files only");
			}

			if (!string.IsNullOrWhiteSpace(criteria.PathContains))
			{
				parts.Add($"Path contains: {criteria.PathContains}");
			}

			return parts.Any() ? string.Join(" | ", parts) : "No criteria";
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
