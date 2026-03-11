using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileSizeAnalyzerGUI.Services;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // Services
        private readonly IFileSystemScannerService _scannerService;
        private readonly IDuplicateDetectionService _duplicateService;
        private readonly IFilterService _filterService;
        private readonly IAnalysisService _analysisService;
        private readonly ILogger _logger;
        private readonly ScanMetadataService _metadataService;
        private readonly TemporaryFilesService _tempFilesService;
        private readonly AdvancedAnalysisService _advancedAnalysisService;
        private readonly ExportService _exportService;
        private readonly SearchService _searchService;
        private readonly TrendsService _trendsService;
        private readonly FilePreviewService _previewService;
        private readonly CleanupRecommendationService _cleanupRecommendationService;
        private readonly FileOperationsService _fileOperationsService;
        private readonly DashboardService _dashboardService;
        private readonly DuplicateManagementService _duplicateManagementService;
        private readonly HashCache _hashCache;

        // Cancellation
        private CancellationTokenSource? _cts;

        // State
        private readonly StringBuilder _scanErrors = new();
        private List<string> _exclusionList = new();
        private long _minDupSizeBytes = Constants.DuplicateDetection.DefaultMinDupSizeBytes;
        private bool _verifyByteByByte = Constants.DuplicateDetection.DefaultVerifyByteByByte;
        private string _lastAutoSelectRuleDescription = "None";

        // Observable Collections
        public ObservableCollection<FileSystemNode> RootNodes { get; } = new();
        public ObservableCollection<FileSystemNode> AllFiles { get; } = new();
        public ObservableCollection<FileSystemNode> SelectedFiles { get; } = new();
        public ObservableCollection<DuplicateSet> Duplicates { get; } = new();
        public ObservableCollection<FileTypeStats> FileTypes { get; } = new();
        public ObservableCollection<FileAgeStats> FileAgeStats { get; } = new();
        public ObservableCollection<FileSystemNode> LargestFiles { get; } = new();
        public ObservableCollection<FileSystemNode> EmptyFolders { get; } = new();
        public ObservableCollection<ScanHistoryEntry> ScanHistory { get; } = new();
        public ObservableCollection<TemporaryFileCategory> TemporaryFileCategories { get; } = new();
        public ObservableCollection<StaleFileInfo> StaleFiles { get; } = new();
        public ObservableCollection<LargeRarelyUsedFile> LargeRarelyUsedFiles { get; } = new();
        public ObservableCollection<FileSystemNode> SearchResults { get; } = new();
        public ObservableCollection<CleanupRecommendation> CleanupRecommendations { get; } = new();

        // Bindable properties
        private string _scanPath = string.Empty;
        public string ScanPath
        {
            get => _scanPath;
            set => SetProperty(ref _scanPath, value);
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged(nameof(IsNotScanning));
                }
            }
        }
        public bool IsNotScanning => !_isScanning;

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private string _reportText = string.Empty;
        public string ReportText
        {
            get => _reportText;
            set => SetProperty(ref _reportText, value);
        }

        private string _duplicateStatsText = "No duplicates found";
        public string DuplicateStatsText
        {
            get => _duplicateStatsText;
            set => SetProperty(ref _duplicateStatsText, value);
        }

        private string _previewText = string.Empty;
        public string PreviewText
        {
            get => _previewText;
            set => SetProperty(ref _previewText, value);
        }

        private string _previewMessage = string.Empty;
        public string PreviewMessage
        {
            get => _previewMessage;
            set => SetProperty(ref _previewMessage, value);
        }

        private bool _previewTextVisible;
        public bool PreviewTextVisible
        {
            get => _previewTextVisible;
            set => SetProperty(ref _previewTextVisible, value);
        }

        private ImageSource? _previewImageSource;
        public ImageSource? PreviewImageSource
        {
            get => _previewImageSource;
            set => SetProperty(ref _previewImageSource, value);
        }

        private bool _previewImageVisible;
        public bool PreviewImageVisible
        {
            get => _previewImageVisible;
            set => SetProperty(ref _previewImageVisible, value);
        }

        private bool _skipSystemFiles = true;
        public bool SkipSystemFiles
        {
            get => _skipSystemFiles;
            set => SetProperty(ref _skipSystemFiles, value);
        }

        private bool _skipWindowsDirectory = true;
        public bool SkipWindowsDirectory
        {
            get => _skipWindowsDirectory;
            set => SetProperty(ref _skipWindowsDirectory, value);
        }

        // Dashboard properties
        private string _dashboardSummary = string.Empty;
        public string DashboardSummary
        {
            get => _dashboardSummary;
            set => SetProperty(ref _dashboardSummary, value);
        }

        private DashboardData? _dashboardData;
        public DashboardData? DashboardData
        {
            get => _dashboardData;
            set => SetProperty(ref _dashboardData, value);
        }

        // Trends properties
        private string _trendsSummary = string.Empty;
        public string TrendsSummary
        {
            get => _trendsSummary;
            set => SetProperty(ref _trendsSummary, value);
        }

        private string _trendsRecommendation = string.Empty;
        public string TrendsRecommendation
        {
            get => _trendsRecommendation;
            set => SetProperty(ref _trendsRecommendation, value);
        }

        // Search properties
        private string _searchStatus = string.Empty;
        public string SearchStatus
        {
            get => _searchStatus;
            set => SetProperty(ref _searchStatus, value);
        }

        // Recommendation properties
        private string _recommendationSummary = string.Empty;
        public string RecommendationSummary
        {
            get => _recommendationSummary;
            set => SetProperty(ref _recommendationSummary, value);
        }

        // Duplicate toolbar
        public long MinDupSizeBytes
        {
            get => _minDupSizeBytes;
            set => SetProperty(ref _minDupSizeBytes, Math.Max(0, value));
        }

        public bool VerifyByteByByte
        {
            get => _verifyByteByByte;
            set => SetProperty(ref _verifyByteByByte, value);
        }

        // Drive list
        public List<string> AvailableDrives { get; }

        // Commands
        public ICommand ScanCommand { get; }
        public ICommand StopScanCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand GenerateRecommendationsCommand { get; }
        public ICommand RefreshDashboardCommand { get; }
        public ICommand RefreshTrendsCommand { get; }
        public ICommand AutoSelectKeepNewestCommand { get; }
        public ICommand AutoSelectKeepOldestCommand { get; }
        public ICommand AutoSelectKeepLargestCommand { get; }
        public ICommand AutoSelectKeepSmallestCommand { get; }
        public ICommand AutoSelectKeepShortestPathCommand { get; }
        public ICommand AutoSelectKeepOnePerFolderCommand { get; }
        public ICommand AutoSelectKeepFastestDriveCommand { get; }
        public ICommand AutoSelectKeepHighestQualityCommand { get; }

        // Events for UI-specific actions that code-behind handles
        public event Action? TreemapRedrawRequested;
#pragma warning disable CS0067 // Event is raised via code-behind subscription
        public event Action? SunburstRedrawRequested;
        public event Action<FileSystemNode>? FilePreviewRequested;
#pragma warning restore CS0067

        public MainViewModel(
            IFileSystemScannerService scannerService,
            IDuplicateDetectionService duplicateService,
            IFilterService filterService,
            IAnalysisService analysisService,
            HashCache hashCache,
            ILogger logger,
            ScanMetadataService metadataService,
            TemporaryFilesService tempFilesService,
            AdvancedAnalysisService advancedAnalysisService,
            ExportService exportService,
            SearchService searchService,
            TrendsService trendsService,
            FilePreviewService previewService,
            CleanupRecommendationService cleanupRecommendationService,
            FileOperationsService fileOperationsService,
            DashboardService dashboardService,
            DuplicateManagementService duplicateManagementService)
        {
            _scannerService = scannerService;
            _duplicateService = duplicateService;
            _filterService = filterService;
            _analysisService = analysisService;
            _hashCache = hashCache;
            _logger = logger;
            _metadataService = metadataService;
            _tempFilesService = tempFilesService;
            _advancedAnalysisService = advancedAnalysisService;
            _exportService = exportService;
            _searchService = searchService;
            _trendsService = trendsService;
            _previewService = previewService;
            _cleanupRecommendationService = cleanupRecommendationService;
            _fileOperationsService = fileOperationsService;
            _dashboardService = dashboardService;
            _duplicateManagementService = duplicateManagementService;

            // Initialize drives
            AvailableDrives = GetAvailableDrives();

            // Commands
            ScanCommand = new AsyncRelayCommand(RunScanAsync, () => !IsScanning);
            StopScanCommand = new RelayCommand(CancelScan, () => IsScanning);
            SearchCommand = new RelayCommand<SearchCriteria>(ExecuteSearch);
            ClearSearchCommand = new RelayCommand(ClearSearch);
            GenerateRecommendationsCommand = new RelayCommand(GenerateRecommendations);
            RefreshDashboardCommand = new RelayCommand(RefreshDashboard);
            RefreshTrendsCommand = new RelayCommand(RefreshTrends);
            AutoSelectKeepNewestCommand = new RelayCommand(() =>
                ApplyAutoSelect(g => g.Files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault(), "Keep newest (all groups)"));
            AutoSelectKeepOldestCommand = new RelayCommand(() =>
                ApplyAutoSelect(g => g.Files.OrderBy(f => f.LastWriteTime).FirstOrDefault(), "Keep oldest (all groups)"));
            AutoSelectKeepLargestCommand = new RelayCommand(() =>
            {
                ApplyAutoSelect(g => g.Files.OrderByDescending(f => f.Size).FirstOrDefault(), "Keep largest");
                UpdateDuplicateStats();
            });
            AutoSelectKeepSmallestCommand = new RelayCommand(() =>
            {
                ApplyAutoSelect(g => g.Files.OrderBy(f => f.Size).FirstOrDefault(), "Keep smallest");
                UpdateDuplicateStats();
            });
            AutoSelectKeepShortestPathCommand = new RelayCommand(() =>
                ApplyAutoSelect(g => g.Files.OrderBy(f => f.FullPath.Length).ThenByDescending(f => f.LastWriteTime).FirstOrDefault(),
                    "Keep shortest path"));
            AutoSelectKeepOnePerFolderCommand = new RelayCommand(AutoSelectKeepOnePerFolder);
            AutoSelectKeepFastestDriveCommand = new RelayCommand(AutoSelectKeepFastestDrive);
            AutoSelectKeepHighestQualityCommand = new RelayCommand(AutoSelectKeepHighestQuality);
        }

        private List<string> GetAvailableDrives()
        {
            try
            {
                return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not retrieve drive list: {ex.Message}");
                return new List<string>();
            }
        }

        #region Scanning

        public async Task RunScanAsync()
        {
            if (string.IsNullOrWhiteSpace(ScanPath) || !Directory.Exists(ScanPath))
            {
                _logger.Warning($"Invalid scan path: '{ScanPath}'");
                ReportText = $"The specified path '{ScanPath}' does not exist.";
                return;
            }

            _logger.Info($"Starting scan of path: {ScanPath}");
            _exclusionList = SettingsManager.LoadSettings().ExclusionPatterns;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _scanErrors.Clear();
            ClearCollections();
            IsScanning = true;
            ProgressText = "Scanning...";
            ProgressValue = 0;
            ReportText = string.Empty;

            var nodeMap = new Dictionary<string, FileSystemNode>();

            var nodeProgress = new Progress<FileSystemNode>(node =>
            {
                if (node.IsDirectory)
                {
                    if (node.Parent == null)
                    {
                        RootNodes.Add(node);
                        nodeMap[node.FullPath] = node;
                    }
                    else if (nodeMap.TryGetValue(node.Parent.FullPath, out var parentNode))
                    {
                        parentNode.Children.Insert(0, node);
                        nodeMap[node.FullPath] = node;
                    }
                }
                else
                {
                    node.FormattedSize = FormatSize(node.Size);
                    AllFiles.Add(node);

                    if (node.Parent != null && nodeMap.TryGetValue(node.Parent.FullPath, out var parentNode))
                    {
                        int i = 0;
                        for (i = 0; i < parentNode.Children.Count; i++)
                        {
                            if (!parentNode.Children[i].IsDirectory && parentNode.Children[i].Size < node.Size)
                                break;
                        }
                        parentNode.Children.Insert(i, node);
                    }
                    UpdateLargestFiles(node);
                }
            });

            var textProgress = new Progress<string>(update => ProgressText = update);
            var percentProgress = new Progress<double>(percent => ProgressValue = percent);

            var scanOptions = new ScanOptions
            {
                SkipSystemFiles = SkipSystemFiles,
                SkipWindowsDirectory = SkipWindowsDirectory,
                ExclusionPatterns = _exclusionList
            };

            try
            {
                var result = await _scannerService.ScanDirectoryAsync(ScanPath, scanOptions, nodeProgress, textProgress, percentProgress, _cts.Token);
                _scanErrors.Append(result.Errors);

                if (!_cts.Token.IsCancellationRequested)
                {
                    ProgressText = "Finalizing analysis...";
                    await FinalizeAnalysisAsync(ScanPath);
                }
                else
                {
                    ReportText = "Scan cancelled by user.\n" + _scanErrors.ToString();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Scan cancelled by user");
                ReportText = "Scan cancelled by user.\n" + _scanErrors.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled error during scan: {ex.Message}", ex);
                ReportText = $"Error: {ex.Message}\n" + _scanErrors.ToString();
            }
            finally
            {
                IsScanning = false;
                ProgressText = "Analysis Complete.";
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task FinalizeAnalysisAsync(string scanPath)
        {
            _logger.Info("Starting finalization analysis");
            var allFoundFiles = AllFiles.ToList();
            var rootNode = RootNodes.FirstOrDefault();
            if (rootNode == null) return;

            UpdateBarWidths(rootNode);
            SortAllNodesBySize(rootNode);
            ApplyFilters(null);
            TreemapRedrawRequested?.Invoke();

            var duplicateProgress = new Progress<DuplicateSet>(d => Duplicates.Add(d));
            var emptyFolderProgress = new Progress<FileSystemNode>(ef => EmptyFolders.Add(ef));
            var fileTypeProgress = new Progress<FileTypeStats>(ft => FileTypes.Add(ft));
            var fileAgeProgress = new Progress<FileAgeStats>(fa => FileAgeStats.Add(fa));

            var duplicateOptions = new DuplicateDetectionOptions
            {
                MinDupSizeBytes = _minDupSizeBytes,
                VerifyByteByByte = _verifyByteByByte,
                ForceVerifyAboveBytes = Constants.DuplicateDetection.DefaultForceVerifyAboveBytes
            };

            if (_cts == null || _cts.Token.IsCancellationRequested) return;

            var analysisTasks = new List<Task>
            {
                Task.Run(() => _duplicateService.FindDuplicates(allFoundFiles, duplicateProgress, _cts.Token, duplicateOptions), _cts.Token),
                Task.Run(() => _scannerService.FindEmptyFolders(rootNode, emptyFolderProgress, _cts.Token), _cts.Token),
                Task.Run(() => _analysisService.GetFileTypeStats(allFoundFiles, fileTypeProgress, _cts.Token), _cts.Token),
                Task.Run(() => _analysisService.GetFileAgeStats(allFoundFiles, fileAgeProgress, _cts.Token), _cts.Token)
            };

            try
            {
                await Task.WhenAll(analysisTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Analysis cancelled during finalization");
                return;
            }

            if (_cts == null || _cts.Token.IsCancellationRequested) return;

            var tempCategories = _tempFilesService.AnalyzeTemporaryFiles(allFoundFiles);
            foreach (var category in tempCategories)
                TemporaryFileCategories.Add(category);

            var staleFiles = _advancedAnalysisService.FindStaleFiles(allFoundFiles, 365);
            foreach (var staleFile in staleFiles.Take(500))
                StaleFiles.Add(staleFile);

            var largeRarelyUsed = _advancedAnalysisService.FindLargeRarelyUsedFiles(allFoundFiles, 100 * 1024 * 1024, 180);
            foreach (var file in largeRarelyUsed.Take(200))
                LargeRarelyUsedFiles.Add(file);

            var previousScan = _metadataService.GetPreviousScan(scanPath);
            var comparison = _metadataService.CompareScan(previousScan, allFoundFiles);
            _metadataService.SaveScan(scanPath, allFoundFiles, rootNode);
            _hashCache.Save();

            ScanHistory.Add(new ScanHistoryEntry { ScanDate = DateTime.Now, Path = scanPath, TotalSize = rootNode.Size });

            var report = GenerateReport(rootNode.Size);
            if (!comparison.IsFirstScan)
                report += $"\n--- Scan Comparison ---\n{comparison.GetSummary()}\n";
            if (tempCategories.Any())
                report += $"\n--- Temporary Files ---\n{_tempFilesService.GetCleanupSummary(tempCategories)}\n";
            if (staleFiles.Any())
            {
                var staleSize = staleFiles.Sum(f => f.File.Size);
                report += $"\n--- Stale Files ---\nFound {staleFiles.Count:N0} files not modified in 1+ year\nTotal: {FormatSize(staleSize)}\n";
            }
            if (largeRarelyUsed.Any())
            {
                var largeRareSize = largeRarelyUsed.Sum(f => f.File.Size);
                report += $"\n--- Large Rarely-Used Files ---\nFound {largeRarelyUsed.Count:N0} large files (100MB+) not accessed in 6+ months\nPotential savings: {FormatSize(largeRareSize)}\n";
            }
            report += "\n--- Scan Errors ---\n" + _scanErrors.ToString();

            ReportText = report;
            _logger.Info($"Scan completed successfully. Files: {allFoundFiles.Count}, Duplicates: {Duplicates.Count}, Empty Folders: {EmptyFolders.Count}, Temp Categories: {tempCategories.Count}");
        }

        public void CancelScan() => _cts?.Cancel();

        private void ClearCollections()
        {
            RootNodes.Clear();
            AllFiles.Clear();
            SelectedFiles.Clear();
            Duplicates.Clear();
            FileTypes.Clear();
            FileAgeStats.Clear();
            LargestFiles.Clear();
            EmptyFolders.Clear();
            TemporaryFileCategories.Clear();
            StaleFiles.Clear();
            LargeRarelyUsedFiles.Clear();
            SearchResults.Clear();
            CleanupRecommendations.Clear();
        }

        private string GenerateReport(long totalSize) =>
            $"Scan Report - {DateTime.Now}\n" +
            $"Directory: {ScanPath}\n" +
            $"Total Size: {FormatSize(totalSize)}\n" +
            $"Total Files Found: {AllFiles?.Count ?? 0}\n" +
            $"Duplicates Found: {Duplicates?.Count ?? 0}\n" +
            $"Empty Folders Found: {EmptyFolders?.Count ?? 0}\n";

        #endregion

        #region File Analysis Helpers

        public void UpdateBarWidths(FileSystemNode node, double maxBarWidth = 200.0)
        {
            if (node == null || !node.IsDirectory) return;

            foreach (var child in node.Children)
            {
                if (node.Size > 0)
                {
                    child.BarWidth = (child.Size / (double)node.Size) * maxBarWidth;
                    child.BarFill = GetRandomColor();
                }
                if (child.IsDirectory)
                    UpdateBarWidths(child, maxBarWidth);
            }
        }

        public void SortAllNodesBySize(FileSystemNode node)
        {
            if (node == null || !node.IsDirectory || node.Children == null || node.Children.Count == 0) return;

            var sortedChildren = new ObservableCollection<FileSystemNode>(node.Children.OrderByDescending(c => c.Size));
            node.Children.Clear();
            foreach (var child in sortedChildren)
            {
                node.Children.Add(child);
                SortAllNodesBySize(child);
            }
        }

        private void UpdateLargestFiles(FileSystemNode file)
        {
            if (LargestFiles.Count < 100)
            {
                LargestFiles.Add(file);
            }
            else if (file.Size > LargestFiles.Min(f => f.Size))
            {
                LargestFiles.Add(file);
                var sorted = LargestFiles.OrderByDescending(f => f.Size).Take(100).ToList();
                LargestFiles.Clear();
                foreach (var f in sorted) LargestFiles.Add(f);
            }
        }

        #endregion

        #region Filtering

        public void ApplyFilters(FilterCriteria? criteria)
        {
            if (AllFiles == null) return;
            SelectedFiles.Clear();

            IEnumerable<FileSystemNode> filtered;
            if (criteria != null)
            {
                filtered = _filterService.ApplyFilters(AllFiles, criteria);
            }
            else
            {
                filtered = AllFiles;
            }

            foreach (var file in filtered)
                SelectedFiles.Add(file);
        }

        public void ApplyQuickFilter(string filterType)
        {
            if (AllFiles == null || AllFiles.Count == 0) return;

            SelectedFiles.Clear();
            var filtered = filterType == "All"
                ? AllFiles.ToList()
                : _advancedAnalysisService.ApplyQuickFilter(AllFiles.ToList(), filterType);

            foreach (var file in filtered)
                SelectedFiles.Add(file);

            _logger.Info($"Quick filter applied: {filterType}, Results: {filtered.Count}");
        }

        #endregion

        #region Search

        public void ExecuteSearch(SearchCriteria? criteria)
        {
            if (AllFiles == null || AllFiles.Count == 0)
            {
                SearchStatus = "Please run a scan first before searching.";
                return;
            }

            if (criteria == null) return;

            try
            {
                var results = _searchService.Search(AllFiles.ToList(), criteria);
                SearchResults.Clear();
                foreach (var result in results)
                    SearchResults.Add(result);

                _searchService.AddToHistory(criteria);
                SearchStatus = $"Found {results.Count:N0} results - {_searchService.GetSearchSummary(criteria)}";
                _logger.Info($"Search executed: {_searchService.GetSearchSummary(criteria)}, Results: {results.Count}");
            }
            catch (Exception ex)
            {
                SearchStatus = $"Search error: {ex.Message}";
                _logger.Error("Search execution failed", ex);
            }
        }

        public void ClearSearch()
        {
            SearchResults.Clear();
            SearchStatus = "Search cleared";
        }

        #endregion

        #region Duplicate Management

        private void ApplyAutoSelect(Func<DuplicateSet, FileSystemNode?> pickKeep, string ruleDescription)
        {
            foreach (var group in Duplicates)
            {
                if (group?.Files == null || group.Files.Count < 2) continue;
                var keep = pickKeep(group);
                if (keep == null) continue;

                foreach (var f in group.Files)
                    f.IsSelected = !StringComparer.OrdinalIgnoreCase.Equals(f.FullPath, keep.FullPath);
            }
            _lastAutoSelectRuleDescription = ruleDescription;
            ReportText = $"Auto-select rule applied: {ruleDescription}";
        }

        public void ApplyAutoSelectForGroup(DuplicateSet group, Func<DuplicateSet, FileSystemNode?> pickKeep)
        {
            if (group?.Files == null || group.Files.Count < 2) return;
            var keep = pickKeep(group);
            if (keep == null) return;

            foreach (var f in group.Files)
                f.IsSelected = !StringComparer.OrdinalIgnoreCase.Equals(f.FullPath, keep.FullPath);
        }

        public void ClearGroupSelection(DuplicateSet group)
        {
            if (group?.Files == null) return;
            foreach (var f in group.Files)
                f.IsSelected = false;
        }

        private void AutoSelectKeepOnePerFolder()
        {
            foreach (var group in Duplicates)
            {
                if (group?.Files == null || group.Files.Count < 2) continue;

                var byRoot = group.Files.GroupBy(f => GetTopFolderKey(f.FullPath));
                var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var bucket in byRoot)
                {
                    var keep = bucket.OrderByDescending(f => f.LastWriteTime).First();
                    keepSet.Add(keep.FullPath);
                }

                foreach (var f in group.Files)
                    f.IsSelected = !keepSet.Contains(f.FullPath);
            }
            _lastAutoSelectRuleDescription = "Keep one per folder tree";
            ReportText = "Auto-select rule applied: Keep one per folder tree";
        }

        private void AutoSelectKeepFastestDrive()
        {
            ApplyAutoSelect(g =>
            {
                FileSystemNode? best = null;
                int bestScore = int.MinValue;

                foreach (var f in g.Files)
                {
                    try
                    {
                        var root = Path.GetPathRoot(f.FullPath);
                        if (string.IsNullOrEmpty(root)) continue;
                        var di = new DriveInfo(root);
                        int score = DrivePreferenceScore(di);

                        if (score > bestScore ||
                            (score == bestScore && f.Size > (best?.Size ?? -1)) ||
                            (score == bestScore && f.Size == (best?.Size ?? -1) && f.LastWriteTime > (best?.LastWriteTime ?? DateTime.MinValue)))
                        {
                            best = f; bestScore = score;
                        }
                    }
                    catch { /* ignore */ }
                }
                return best ?? g.Files.FirstOrDefault();
            }, "Keep on fastest/cheapest drive (heuristic)");
        }

        private void AutoSelectKeepHighestQuality()
        {
            ApplyAutoSelect(g =>
            {
                FileSystemNode? best = null;
                long bestScore = -1;

                foreach (var f in g.Files)
                {
                    long score = 0;
                    var ext = (f.Extension ?? "").ToLowerInvariant();

                    if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif")
                    {
                        try
                        {
                            using var fs = new FileStream(f.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            var frame = decoder.Frames.FirstOrDefault();
                            if (frame != null)
                            {
                                score = (long)frame.PixelWidth * (long)frame.PixelHeight;
                                score = (score << 20) + f.Size;
                            }
                            else score = f.Size;
                        }
                        catch { score = f.Size; }
                    }
                    else
                    {
                        score = f.Size;
                    }

                    if (score > bestScore || (score == bestScore && f.LastWriteTime > (best?.LastWriteTime ?? DateTime.MinValue)))
                    {
                        best = f; bestScore = score;
                    }
                }

                return best ?? g.Files.OrderByDescending(x => x.Size).FirstOrDefault();
            }, "Keep highest quality (media heuristic)");
        }

        public void UpdateDuplicateStats()
        {
            if (Duplicates == null || !Duplicates.Any())
            {
                DuplicateStatsText = "No duplicates found";
                return;
            }

            var groups = _duplicateManagementService.ApplyAutoSelectRules(
                Duplicates.ToList(), AutoSelectRule.None);
            var stats = _duplicateManagementService.CalculateStatistics(groups);

            var selectedFiles = Duplicates.SelectMany(d => d.Files).Count(f => f.IsSelected);
            var selectedSize = Duplicates.SelectMany(d => d.Files).Where(f => f.IsSelected).Sum(f => f.Size);

            DuplicateStatsText = $"Selected: {selectedFiles:N0} files ({FormatSize(selectedSize)}) | " +
                                 $"Total Wasted: {FormatSize(stats.TotalWastedSpace)}";
        }

        public List<FileSystemNode> GetSelectedDuplicateFiles()
        {
            return Duplicates?.SelectMany(d => d.Files).Where(f => f.IsSelected).ToList() ?? new List<FileSystemNode>();
        }

        public void RemoveDeletedFilesFromDuplicates(List<FileSystemNode> deletedFiles)
        {
            foreach (var file in deletedFiles)
            {
                foreach (var dup in Duplicates.ToList())
                {
                    dup.Files.Remove(file);
                    if (dup.Files.Count < 2)
                        Duplicates.Remove(dup);
                }
                AllFiles.Remove(file);
            }
        }

        public string LastAutoSelectRuleDescription => _lastAutoSelectRuleDescription;

        #endregion

        #region Dashboard & Trends

        public void RefreshDashboard()
        {
            var rootNode = RootNodes.FirstOrDefault();
            if (rootNode == null || AllFiles == null || !AllFiles.Any())
            {
                DashboardSummary = "Please run a scan first.";
                return;
            }

            try
            {
                var dashboard = _dashboardService.GenerateDashboard(AllFiles.ToList(), rootNode);
                DashboardData = dashboard;
                DashboardSummary = _dashboardService.GetDashboardSummary(dashboard);
                _logger.Info("Dashboard refreshed successfully");
            }
            catch (Exception ex)
            {
                DashboardSummary = $"Error refreshing dashboard: {ex.Message}";
                _logger.Error($"Error refreshing dashboard: {ex.Message}");
            }
        }

        public void RefreshTrends()
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null)
            {
                TrendsSummary = "Please run a scan first.";
                return;
            }

            var analysis = _trendsService.AnalyzeTrends(root.FullPath);
            var breakdown = _trendsService.GetSpaceBreakdown(AllFiles?.ToList() ?? new List<FileSystemNode>());

            TrendsSummary = analysis.Summary;
            TrendsRecommendation = analysis.Recommendation;
            _logger.Info("Trends refreshed successfully");
        }

        public TrendAnalysis? GetTrendAnalysis()
        {
            var root = RootNodes.FirstOrDefault();
            return root != null ? _trendsService.AnalyzeTrends(root.FullPath) : null;
        }

        public SpaceBreakdown? GetSpaceBreakdown()
        {
            return _trendsService.GetSpaceBreakdown(AllFiles?.ToList() ?? new List<FileSystemNode>());
        }

        public List<string> GetGrowthPredictions(TrendAnalysis analysis)
        {
            return _trendsService.GetGrowthPredictions(analysis);
        }

        #endregion

        #region Cleanup Recommendations

        public void GenerateRecommendations()
        {
            if (AllFiles == null || !AllFiles.Any())
            {
                RecommendationSummary = "Please run a scan first to get cleanup recommendations.";
                return;
            }

            try
            {
                var recommendations = _cleanupRecommendationService.GenerateRecommendations(AllFiles.ToList());
                RecommendationSummary = _cleanupRecommendationService.GetOverallSummary(recommendations);

                CleanupRecommendations.Clear();
                foreach (var recommendation in recommendations)
                    CleanupRecommendations.Add(recommendation);

                _logger.Info($"Generated {recommendations.Count} cleanup recommendations");
            }
            catch (Exception ex)
            {
                RecommendationSummary = $"Error generating recommendations: {ex.Message}";
                _logger.Error($"Error generating recommendations: {ex.Message}");
            }
        }

        #endregion

        #region File Preview

        public async Task PreviewFileAsync(FileSystemNode file)
        {
            if (file == null) return;

            PreviewImageVisible = false;
            PreviewTextVisible = false;
            PreviewMessage = "Loading preview...";

            await Task.Run(() =>
            {
                try
                {
                    if (file.IsDirectory)
                    {
                        PreviewMessage = "Select a file to preview.";
                        return;
                    }

                    var result = _previewService.PreviewFile(file.FullPath);

                    if (!result.CanPreview)
                    {
                        PreviewMessage = result.Message;
                        return;
                    }

                    if (!string.IsNullOrEmpty(result.PreviewText))
                    {
                        PreviewText = result.PreviewText;
                        PreviewTextVisible = true;
                        PreviewMessage = "";
                    }
                    else if (!string.IsNullOrEmpty(result.Message))
                    {
                        PreviewText = result.Message;
                        PreviewTextVisible = true;
                        PreviewMessage = "";
                    }
                    else
                    {
                        PreviewMessage = "No preview available.";
                    }
                }
                catch (Exception ex)
                {
                    PreviewMessage = $"Error previewing file: {ex.Message}";
                }
            });
        }

        #endregion

        #region Export

        public ExportData PrepareExportData(ExportOptions options)
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null)
                throw new InvalidOperationException("No scan data available for export.");

            return _exportService.PrepareExportData(
                root.FullPath,
                root,
                AllFiles?.ToList() ?? new List<FileSystemNode>(),
                Duplicates?.ToList() ?? new List<DuplicateSet>(),
                FileTypes?.ToList() ?? new List<FileTypeStats>(),
                LargestFiles?.ToList() ?? new List<FileSystemNode>(),
                TemporaryFileCategories?.ToList() ?? new List<TemporaryFileCategory>(),
                StaleFiles?.ToList() ?? new List<StaleFileInfo>(),
                options
            );
        }

        public string ExportToCsv(ExportData data, ExportOptions options) => _exportService.ExportToCsv(data, options);
        public string ExportToJson(ExportData data) => _exportService.ExportToJson(data);
        public string ExportToHtml(ExportData data, ExportOptions options) => _exportService.ExportToHtml(data, options);

        #endregion

        #region File Operations

        public async Task<FileOperationResult> DeleteFilesAsync(List<string> filePaths, bool useRecycleBin, IProgress<int>? progress = null)
        {
            return await _fileOperationsService.DeleteFilesAsync(filePaths, useRecycleBin, progress);
        }

        public async Task<FileOperationResult> MoveFilesAsync(List<string> filePaths, string destination, IProgress<int>? progress = null)
        {
            return await _fileOperationsService.MoveFilesAsync(filePaths, destination, progress);
        }

        public async Task<FileOperationResult> CompressFilesAsync(List<string> filePaths, string zipPath, IProgress<int>? progress = null)
        {
            return await _fileOperationsService.CompressFilesAsync(filePaths, zipPath, progress);
        }

        public HashSet<FileSystemNode> GetSelectedItemsForDeletion()
        {
            var items = new HashSet<FileSystemNode>(AllFiles.Where(f => f.IsSelected));
            foreach (var duplicateSet in Duplicates)
            {
                foreach (var file in duplicateSet.Files.Where(f => f.IsSelected))
                    items.Add(file);
            }
            foreach (var folder in EmptyFolders.Where(f => f.IsSelected))
                items.Add(folder);
            return items;
        }

        #endregion

        #region Utility

        private static Color GetRandomColor() =>
            Color.FromRgb((byte)Random.Shared.Next(100, 220), (byte)Random.Shared.Next(100, 220), (byte)Random.Shared.Next(100, 220));

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "0 bytes";
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

        public IEnumerable<FileSystemNode> GetAllNodes(FileSystemNode node)
        {
            if (node == null) yield break;
            var stack = new Stack<FileSystemNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                if (current.Children == null) continue;
                foreach (var child in current.Children) stack.Push(child);
            }
        }

        private static string GetTopFolderKey(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (root == null) return path.ToUpperInvariant();
                var rest = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var first = rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
                return (root + first ?? "").ToUpperInvariant();
            }
            catch { return path.ToUpperInvariant(); }
        }

        private static int DrivePreferenceScore(DriveInfo di)
        {
            return di.DriveType switch
            {
                DriveType.Fixed => 5,
                DriveType.Ram => 4,
                DriveType.Removable => 3,
                DriveType.Network => 2,
                _ => 1
            };
        }

        #endregion
    }

    // Generic version of RelayCommand for typed parameters
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
        public void Execute(object? parameter) => _execute((T?)parameter);
    }
}
