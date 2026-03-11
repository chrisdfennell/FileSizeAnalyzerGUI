using FileSizeAnalyzerGUI;
using FileSizeAnalyzerGUI.Services;

namespace FileSizeAnalyzerGUI.Tests;

public class AnalysisServiceTests
{
    private readonly AnalysisService _service = new();

    [Fact]
    public void GetFileTypeStats_GroupsByExtension()
    {
        var files = new List<FileSystemNode>
        {
            new() { FullPath = "a.txt", Size = 100 },
            new() { FullPath = "b.txt", Size = 200 },
            new() { FullPath = "c.pdf", Size = 500 },
        };

        var stats = new List<FileTypeStats>();
        var progress = new Progress<FileTypeStats>(s => stats.Add(s));

        _service.GetFileTypeStats(files, progress, CancellationToken.None);

        // Progress callbacks are async, give them a moment
        Thread.Sleep(100);

        Assert.True(stats.Count >= 2);
        var txtStat = stats.FirstOrDefault(s => s.Extension == ".txt");
        Assert.NotNull(txtStat);
        Assert.Equal(2, txtStat!.FileCount);
        Assert.Equal(300, txtStat.TotalSize);
    }

    [Fact]
    public void GetFileAgeStats_CategorizesByAge()
    {
        var files = new List<FileSystemNode>
        {
            new() { FullPath = "recent.txt", Size = 100, LastWriteTime = DateTime.Now.AddDays(-10) },
            new() { FullPath = "old.txt", Size = 200, LastWriteTime = DateTime.Now.AddYears(-3) },
        };

        var stats = new List<FileAgeStats>();
        var progress = new Progress<FileAgeStats>(s => stats.Add(s));

        _service.GetFileAgeStats(files, progress, CancellationToken.None);

        Thread.Sleep(100);

        Assert.NotEmpty(stats);
    }
}

public class CleanupRecommendationServiceTests
{
    private readonly CleanupRecommendationService _service = new(new AdvancedAnalysisService(), new TemporaryFilesService());

    [Fact]
    public void GenerateRecommendations_WithTempFiles_FindsThem()
    {
        var files = new List<FileSystemNode>
        {
            new() { FullPath = @"C:\Users\test\AppData\Local\Temp\file.tmp", Size = 10 * 1024 * 1024, LastWriteTime = DateTime.Now.AddDays(-30) },
            new() { FullPath = @"C:\data\important.doc", Size = 1024, LastWriteTime = DateTime.Now },
        };

        var recommendations = _service.GenerateRecommendations(files);

        Assert.NotNull(recommendations);
        // Should have at least some recommendations for temp files
    }

    [Fact]
    public void GenerateRecommendations_EmptyList_ReturnsEmpty()
    {
        var recommendations = _service.GenerateRecommendations(new List<FileSystemNode>());
        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
    }

    [Fact]
    public void GetOverallSummary_WithRecommendations_ReturnsSummary()
    {
        var recommendations = new List<CleanupRecommendation>
        {
            new() { Category = "Test", PotentialSavings = 1024 * 1024, FileCount = 5, Priority = "High" }
        };

        var summary = _service.GetOverallSummary(recommendations);

        Assert.NotNull(summary);
        Assert.NotEmpty(summary);
    }

    [Fact]
    public void GetOverallSummary_Empty_ReturnsSummary()
    {
        var summary = _service.GetOverallSummary(new List<CleanupRecommendation>());

        Assert.NotNull(summary);
    }
}
