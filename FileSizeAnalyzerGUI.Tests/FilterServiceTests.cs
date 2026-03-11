using FileSizeAnalyzerGUI;
using FileSizeAnalyzerGUI.Services;

namespace FileSizeAnalyzerGUI.Tests;

public class FilterServiceTests
{
    private readonly FilterService _service = new();

    private List<FileSystemNode> CreateTestFiles() => new()
    {
        new FileSystemNode { FullPath = @"C:\docs\report.pdf", Size = 5 * 1024 * 1024, LastWriteTime = DateTime.Now.AddDays(-5) },
        new FileSystemNode { FullPath = @"C:\docs\readme.txt", Size = 1024, LastWriteTime = DateTime.Now.AddDays(-60) },
        new FileSystemNode { FullPath = @"C:\photos\photo.jpg", Size = 200 * 1024 * 1024, LastWriteTime = DateTime.Now.AddMonths(-8) },
        new FileSystemNode { FullPath = @"C:\videos\movie.mp4", Size = 2L * 1024 * 1024 * 1024, LastWriteTime = DateTime.Now.AddYears(-2) },
        new FileSystemNode { FullPath = @"C:\code\app.cs", Size = 512, LastWriteTime = DateTime.Now },
    };

    [Fact]
    public void ApplyFilters_AllFiles_ReturnsAll()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria { SizeFilter = "All Files" };

        var result = _service.ApplyFilters(files, criteria).ToList();

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ApplyFilters_ByExtension_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria
        {
            SizeFilter = "All Files",
            Extensions = new[] { ".pdf", ".txt" }
        };

        var result = _service.ApplyFilters(files, criteria).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.True(
            f.FullPath.EndsWith(".pdf") || f.FullPath.EndsWith(".txt")));
    }

    [Fact]
    public void ApplyFilters_LargerThan100MB_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria { SizeFilter = "> 100MB" };

        var result = _service.ApplyFilters(files, criteria).ToList();

        Assert.All(result, f => Assert.True(f.Size > 100 * 1024 * 1024));
    }

    [Fact]
    public void ApplyFilters_LastMonth_FiltersCorrectly()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria { DateFilter = "Last Month" };

        var result = _service.ApplyFilters(files, criteria).ToList();

        var oneMonthAgo = DateTime.Now.AddMonths(-1);
        Assert.All(result, f => Assert.True(f.LastWriteTime >= oneMonthAgo));
    }

    [Fact]
    public void ApplyFilters_EmptyExtensions_IgnoresExtensionFilter()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria
        {
            SizeFilter = "All Files",
            Extensions = Array.Empty<string>()
        };

        var result = _service.ApplyFilters(files, criteria).ToList();

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ApplyFilters_NullCriteria_Values_ReturnsAll()
    {
        var files = CreateTestFiles();
        var criteria = new FilterCriteria();

        var result = _service.ApplyFilters(files, criteria).ToList();

        Assert.Equal(5, result.Count);
    }
}
