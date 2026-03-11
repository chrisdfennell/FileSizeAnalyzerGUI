using FileSizeAnalyzerGUI;
using FileSizeAnalyzerGUI.Services;
using FileSizeAnalyzerGUI.Services.Interfaces;
using FileSizeAnalyzerGUI.ViewModels;
using Moq;
using System.Collections.ObjectModel;

namespace FileSizeAnalyzerGUI.Tests;

public class MainViewModelTests
{
    private readonly Mock<IFileSystemScannerService> _scannerService = new();
    private readonly Mock<IDuplicateDetectionService> _duplicateService = new();
    private readonly Mock<IFilterService> _filterService = new();
    private readonly Mock<IAnalysisService> _analysisService = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly HashCache _hashCache = new();
    private readonly ScanMetadataService _metadataService = new();
    private readonly TemporaryFilesService _tempFilesService = new();
    private readonly AdvancedAnalysisService _advancedAnalysisService = new();
    private readonly ExportService _exportService = new();
    private readonly SearchService _searchService = new();
    private readonly TrendsService _trendsService;
    private readonly FilePreviewService _previewService = new();
    private readonly CleanupRecommendationService _cleanupRecommendationService;
    private readonly FileOperationsService _fileOperationsService;
    private readonly DashboardService _dashboardService;
    private readonly DuplicateManagementService _duplicateManagementService = new();

    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _trendsService = new TrendsService(_metadataService);
        _fileOperationsService = new FileOperationsService(_logger.Object);
        _cleanupRecommendationService = new CleanupRecommendationService(_advancedAnalysisService, _tempFilesService);
        _dashboardService = new DashboardService(_metadataService);

        _viewModel = new MainViewModel(
            _scannerService.Object,
            _duplicateService.Object,
            _filterService.Object,
            _analysisService.Object,
            _hashCache,
            _logger.Object,
            _metadataService,
            _tempFilesService,
            _advancedAnalysisService,
            _exportService,
            _searchService,
            _trendsService,
            _previewService,
            _cleanupRecommendationService,
            _fileOperationsService,
            _dashboardService,
            _duplicateManagementService);
    }

    [Fact]
    public void Constructor_InitializesEmptyCollections()
    {
        Assert.NotNull(_viewModel.RootNodes);
        Assert.NotNull(_viewModel.AllFiles);
        Assert.NotNull(_viewModel.SelectedFiles);
        Assert.NotNull(_viewModel.Duplicates);
        Assert.NotNull(_viewModel.FileTypes);
        Assert.NotNull(_viewModel.LargestFiles);
        Assert.NotNull(_viewModel.EmptyFolders);
        Assert.NotNull(_viewModel.SearchResults);
        Assert.NotNull(_viewModel.CleanupRecommendations);
        Assert.Empty(_viewModel.RootNodes);
        Assert.Empty(_viewModel.AllFiles);
    }

    [Fact]
    public void Constructor_InitializesDefaultState()
    {
        Assert.False(_viewModel.IsScanning);
        Assert.True(_viewModel.IsNotScanning);
        Assert.True(_viewModel.SkipSystemFiles);
        Assert.True(_viewModel.SkipWindowsDirectory);
        Assert.Equal(string.Empty, _viewModel.ScanPath);
        Assert.Equal(string.Empty, _viewModel.ProgressText);
        Assert.Equal(0, _viewModel.ProgressValue);
    }

    [Fact]
    public void Constructor_InitializesCommands()
    {
        Assert.NotNull(_viewModel.ScanCommand);
        Assert.NotNull(_viewModel.StopScanCommand);
        Assert.NotNull(_viewModel.SearchCommand);
        Assert.NotNull(_viewModel.ClearSearchCommand);
        Assert.NotNull(_viewModel.GenerateRecommendationsCommand);
        Assert.NotNull(_viewModel.RefreshDashboardCommand);
        Assert.NotNull(_viewModel.RefreshTrendsCommand);
        Assert.NotNull(_viewModel.AutoSelectKeepNewestCommand);
        Assert.NotNull(_viewModel.AutoSelectKeepOldestCommand);
        Assert.NotNull(_viewModel.AutoSelectKeepLargestCommand);
        Assert.NotNull(_viewModel.AutoSelectKeepSmallestCommand);
    }

    [Fact]
    public void ScanPath_SetProperty_RaisesPropertyChanged()
    {
        var raised = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ScanPath)) raised = true;
        };

        _viewModel.ScanPath = @"C:\Test";

        Assert.True(raised);
        Assert.Equal(@"C:\Test", _viewModel.ScanPath);
    }

    [Fact]
    public void IsScanning_SetTrue_UpdatesIsNotScanning()
    {
        var changedProps = new List<string>();
        _viewModel.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        _viewModel.IsScanning = true;

        Assert.True(_viewModel.IsScanning);
        Assert.False(_viewModel.IsNotScanning);
        Assert.Contains(nameof(MainViewModel.IsScanning), changedProps);
        Assert.Contains(nameof(MainViewModel.IsNotScanning), changedProps);
    }

    [Fact]
    public async Task RunScanAsync_InvalidPath_SetsReportText()
    {
        _viewModel.ScanPath = @"Z:\NonExistent\Path\12345";

        await _viewModel.RunScanAsync();

        Assert.Contains("does not exist", _viewModel.ReportText);
        _logger.Verify(l => l.Warning(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void CancelScan_DoesNotThrow_WhenNotScanning()
    {
        var ex = Record.Exception(() => _viewModel.CancelScan());
        Assert.Null(ex);
    }

    [Fact]
    public void FormatSize_FormatsCorrectly()
    {
        Assert.Equal("0 bytes", MainViewModel.FormatSize(-1));
        Assert.Equal("0 B", MainViewModel.FormatSize(0));
        Assert.Equal("512 B", MainViewModel.FormatSize(512));
        Assert.Equal("1 KB", MainViewModel.FormatSize(1024));
        Assert.Equal("1.5 KB", MainViewModel.FormatSize(1536));
        Assert.Equal("1 MB", MainViewModel.FormatSize(1048576));
        Assert.Equal("1 GB", MainViewModel.FormatSize(1073741824));
        Assert.Equal("1 TB", MainViewModel.FormatSize(1099511627776));
    }

    [Fact]
    public void ApplyFilters_NullCriteria_ShowsAllFiles()
    {
        var file1 = new FileSystemNode { FullPath = "test1.txt", Size = 100 };
        var file2 = new FileSystemNode { FullPath = "test2.txt", Size = 200 };
        _viewModel.AllFiles.Add(file1);
        _viewModel.AllFiles.Add(file2);

        _viewModel.ApplyFilters(null);

        Assert.Equal(2, _viewModel.SelectedFiles.Count);
    }

    [Fact]
    public void ApplyFilters_WithCriteria_DelegatesToFilterService()
    {
        var file1 = new FileSystemNode { FullPath = "test1.txt", Size = 100 };
        _viewModel.AllFiles.Add(file1);

        var criteria = new FilterCriteria { SizeFilter = "All Files" };
        _filterService.Setup(f => f.ApplyFilters(It.IsAny<IEnumerable<FileSystemNode>>(), criteria))
            .Returns(new[] { file1 });

        _viewModel.ApplyFilters(criteria);

        Assert.Single(_viewModel.SelectedFiles);
        _filterService.Verify(f => f.ApplyFilters(It.IsAny<IEnumerable<FileSystemNode>>(), criteria), Times.Once);
    }

    [Fact]
    public void ExecuteSearch_NoFiles_SetsStatusMessage()
    {
        var criteria = new SearchCriteria { NamePattern = "test" };

        _viewModel.ExecuteSearch(criteria);

        Assert.Contains("scan first", _viewModel.SearchStatus);
    }

    [Fact]
    public void ClearSearch_ClearsResultsAndSetsStatus()
    {
        _viewModel.SearchResults.Add(new FileSystemNode { FullPath = "test.txt" });

        _viewModel.ClearSearch();

        Assert.Empty(_viewModel.SearchResults);
        Assert.Equal("Search cleared", _viewModel.SearchStatus);
    }

    [Fact]
    public void GetSelectedItemsForDeletion_CollectsFromAllSources()
    {
        var file1 = new FileSystemNode { FullPath = "allfiles.txt", IsSelected = true };
        var file2 = new FileSystemNode { FullPath = "duplicate.txt", IsSelected = true };
        var folder = new FileSystemNode { FullPath = "empty", IsDirectory = true, IsSelected = true };
        var unselected = new FileSystemNode { FullPath = "skip.txt", IsSelected = false };

        _viewModel.AllFiles.Add(file1);
        _viewModel.AllFiles.Add(unselected);
        _viewModel.Duplicates.Add(new DuplicateSet
        {
            FileName = "dup",
            Files = new ObservableCollection<FileSystemNode> { file2 }
        });
        _viewModel.EmptyFolders.Add(folder);

        var result = _viewModel.GetSelectedItemsForDeletion();

        Assert.Equal(3, result.Count);
        Assert.Contains(file1, result);
        Assert.Contains(file2, result);
        Assert.Contains(folder, result);
        Assert.DoesNotContain(unselected, result);
    }

    [Fact]
    public void ApplyAutoSelectForGroup_SelectsDuplicatesExceptKeep()
    {
        var keep = new FileSystemNode { FullPath = "keep.txt", LastWriteTime = DateTime.Now };
        var delete1 = new FileSystemNode { FullPath = "old1.txt", LastWriteTime = DateTime.Now.AddDays(-10) };
        var delete2 = new FileSystemNode { FullPath = "old2.txt", LastWriteTime = DateTime.Now.AddDays(-20) };

        var group = new DuplicateSet
        {
            FileName = "test",
            Files = new ObservableCollection<FileSystemNode> { keep, delete1, delete2 }
        };

        _viewModel.ApplyAutoSelectForGroup(group, g => g.Files.OrderByDescending(f => f.LastWriteTime).First());

        Assert.False(keep.IsSelected);
        Assert.True(delete1.IsSelected);
        Assert.True(delete2.IsSelected);
    }

    [Fact]
    public void ClearGroupSelection_UnselectsAllFiles()
    {
        var file1 = new FileSystemNode { FullPath = "f1.txt", IsSelected = true };
        var file2 = new FileSystemNode { FullPath = "f2.txt", IsSelected = true };

        var group = new DuplicateSet
        {
            FileName = "test",
            Files = new ObservableCollection<FileSystemNode> { file1, file2 }
        };

        _viewModel.ClearGroupSelection(group);

        Assert.False(file1.IsSelected);
        Assert.False(file2.IsSelected);
    }

    [Fact]
    public void RemoveDeletedFilesFromDuplicates_RemovesAndCleans()
    {
        var file1 = new FileSystemNode { FullPath = "keep.txt" };
        var file2 = new FileSystemNode { FullPath = "delete.txt" };

        var dupSet = new DuplicateSet
        {
            FileName = "test",
            Files = new ObservableCollection<FileSystemNode> { file1, file2 }
        };

        _viewModel.AllFiles.Add(file1);
        _viewModel.AllFiles.Add(file2);
        _viewModel.Duplicates.Add(dupSet);

        _viewModel.RemoveDeletedFilesFromDuplicates(new List<FileSystemNode> { file2 });

        Assert.Single(_viewModel.AllFiles);
        Assert.DoesNotContain(file2, _viewModel.AllFiles);
        // Group removed because only 1 file left (< 2)
        Assert.Empty(_viewModel.Duplicates);
    }

    [Fact]
    public void UpdateDuplicateStats_NoDuplicates_ShowsDefault()
    {
        _viewModel.UpdateDuplicateStats();
        Assert.Equal("No duplicates found", _viewModel.DuplicateStatsText);
    }

    [Fact]
    public void GenerateRecommendations_NoFiles_SetsMessage()
    {
        _viewModel.GenerateRecommendations();
        Assert.Contains("scan first", _viewModel.RecommendationSummary);
    }

    [Fact]
    public void RefreshDashboard_NoData_SetsMessage()
    {
        _viewModel.RefreshDashboard();
        Assert.Contains("scan first", _viewModel.DashboardSummary);
    }

    [Fact]
    public void RefreshTrends_NoData_SetsMessage()
    {
        _viewModel.RefreshTrends();
        Assert.Contains("scan first", _viewModel.TrendsSummary);
    }

    [Fact]
    public void SortAllNodesBySize_SortsDescending()
    {
        var root = new FileSystemNode
        {
            FullPath = "root",
            IsDirectory = true,
            Children = new ObservableCollection<FileSystemNode>
            {
                new FileSystemNode { FullPath = "small.txt", Size = 100 },
                new FileSystemNode { FullPath = "large.txt", Size = 1000 },
                new FileSystemNode { FullPath = "medium.txt", Size = 500 }
            }
        };

        _viewModel.SortAllNodesBySize(root);

        Assert.Equal(1000, root.Children[0].Size);
        Assert.Equal(500, root.Children[1].Size);
        Assert.Equal(100, root.Children[2].Size);
    }

    [Fact]
    public void GetAllNodes_TraversesTree()
    {
        var child = new FileSystemNode
        {
            FullPath = "child",
            IsDirectory = true,
            Children = new ObservableCollection<FileSystemNode>
            {
                new FileSystemNode { FullPath = "grandchild.txt" }
            }
        };

        var root = new FileSystemNode
        {
            FullPath = "root",
            IsDirectory = true,
            Children = new ObservableCollection<FileSystemNode> { child }
        };

        var allNodes = _viewModel.GetAllNodes(root).ToList();

        Assert.Equal(3, allNodes.Count);
    }

    [Fact]
    public void AvailableDrives_IsPopulated()
    {
        // Should have at least one drive on any Windows system
        Assert.NotNull(_viewModel.AvailableDrives);
        Assert.NotEmpty(_viewModel.AvailableDrives);
    }
}
