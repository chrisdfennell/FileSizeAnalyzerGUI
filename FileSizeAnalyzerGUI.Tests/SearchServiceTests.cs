using FileSizeAnalyzerGUI;
using FileSizeAnalyzerGUI.Services;

namespace FileSizeAnalyzerGUI.Tests;

public class SearchServiceTests
{
    private readonly SearchService _service = new();

    private List<FileSystemNode> CreateTestFiles() => new()
    {
        new FileSystemNode { FullPath = @"C:\docs\report.pdf", Size = 5 * 1024 * 1024, LastWriteTime = DateTime.Now.AddDays(-5) },
        new FileSystemNode { FullPath = @"C:\docs\README.md", Size = 2048, LastWriteTime = DateTime.Now.AddDays(-1) },
        new FileSystemNode { FullPath = @"C:\photos\vacation_photo.jpg", Size = 8 * 1024 * 1024, LastWriteTime = DateTime.Now.AddMonths(-3) },
        new FileSystemNode { FullPath = @"C:\code\Program.cs", Size = 4096, LastWriteTime = DateTime.Now },
        new FileSystemNode { FullPath = @"C:\code\report_generator.py", Size = 8192, LastWriteTime = DateTime.Now.AddDays(-10) },
    };

    [Fact]
    public void Search_ByName_FindsMatches()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { NamePattern = "*report*" };

        var results = _service.Search(files, criteria);

        Assert.Equal(2, results.Count); // report.pdf and report_generator.py
    }

    [Fact]
    public void Search_ByName_CaseInsensitiveByDefault()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { NamePattern = "*README*" };

        var results = _service.Search(files, criteria);

        Assert.Single(results);
        Assert.Contains("README.md", results[0].FullPath);
    }

    [Fact]
    public void Search_ByName_CaseSensitive()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { NamePattern = "*README*", CaseSensitive = true };

        var results = _service.Search(files, criteria);

        Assert.Single(results);
    }

    [Fact]
    public void Search_ByExtension_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { Extension = ".cs" };

        var results = _service.Search(files, criteria);

        Assert.Single(results);
        Assert.Contains("Program.cs", results[0].FullPath);
    }

    [Fact]
    public void Search_ByMinSize_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { MinSize = 5 * 1024 * 1024 };

        var results = _service.Search(files, criteria);

        Assert.All(results, f => Assert.True(f.Size >= 5 * 1024 * 1024));
    }

    [Fact]
    public void Search_ByMaxSize_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { MaxSize = 10000 };

        var results = _service.Search(files, criteria);

        Assert.All(results, f => Assert.True(f.Size <= 10000));
    }

    [Fact]
    public void Search_ByPathContains_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { PathContains = "docs" };

        var results = _service.Search(files, criteria);

        Assert.Equal(2, results.Count);
        Assert.All(results, f => Assert.Contains("docs", f.FullPath));
    }

    [Fact]
    public void Search_ByModifiedAfter_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria { ModifiedAfter = DateTime.Now.AddDays(-2) };

        var results = _service.Search(files, criteria);

        Assert.All(results, f => Assert.True(f.LastWriteTime >= DateTime.Now.AddDays(-2)));
    }

    [Fact]
    public void Search_MultipleFilters_CombinesCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria
        {
            PathContains = "code",
            MaxSize = 5000
        };

        var results = _service.Search(files, criteria);

        Assert.Single(results);
        Assert.Contains("Program.cs", results[0].FullPath);
    }

    [Fact]
    public void Search_NoCriteria_ReturnsAll()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria();

        var results = _service.Search(files, criteria);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void Search_WithRegex_MatchesPattern()
    {
        var files = CreateTestFiles();
        var criteria = new SearchCriteria
        {
            NamePattern = @"\.cs$",
            UseRegex = true
        };

        var results = _service.Search(files, criteria);

        Assert.Single(results);
        Assert.Contains("Program.cs", results[0].FullPath);
    }

    [Fact]
    public void AddToHistory_StoresSearch()
    {
        var criteria = new SearchCriteria { NamePattern = "test" };
        _service.AddToHistory(criteria);

        var summary = _service.GetSearchSummary(criteria);
        Assert.NotNull(summary);
        Assert.NotEmpty(summary);
    }
}
