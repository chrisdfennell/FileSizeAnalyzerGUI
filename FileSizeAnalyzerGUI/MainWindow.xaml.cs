using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing; // System.IO.Hashing.XxHash64
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileSizeAnalyzerGUI.Services;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<FileSystemNode> RootNodes { get; set; }
        public ObservableCollection<FileSystemNode> AllFiles { get; set; }
        public ObservableCollection<FileSystemNode> SelectedFiles { get; set; }
        public ObservableCollection<DuplicateSet> Duplicates { get; set; }
        public ObservableCollection<FileTypeStats> FileTypes { get; set; }
        public ObservableCollection<FileAgeStats> FileAgeStats { get; set; }
        public ObservableCollection<FileSystemNode> LargestFiles { get; set; }
        public ObservableCollection<FileSystemNode> EmptyFolders { get; set; }
        public ObservableCollection<ScanHistoryEntry> ScanHistory { get; set; }
        public ObservableCollection<TemporaryFileCategory> TemporaryFileCategories { get; set; }
        public ObservableCollection<StaleFileInfo> StaleFiles { get; set; }
        public ObservableCollection<LargeRarelyUsedFile> LargeRarelyUsedFiles { get; set; }
        public ObservableCollection<FileSystemNode> SearchResults { get; set; }
        public ObservableCollection<CleanupRecommendation> CleanupRecommendations { get; set; }

        private CancellationTokenSource _cts;
        private readonly StringBuilder _scanErrors;
        private DateTime _lastStatusUpdateTime;
        private List<string> _exclusionList = new List<string>();
        private Dictionary<string, string>? _cliArgs;

        // Duplicate engine options (wired to toolbar)
        private long _minDupSizeBytes = Constants.DuplicateDetection.DefaultMinDupSizeBytes;
        private bool _verifyByteByByte = Constants.DuplicateDetection.DefaultVerifyByteByByte;
        private readonly HashCache _hashCache;

        // Report context
        private string _lastAutoSelectRuleDescription = "None";

        private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        private readonly IFileSystemScannerService _scannerService;
        private readonly IDuplicateDetectionService _duplicateService;
        private readonly IFilterService _filterService;
        private readonly IAnalysisService _analysisService;
        private readonly ILogger _logger;
        private readonly TreemapService _treemapService;
        private readonly SunburstService _sunburstService;
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

        public MainWindow(
            IFileSystemScannerService scannerService,
            IDuplicateDetectionService duplicateService,
            IFilterService filterService,
            IAnalysisService analysisService,
            HashCache hashCache,
            StringBuilder scanErrors,
            ILogger logger,
            TreemapService treemapService,
            SunburstService sunburstService,
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
            _scanErrors = scanErrors;
            _logger = logger;
            _treemapService = treemapService;
            _sunburstService = sunburstService;
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

            InitializeComponent();
            InitializeData();
            WireDuplicateToolbar();
        }

        public void InitializeWithPath(string initialPath)
        {
            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            {
                DirectoryPathTextBox.Text = initialPath;
                Loaded += (s, e) => ScanButton_Click(null, null);
            }
        }

        public void InitializeWithCliArgs(Dictionary<string, string> cliArgs)
        {
            _cliArgs = cliArgs;

            if (_cliArgs.ContainsKey("-no-skip-system"))
            {
                SkipSystemFilesCheckBox.IsChecked = false;
            }
            if (_cliArgs.ContainsKey("-no-skip-windows"))
            {
                SkipWindowsDirCheckBox.IsChecked = false;
            }

            Loaded += MainWindow_Loaded_Cli;
        }

        private void WireDuplicateToolbar()
        {
            if (FindName("VerifyDuplicatesCheckBox") is CheckBox verifyBox)
            {
                _verifyByteByByte = verifyBox.IsChecked == true;
                verifyBox.Checked += (_, __) => _verifyByteByByte = true;
                verifyBox.Unchecked += (_, __) => _verifyByteByByte = false;
            }

            if (FindName("MinDupSizeCombo") is ComboBox sizeCombo)
            {
                ApplyMinDupSizeFromCombo(sizeCombo);
                sizeCombo.SelectionChanged += (_, __) => ApplyMinDupSizeFromCombo(sizeCombo);
            }
        }

        private void ApplyMinDupSizeFromCombo(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem it &&
                long.TryParse((it.Tag ?? "").ToString(), out var val))
            {
                _minDupSizeBytes = Math.Max(0, val);
            }
        }

        private async void MainWindow_Loaded_Cli(object sender, RoutedEventArgs e)
        {
            if (_cliArgs == null) return;

            DirectoryPathTextBox.Text = _cliArgs["-path"];
            await RunScanAsync();

            if (_cliArgs.TryGetValue("-export", out var exportPath))
            {
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    exportPath = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
                }
                await Task.Run(() => CsvExporter.ExportToCsv(AllFiles.ToList(), exportPath));
            }

            if (_cliArgs.ContainsKey("-exit"))
            {
                Application.Current.Shutdown();
            }
        }

        private void InitializeData()
        {
            RootNodes = new ObservableCollection<FileSystemNode>();
            AllFiles = new ObservableCollection<FileSystemNode>();
            SelectedFiles = new ObservableCollection<FileSystemNode>();
            Duplicates = new ObservableCollection<DuplicateSet>();
            FileTypes = new ObservableCollection<FileTypeStats>();
            FileAgeStats = new ObservableCollection<FileAgeStats>();
            LargestFiles = new ObservableCollection<FileSystemNode>();
            EmptyFolders = new ObservableCollection<FileSystemNode>();
            ScanHistory = new ObservableCollection<ScanHistoryEntry>();
            TemporaryFileCategories = new ObservableCollection<TemporaryFileCategory>();
            StaleFiles = new ObservableCollection<StaleFileInfo>();
            LargeRarelyUsedFiles = new ObservableCollection<LargeRarelyUsedFile>();
            SearchResults = new ObservableCollection<FileSystemNode>();
            CleanupRecommendations = new ObservableCollection<CleanupRecommendation>();

            this.DataContext = this;

            PopulateDrives();
            DirectoryTreeView.SelectedItemChanged += DirectoryTreeView_SelectedItemChanged;
        }

        #region Window Control and Setup
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;

        private void PopulateDrives()
        {
            try
            {
                DriveSelectionComboBox.ItemsSource = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Could not retrieve drive list: {ex.Message}");
            }
        }

        private void DriveSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveSelectionComboBox.SelectedItem is string drive)
            {
                DirectoryPathTextBox.Text = drive;
            }
        }
        #endregion

        #region Scanning Logic
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            await RunScanAsync();
        }

        private async Task RunScanAsync()
        {
            string scanPath = DirectoryPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
            {
                _logger.Warning($"Invalid scan path: '{scanPath}'");
                MessageBox.Show($"The specified path '{scanPath}' does not exist.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _logger.Info($"Starting scan of path: {scanPath}");
            _exclusionList = SettingsManager.LoadSettings().ExclusionPatterns;

            _cts = new CancellationTokenSource();
            _scanErrors.Clear();
            ResetUIForScan();

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
                else // file
                {
                    node.FormattedSize = FormatSize(node.Size);
                    AllFiles.Add(node);

                    if (nodeMap.TryGetValue(node.Parent.FullPath, out var parentNode))
                    {
                        int i = 0;
                        for (i = 0; i < parentNode.Children.Count; i++)
                        {
                            if (!parentNode.Children[i].IsDirectory && parentNode.Children[i].Size < node.Size)
                            {
                                break;
                            }
                        }
                        parentNode.Children.Insert(i, node);
                    }
                    UpdateLargestFiles(node);
                }
            });

            var textProgress = new Progress<string>(update => ProgressTextBlock.Text = update);
            var percentProgress = new Progress<double>(percent =>
            {
                ScanProgressBar.Value = percent;
            });

            var scanOptions = new ScanOptions
            {
                SkipSystemFiles = SkipSystemFilesCheckBox.IsChecked == true,
                SkipWindowsDirectory = SkipWindowsDirCheckBox.IsChecked == true,
                ExclusionPatterns = _exclusionList
            };

            try
            {
                var result = await _scannerService.ScanDirectoryAsync(scanPath, scanOptions, nodeProgress, textProgress, percentProgress, _cts.Token);

                _scanErrors.Append(result.Errors);

                if (!_cts.Token.IsCancellationRequested)
                {
                    ProgressTextBlock.Text = "Finalizing analysis...";
                    await FinalizeAnalysisAsync(scanPath);
                }
                else
                {
                    ReportsTextBox.Text = "Scan cancelled by user.\n" + _scanErrors.ToString();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Scan cancelled by user");
                ReportsTextBox.Text = "Scan cancelled by user.\n" + _scanErrors.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled error during scan: {ex.Message}", ex);
                MessageBox.Show($"An unhandled error occurred: {ex.Message}", "Error");
                ReportsTextBox.Text = $"Error: {ex.Message}\n" + _scanErrors.ToString();
            }
            finally
            {
                ResetUIAfterScan();
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
            ApplyFilters_Click(null, null);
            DrawTreemap();

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

            // Analyze temporary files
            var tempCategories = _tempFilesService.AnalyzeTemporaryFiles(allFoundFiles);
            foreach (var category in tempCategories)
            {
                TemporaryFileCategories.Add(category);
            }

            // Find stale files (1+ year old)
            var staleFiles = _advancedAnalysisService.FindStaleFiles(allFoundFiles, 365);
            foreach (var staleFile in staleFiles.Take(500)) // Limit to 500 for performance
            {
                StaleFiles.Add(staleFile);
            }

            // Find large rarely-used files (100MB+, 6+ months)
            var largeRarelyUsed = _advancedAnalysisService.FindLargeRarelyUsedFiles(allFoundFiles, 100 * 1024 * 1024, 180);
            foreach (var file in largeRarelyUsed.Take(200)) // Limit to 200 for performance
            {
                LargeRarelyUsedFiles.Add(file);
            }

            // Compare with previous scan
            var previousScan = _metadataService.GetPreviousScan(scanPath);
            var comparison = _metadataService.CompareScan(previousScan, allFoundFiles);

            // Save current scan metadata
            _metadataService.SaveScan(scanPath, allFoundFiles, rootNode);

            _hashCache.Save();

            ScanHistory.Add(new ScanHistoryEntry { ScanDate = DateTime.Now, Path = scanPath, TotalSize = rootNode.Size });

            // Enhanced report with comparison
            var report = GenerateReport(rootNode.Size);
            if (!comparison.IsFirstScan)
            {
                report += $"\n--- Scan Comparison ---\n{comparison.GetSummary()}\n";
            }
            if (tempCategories.Any())
            {
                report += $"\n--- Temporary Files ---\n{_tempFilesService.GetCleanupSummary(tempCategories)}\n";
            }
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

            ReportsTextBox.Text = report;

            _logger.Info($"Scan completed successfully. Files: {allFoundFiles.Count}, Duplicates: {Duplicates.Count}, Empty Folders: {EmptyFolders.Count}, Temp Categories: {tempCategories.Count}");
        }

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
                {
                    UpdateBarWidths(child, maxBarWidth);
                }
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

        private void ResetUIForScan()
        {
            ScanButton.Visibility = Visibility.Collapsed;
            StopScanButton.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Minimum = 0;
            ScanProgressBar.Maximum = 100;
            ScanProgressBar.Value = 0;
            ScanProgressBar.Visibility = Visibility.Visible;
            ProgressTextBlock.Text = "Scanning...";
            ProgressTextBlock.Visibility = Visibility.Visible;

            _lastStatusUpdateTime = DateTime.MinValue;

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
            TreemapCanvas.Children.Clear();
            var sunburstCanvas = FindName("SunburstCanvas") as Canvas;
            sunburstCanvas?.Children.Clear();
            ReportsTextBox.Text = "";
        }

        private void ResetUIAfterScan()
        {
            ProgressTextBlock.Text = "Analysis Complete.";
            Task.Delay(2000).ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressTextBlock.Visibility = Visibility.Collapsed;
                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    StopScanButton.Visibility = Visibility.Collapsed;
                    ScanButton.Visibility = Visibility.Visible;
                });
            });
        }

        private void StopScanButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
        #endregion

        private string GenerateReport(long totalSize) =>
            $"Scan Report - {DateTime.Now}\n" +
            $"Directory: {DirectoryPathTextBox.Text}\n" +
            $"Total Size: {FormatSize(totalSize)}\n" +
            $"Total Files Found: {AllFiles?.Count ?? 0}\n" +
            $"Duplicates Found: {Duplicates?.Count ?? 0}\n" +
            $"Empty Folders Found: {EmptyFolders?.Count ?? 0}\n";

        #region UI Handlers + Preview
        private void DateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DateFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                bool isCustomRange = selectedItem.Content.ToString() == "Custom Range";
                if (StartDatePicker != null) StartDatePicker.Visibility = isCustomRange ? Visibility.Visible : Visibility.Collapsed;
                if (EndDatePicker != null) EndDatePicker.Visibility = isCustomRange ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select a folder to scan" };
            if (dialog.ShowDialog() == true)
            {
                DirectoryPathTextBox.Text = dialog.FolderName;
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (AllFiles == null || !AllFiles.Any())
            {
                MessageBox.Show("No data to export. Please run a scan first.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                await Task.Run(() => CsvExporter.ExportToCsv(AllFiles.ToList(), sfd.FileName));
                MessageBox.Show($"Exported to {sfd.FileName}");
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
        private void AboutButton_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FileSystemNode node)
            {
                try
                {
                    if (Directory.Exists(node.FullPath) || File.Exists(node.FullPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}");
                }
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FileSystemNode node)
            {
                await DeleteFileAsync(node);
            }
        }
        #endregion

        #region Temporary Files Cleanup
        private async void CleanupTempFiles_Click(object sender, RoutedEventArgs e)
        {
            var tempDataGrid = FindName("TempFilesDataGrid") as DataGrid;
            if (tempDataGrid == null) return;

            var selectedCategories = tempDataGrid.SelectedItems.Cast<TemporaryFileCategory>().ToList();
            if (!selectedCategories.Any())
            {
                MessageBox.Show("Please select categories to clean up.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalFiles = selectedCategories.Sum(c => c.Files.Count);
            var totalSize = selectedCategories.Sum(c => c.TotalSize);
            var hasUnsafeCategories = selectedCategories.Any(c => !c.IsSafeToDelete);

            var warningMessage = $"About to delete {totalFiles:N0} files totaling {FormatSize(totalSize)}";
            if (hasUnsafeCategories)
            {
                warningMessage += "\n\n⚠️ WARNING: Some selected categories are marked as potentially unsafe to delete!";
            }
            warningMessage += "\n\nFiles will be moved to Recycle Bin. Continue?";

            if (MessageBox.Show(warningMessage, "Confirm Cleanup", MessageBoxButton.YesNo,
                hasUnsafeCategories ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var filesToDelete = selectedCategories.SelectMany(c => c.Files).ToList();
                await DeleteMultipleFilesAsync(filesToDelete);
            }
        }
        #endregion

        #region Bulk Operations
        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = new HashSet<FileSystemNode>(AllFiles.Where(f => f.IsSelected));
            foreach (var duplicateSet in Duplicates)
            {
                foreach (var file in duplicateSet.Files.Where(f => f.IsSelected))
                {
                    itemsToDelete.Add(file);
                }
            }
            foreach (var folder in EmptyFolders.Where(f => f.IsSelected))
            {
                itemsToDelete.Add(folder);
            }

            if (!itemsToDelete.Any())
            {
                MessageBox.Show("No items selected for deletion.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _logger.Info($"User requested deletion of {itemsToDelete.Count} items");
            string message = $"Are you sure you want to move {itemsToDelete.Count} selected item(s) to the Recycle Bin?";
            if (MessageBox.Show(message, "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await DeleteMultipleFilesAsync(itemsToDelete.ToList());
            }
        }

        private async void MoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = AllFiles.Where(f => f.IsSelected && !f.IsDirectory).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No files selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFolderDialog { Title = "Select destination folder" };
            if (dialog.ShowDialog() == true)
            {
                await MoveFilesAsync(selectedItems, dialog.FolderName);
            }
        }

        private async void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = AllFiles.Where(f => f.IsSelected && !f.IsDirectory).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No files selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFolderDialog { Title = "Select destination folder" };
            if (dialog.ShowDialog() == true)
            {
                await CopyFilesAsync(selectedItems, dialog.FolderName);
            }
        }

        private async Task MoveFilesAsync(List<FileSystemNode> files, string destinationFolder)
        {
            ProgressTextBlock.Text = "Moving files...";
            ProgressTextBlock.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = 0;
            ScanProgressBar.Maximum = files.Count;
            ScanProgressBar.Visibility = Visibility.Visible;

            int movedCount = 0;
            var errors = new StringBuilder();

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        var destPath = Path.Combine(destinationFolder, Path.GetFileName(file.FullPath));
                        if (File.Exists(file.FullPath))
                        {
                            File.Move(file.FullPath, destPath, overwrite: false);
                            movedCount++;
                        }
                        Dispatcher.Invoke(() => ScanProgressBar.Value = movedCount);
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine($"Failed to move {file.FullPath}: {ex.Message}");
                    }
                }
            });

            _logger.Info($"Moved {movedCount} files to {destinationFolder}");
            MessageBox.Show($"{movedCount} file(s) moved successfully." +
                          (errors.Length > 0 ? $"\n\nErrors:\n{errors}" : ""),
                          "Move Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            await RunScanAsync();
        }

        private async Task CopyFilesAsync(List<FileSystemNode> files, string destinationFolder)
        {
            ProgressTextBlock.Text = "Copying files...";
            ProgressTextBlock.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = 0;
            ScanProgressBar.Maximum = files.Count;
            ScanProgressBar.Visibility = Visibility.Visible;

            int copiedCount = 0;
            var errors = new StringBuilder();

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        var destPath = Path.Combine(destinationFolder, Path.GetFileName(file.FullPath));
                        if (File.Exists(file.FullPath))
                        {
                            File.Copy(file.FullPath, destPath, overwrite: false);
                            copiedCount++;
                        }
                        Dispatcher.Invoke(() => ScanProgressBar.Value = copiedCount);
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine($"Failed to copy {file.FullPath}: {ex.Message}");
                    }
                }
            });

            _logger.Info($"Copied {copiedCount} files to {destinationFolder}");
            MessageBox.Show($"{copiedCount} file(s) copied successfully." +
                          (errors.Length > 0 ? $"\n\nErrors:\n{errors}" : ""),
                          "Copy Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            ProgressTextBlock.Visibility = Visibility.Collapsed;
            ScanProgressBar.Visibility = Visibility.Collapsed;
        }

        private async Task DeleteMultipleFilesAsync(List<FileSystemNode> items)
        {
            ProgressTextBlock.Text = "Deleting items...";
            ProgressTextBlock.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = 0;
            ScanProgressBar.Maximum = items.Count;
            ScanProgressBar.Visibility = Visibility.Visible;

            int deletedCount = 0;
            var deleteErrors = new StringBuilder();

            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    try
                    {
                        if (item.IsDirectory)
                        {
                            if (Directory.Exists(item.FullPath))
                                FileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                        else
                        {
                            if (File.Exists(item.FullPath))
                                FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                        deletedCount++;
                        Dispatcher.Invoke(() => ScanProgressBar.Value = deletedCount);
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Failed to delete {item.FullPath}: {ex.Message}";
                        deleteErrors.AppendLine(errorMsg);
                        _logger.Warning(errorMsg, ex);
                    }
                }
            });

            _logger.Info($"Deletion completed. {deletedCount} items deleted, {items.Count - deletedCount} failed");
            string summaryMessage = $"{deletedCount} item(s) moved to Recycle Bin.";
            if (deleteErrors.Length > 0)
            {
                summaryMessage += "\n\nSome errors occurred:\n" + deleteErrors.ToString();
            }
            MessageBox.Show(summaryMessage, "Deletion Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            await RunScanAsync();
        }

        private async Task DeleteFileAsync(FileSystemNode file)
        {
            string message = file.IsDirectory
                ? $"Are you sure you want to move the folder '{Path.GetFileName(file.FullPath)}' and all its contents to the Recycle Bin?"
                : $"Are you sure you want to move '{Path.GetFileName(file.FullPath)}' to the Recycle Bin?";

            if (MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        if (file.IsDirectory) FileSystem.DeleteDirectory(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else FileSystem.DeleteFile(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    });
                    MessageBox.Show("Item(s) moved to Recycle Bin.");
                    await RunScanAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error moving item to Recycle Bin: {ex.Message}");
                }
            }
        }
        #endregion

        #region Selection & Preview handlers
        private async void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private async void DuplicatesTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (DuplicatesTreeView.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private void ScanHistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ScanHistoryDataGrid.SelectedItem is ScanHistoryEntry se) { DirectoryPathTextBox.Text = se.Path; ScanButton_Click(null, null); } }
        private async void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await PreviewFileAsync(sf); }

        private async Task PreviewFileAsync(FileSystemNode file)
        {
            if (file == null) return;

            var previewImage = FindName("PreviewImage") as System.Windows.Controls.Image;
            if (previewImage != null) previewImage.Visibility = Visibility.Collapsed;

            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewMessage.Text = "Loading preview...";

            PreviewTextBox.FontFamily = new FontFamily("Consolas");

            await Task.Run(() =>
            {
                try
                {
                    if (file.IsDirectory)
                    {
                        Dispatcher.Invoke(() => PreviewMessage.Text = "Select a file to preview.");
                        return;
                    }

                    var result = _previewService.PreviewFile(file.FullPath);

                    Dispatcher.Invoke(() =>
                    {
                        if (!result.CanPreview)
                        {
                            PreviewMessage.Text = result.Message;
                            return;
                        }

                        if (!string.IsNullOrEmpty(result.PreviewText))
                        {
                            PreviewTextBox.Text = result.PreviewText;
                            PreviewTextBox.Visibility = Visibility.Visible;
                            PreviewMessage.Text = "";
                        }
                        else if (!string.IsNullOrEmpty(result.Message))
                        {
                            PreviewTextBox.Text = result.Message;
                            PreviewTextBox.Visibility = Visibility.Visible;
                            PreviewMessage.Text = "";
                        }
                        else
                        {
                            PreviewMessage.Text = "No preview available.";
                        }

                        // Try to show image if it's an image file
                        if (previewImage != null && !result.IsBinary)
                        {
                            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                            if (imageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.UriSource = new Uri(file.FullPath);
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.DecodePixelWidth = 400; // Limit size for preview
                                    bitmap.EndInit();
                                    previewImage.Source = bitmap;
                                    previewImage.Visibility = Visibility.Visible;
                                }
                                catch
                                {
                                    // If image loading fails, just show text preview
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => PreviewMessage.Text = $"Error previewing file: {ex.Message}");
                }
            });
        }
        #endregion

        #region Filter Preset Handlers
        private void SaveFilter_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFilterWindow { Owner = this };
            if (saveDialog.ShowDialog() == true)
            {
                var newPreset = new FilterPreset
                {
                    Name = saveDialog.FilterName,
                    ExtensionFilter = ExtensionFilterTextBox.Text,
                    SizeFilterIndex = SizeFilterComboBox.SelectedIndex,
                    DateFilterIndex = DateFilterComboBox.SelectedIndex,
                    StartDate = StartDatePicker.SelectedDate,
                    EndDate = EndDatePicker.SelectedDate
                };

                var settings = SettingsManager.LoadSettings();
                settings.FilterPresets.RemoveAll(p => p.Name.Equals(newPreset.Name, StringComparison.OrdinalIgnoreCase));
                settings.FilterPresets.Add(newPreset);
                SettingsManager.SaveSettings(settings);
            }
        }

        private void LoadFilter_Click(object sender, RoutedEventArgs e)
        {
            LoadFilterButton.ContextMenu.IsOpen = true;
        }

        private void LoadFilterContextMenu_Opening(object sender, RoutedEventArgs e)
        {
            var contextMenu = sender as ContextMenu;
            if (contextMenu == null) return;

            contextMenu.Items.Clear();
            var settings = SettingsManager.LoadSettings();

            if (settings.FilterPresets.Any())
            {
                foreach (var preset in settings.FilterPresets.OrderBy(p => p.Name))
                {
                    var menuItem = new MenuItem { Header = preset.Name, Tag = preset };
                    menuItem.Click += ApplyFilterPreset_Click;
                    contextMenu.Items.Add(menuItem);
                }
            }
            else
            {
                contextMenu.Items.Add(new MenuItem { Header = "No saved filters", IsEnabled = false });
            }
        }

        private void ApplyFilterPreset_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.Tag is FilterPreset preset)
            {
                ExtensionFilterTextBox.Text = preset.ExtensionFilter;
                SizeFilterComboBox.SelectedIndex = preset.SizeFilterIndex;
                DateFilterComboBox.SelectedIndex = preset.DateFilterIndex;
                StartDatePicker.SelectedDate = preset.StartDate;
                EndDatePicker.SelectedDate = preset.EndDate;

                ApplyFilters_Click(null, null);
            }
        }
        #endregion

        #region Treemap and Sunburst
        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            DrawTreemap();
            DrawSunburst();
        }

        private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTreemap();
        private void SunburstCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSunburst();

        private void SunburstCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Handle sunburst segment clicks
            var clickedElement = e.OriginalSource as FrameworkElement;
            if (clickedElement?.Tag is SunburstSegment segment)
            {
                if (segment.Node.IsDirectory && segment.Node.Children.Any(n => n.Size > 0))
                {
                    SelectTreeViewItem(segment.Node);
                    DrawSunburst();
                }
            }
        }

        private void DrawTreemap()
        {
            TreemapCanvas.Children.Clear();
            FileSystemNode currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? RootNodes.FirstOrDefault();
            if (currentNode == null) return;
            if (!currentNode.IsDirectory) currentNode = currentNode.Parent;
            if (currentNode == null) return;

            UpdateBreadcrumb(currentNode);

            var nodesToDraw = currentNode.Children.Where(n => n.Size > 0).ToList();
            if (!nodesToDraw.Any()) return;

            var bounds = new Rect(0, 0, TreemapCanvas.ActualWidth, TreemapCanvas.ActualHeight);
            var rectangles = _treemapService.GenerateTreemap(nodesToDraw, bounds);

            foreach (var rect in rectangles)
            {
                RenderTreemapRectangle(rect);
            }
        }

        private void UpdateBreadcrumb(FileSystemNode currentNode)
        {
            var breadcrumbs = new List<BreadcrumbItem>();
            var pathStack = new Stack<FileSystemNode>();

            for (var node = currentNode; node != null; node = node.Parent)
            {
                pathStack.Push(node);
            }

            while (pathStack.Count > 0)
            {
                var node = pathStack.Pop();
                var name = node.Parent == null
                    ? System.IO.Path.GetFileName(node.FullPath) ?? node.FullPath
                    : System.IO.Path.GetFileName(node.FullPath);

                breadcrumbs.Add(new BreadcrumbItem
                {
                    Name = name,
                    Node = node,
                    ShowSeparator = pathStack.Count > 0
                });
            }

            BreadcrumbBar.ItemsSource = breadcrumbs;
        }

        private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is FileSystemNode node)
            {
                SelectTreeViewItem(node);
                DrawTreemap();
            }
        }

        private void RenderTreemapRectangle(TreemapRectangle rect)
        {
            if (rect.Bounds.Width < 1 || rect.Bounds.Height < 1) return;

            var border = new Border
            {
                Width = rect.Bounds.Width,
                Height = rect.Bounds.Height,
                Background = TreemapService.CreateCushionBrush(rect.Color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderThickness = new Thickness(0.5),
                Tag = rect.Node,
                Cursor = Cursors.Hand
            };

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(4),
                IsHitTestVisible = false
            };

            if (rect.Bounds.Width > 60 && rect.Bounds.Height > 30)
            {
                var nameBlock = new TextBlock
                {
                    Text = System.IO.Path.GetFileName(rect.Node.FullPath),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 3,
                        ShadowDepth = 1,
                        Opacity = 0.8
                    }
                };
                stackPanel.Children.Add(nameBlock);

                if (rect.Bounds.Height > 45)
                {
                    var sizeBlock = new TextBlock
                    {
                        Text = FormatSize(rect.Node.Size),
                        Foreground = Brushes.White,
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = Colors.Black,
                            BlurRadius = 3,
                            ShadowDepth = 1,
                            Opacity = 0.8
                        }
                    };
                    stackPanel.Children.Add(sizeBlock);
                }

                if (rect.Bounds.Height > 60)
                {
                    var percentBlock = new TextBlock
                    {
                        Text = $"{rect.Percentage:F1}%",
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                        FontSize = 9,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = Colors.Black,
                            BlurRadius = 3,
                            ShadowDepth = 1,
                            Opacity = 0.8
                        }
                    };
                    stackPanel.Children.Add(percentBlock);
                }

                border.Child = stackPanel;
            }

            var tooltip = new StackPanel();
            tooltip.Children.Add(new TextBlock
            {
                Text = rect.Node.FullPath,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            tooltip.Children.Add(new TextBlock
            {
                Text = $"Size: {FormatSize(rect.Node.Size)} ({rect.Percentage:F2}%)"
            });
            tooltip.Children.Add(new TextBlock
            {
                Text = $"Type: {(rect.Node.IsDirectory ? "Folder" : rect.Node.Extension ?? "File")}"
            });
            if (!rect.Node.IsDirectory)
            {
                tooltip.Children.Add(new TextBlock
                {
                    Text = $"Modified: {rect.Node.LastWriteTime:yyyy-MM-dd HH:mm}"
                });
            }
            border.ToolTip = tooltip;

            border.MouseEnter += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                border.BorderThickness = new Thickness(2);
            };

            border.MouseLeave += (s, e) =>
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                border.BorderThickness = new Thickness(0.5);
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                TreemapRectangle_Click(rect.Node);
            };

            Canvas.SetLeft(border, rect.Bounds.X);
            Canvas.SetTop(border, rect.Bounds.Y);
            TreemapCanvas.Children.Add(border);
        }

        private void TreemapRectangle_Click(FileSystemNode node)
        {
            if (node.IsDirectory && node.Children.Any(n => n.Size > 0))
            {
                SelectTreeViewItem(node);
                DrawTreemap();
            }
            else if (!node.IsDirectory && node.Parent != null)
            {
                SelectTreeViewItem(node.Parent);
                DrawTreemap();
            }
        }

        private void DrawSunburst()
        {
            var sunburstCanvas = FindName("SunburstCanvas") as Canvas;
            if (sunburstCanvas == null) return;

            sunburstCanvas.Children.Clear();
            FileSystemNode? currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? RootNodes.FirstOrDefault();
            if (currentNode == null) return;
            if (!currentNode.IsDirectory) currentNode = currentNode.Parent;
            if (currentNode == null) return;

            UpdateBreadcrumb(currentNode);

            double centerX = sunburstCanvas.ActualWidth / 2;
            double centerY = sunburstCanvas.ActualHeight / 2;
            double maxRadius = Math.Min(centerX, centerY) - 10;

            if (maxRadius < 50) return; // Canvas too small

            var segments = _sunburstService.GenerateSunburst(currentNode, centerX, centerY, maxRadius);

            foreach (var segment in segments)
            {
                var path = _sunburstService.CreateSegmentPath(segment, centerX, centerY);

                // Add tooltip
                var tooltip = new StackPanel();
                tooltip.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(segment.Node.FullPath) ?? segment.Node.FullPath,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                tooltip.Children.Add(new TextBlock
                {
                    Text = $"Size: {FormatSize(segment.Node.Size)}"
                });
                tooltip.Children.Add(new TextBlock
                {
                    Text = $"Type: {(segment.Node.IsDirectory ? "Folder" : segment.Node.Extension ?? "File")}"
                });
                path.ToolTip = tooltip;

                // Hover effects
                path.MouseEnter += (s, e) =>
                {
                    path.StrokeThickness = 2;
                    path.Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                };

                path.MouseLeave += (s, e) =>
                {
                    path.StrokeThickness = 1;
                    path.Stroke = new SolidColorBrush(Colors.White);
                };

                path.Cursor = Cursors.Hand;
                sunburstCanvas.Children.Add(path);
            }
        }

        private void SelectTreeViewItem(FileSystemNode node)
        {
            var pathStack = new Stack<FileSystemNode>();
            for (var current = node; current != null; current = current.Parent)
            {
                pathStack.Push(current);
            }

            ItemsControl parentContainer = DirectoryTreeView;
            while (pathStack.Count > 0)
            {
                var itemToFind = pathStack.Pop();
                if (parentContainer.ItemContainerGenerator.ContainerFromItem(itemToFind) is TreeViewItem currentContainer)
                {
                    if (pathStack.Count > 0)
                    {
                        currentContainer.IsExpanded = true;
                        parentContainer = currentContainer;
                    }
                    else
                    {
                        currentContainer.IsSelected = true;
                        currentContainer.BringIntoView();
                    }
                }
                else
                {
                    parentContainer.UpdateLayout();
                    if (parentContainer.ItemContainerGenerator.ContainerFromItem(itemToFind) is TreeViewItem retryContainer)
                    {
                        if (pathStack.Count > 0)
                        {
                            retryContainer.IsExpanded = true;
                            parentContainer = retryContainer;
                        }
                        else
                        {
                            retryContainer.IsSelected = true;
                            retryContainer.BringIntoView();
                        }
                    }
                    else break;
                }
            }
        }
        #endregion

        #region Utility Methods
        private Color GetRandomColor() => Color.FromRgb((byte)new Random().Next(100, 220), (byte)new Random().Next(100, 220), (byte)new Random().Next(100, 220));

        private IEnumerable<FileSystemNode> GetAllNodes(FileSystemNode node)
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

        private string FormatSize(long bytes)
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
        #endregion

        #region Auto-select rules + keyboard shortcuts
        private void ApplyAutoSelect(Func<DuplicateSet, FileSystemNode> pickKeep, string ruleDescription)
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
            ReportsTextBox.Text = $"Auto-select rule applied: {ruleDescription}";
        }

        private void SelectDuplicates_KeepNewest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
            {
                if (group?.Files == null || group.Files.Count < 2) return;
                var newest = group.Files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (newest != null)
                {
                    foreach (var f in group.Files)
                        f.IsSelected = !StringComparer.OrdinalIgnoreCase.Equals(f.FullPath, newest.FullPath);
                }
            }
        }

        private void SelectDuplicates_KeepOldest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
            {
                if (group?.Files == null || group.Files.Count < 2) return;
                var oldest = group.Files.OrderBy(f => f.LastWriteTime).FirstOrDefault();
                if (oldest != null)
                {
                    foreach (var f in group.Files)
                        f.IsSelected = !StringComparer.OrdinalIgnoreCase.Equals(f.FullPath, oldest.FullPath);
                }
            }
        }

        private void SelectDuplicates_Clear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
            {
                if (group?.Files == null) return;
                foreach (var f in group.Files)
                    f.IsSelected = false;
            }
        }

        private void AutoSelect_KeepNewest_AllGroups_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g => g.Files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault(), "Keep newest (all groups)");
        }

        private void AutoSelect_KeepOldest_AllGroups_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g => g.Files.OrderBy(f => f.LastWriteTime).FirstOrDefault(), "Keep oldest (all groups)");
        }

        private static string GetTopFolderKey(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                var rest = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var first = rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
                return (root + first ?? "").ToUpperInvariant();
            }
            catch { return path.ToUpperInvariant(); }
        }

        private void AutoSelect_KeepOnePerFolder_Click(object sender, RoutedEventArgs e)
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
            ReportsTextBox.Text = "Auto-select rule applied: Keep one per folder tree";
        }

        private void AutoSelect_KeepShortestPath_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g => g.Files.OrderBy(f => f.FullPath.Length).ThenByDescending(f => f.LastWriteTime).FirstOrDefault(),
                            "Keep shortest path");
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

        private void AutoSelect_KeepFastestDrive_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g =>
            {
                FileSystemNode best = null;
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

        private void AutoSelect_KeepHighestQuality_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g =>
            {
                FileSystemNode best = null;
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
                        score = f.Size; // TODO: integrate TagLibSharp for AV quality
                    }

                    if (score > bestScore || (score == bestScore && f.LastWriteTime > (best?.LastWriteTime ?? DateTime.MinValue)))
                    {
                        best = f; bestScore = score;
                    }
                }

                return best ?? g.Files.OrderByDescending(x => x.Size).FirstOrDefault();
            }, "Keep highest quality (media heuristic)");
        }

        private void AutoSelect_KeepLargest_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g => g.Files.OrderByDescending(f => f.Size).FirstOrDefault(), "Keep largest");
            UpdateDuplicateStats();
        }

        private void AutoSelect_KeepSmallest_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoSelect(g => g.Files.OrderBy(f => f.Size).FirstOrDefault(), "Keep smallest");
            UpdateDuplicateStats();
        }

        private void UpdateDuplicateStats()
        {
            if (Duplicates == null || !Duplicates.Any())
            {
                DuplicateStatsText.Text = "No duplicates found";
                return;
            }

            var groups = _duplicateManagementService.ApplyAutoSelectRules(
                Duplicates.ToList(),
                AutoSelectRule.None); // Just for stats calculation

            var stats = _duplicateManagementService.CalculateStatistics(groups);

            var selectedFiles = Duplicates.SelectMany(d => d.Files).Count(f => f.IsSelected);
            var selectedSize = Duplicates.SelectMany(d => d.Files).Where(f => f.IsSelected).Sum(f => f.Size);

            DuplicateStatsText.Text = $"Selected: {selectedFiles:N0} files ({FormatSize(selectedSize)}) | " +
                                     $"Total Wasted: {FormatSize(stats.TotalWastedSpace)}";
        }

        private async void DeleteSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = Duplicates?.SelectMany(d => d.Files).Where(f => f.IsSelected).ToList();
            if (selectedFiles == null || !selectedFiles.Any())
            {
                MessageBox.Show("No files selected for deletion.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalSize = selectedFiles.Sum(f => f.Size);
            var result = MessageBox.Show(
                $"Delete {selectedFiles.Count:N0} selected duplicate files ({FormatSize(totalSize)})?\n\n" +
                "Files will be sent to the Recycle Bin and can be restored if needed.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var progressWindow = new Window
            {
                Title = "Deleting Files",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "Deleting files...", FontSize = 14, Margin = new Thickness(0, 0, 0, 10) },
                        new ProgressBar { Name = "DeleteProgressBar", Height = 25, IsIndeterminate = false, Maximum = 100 }
                    }
                }
            };

            var progress = new Progress<int>(percent =>
            {
                var progressBar = ((StackPanel)progressWindow.Content).Children.OfType<ProgressBar>().First();
                progressBar.Value = percent;
            });

            progressWindow.Show();

            var deleteResult = await _fileOperationsService.DeleteFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(),
                useRecycleBin: true,
                progress);

            progressWindow.Close();

            if (deleteResult.Success)
            {
                MessageBox.Show(
                    $"Successfully deleted {deleteResult.SuccessCount:N0} files ({FormatSize(deleteResult.TotalSize)}).",
                    "Deletion Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh the view
                foreach (var file in selectedFiles)
                {
                    foreach (var dup in Duplicates.ToList())
                    {
                        dup.Files.Remove(file);
                        if (dup.Files.Count < 2)
                        {
                            Duplicates.Remove(dup);
                        }
                    }
                    AllFiles.Remove(file);
                }
            }
            else
            {
                var errorMsg = $"Deleted {deleteResult.SuccessCount:N0} files, {deleteResult.FailedCount:N0} failed.\n\n";
                if (deleteResult.Errors.Any())
                {
                    errorMsg += "First few errors:\n" + string.Join("\n", deleteResult.Errors.Take(5));
                }
                MessageBox.Show(errorMsg, "Deletion Completed with Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void MoveSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = Duplicates?.SelectMany(d => d.Files).Where(f => f.IsSelected).ToList();
            if (selectedFiles == null || !selectedFiles.Any())
            {
                MessageBox.Show("No files selected to move.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Simple folder path input dialog
            var inputDialog = new Window
            {
                Title = "Select Destination Folder",
                Width = 500,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel { Margin = new Thickness(15) };
            stackPanel.Children.Add(new TextBlock { Text = "Enter destination folder path:", Margin = new Thickness(0, 0, 0, 10) });

            var pathTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            stackPanel.Children.Add(pathTextBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75 };

            bool dialogResult = false;
            okButton.Click += (s, args) => { dialogResult = true; inputDialog.Close(); };
            cancelButton.Click += (s, args) => { dialogResult = false; inputDialog.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);

            inputDialog.Content = stackPanel;
            inputDialog.ShowDialog();

            if (!dialogResult || string.IsNullOrWhiteSpace(pathTextBox.Text))
                return;

            var destinationFolder = pathTextBox.Text.Trim();
            var totalSize = selectedFiles.Sum(f => f.Size);

            var result = MessageBox.Show(
                $"Move {selectedFiles.Count:N0} files ({FormatSize(totalSize)}) to:\n{destinationFolder}?",
                "Confirm Move",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var progressWindow = new Window
            {
                Title = "Moving Files",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "Moving files...", FontSize = 14, Margin = new Thickness(0, 0, 0, 10) },
                        new ProgressBar { Name = "MoveProgressBar", Height = 25, IsIndeterminate = false, Maximum = 100 }
                    }
                }
            };

            var progress = new Progress<int>(percent =>
            {
                var progressBar = ((StackPanel)progressWindow.Content).Children.OfType<ProgressBar>().First();
                progressBar.Value = percent;
            });

            progressWindow.Show();

            var moveResult = await _fileOperationsService.MoveFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(),
                destinationFolder,
                progress);

            progressWindow.Close();

            MessageBox.Show(
                $"Moved {moveResult.SuccessCount:N0} files successfully.\n" +
                (moveResult.FailedCount > 0 ? $"Failed: {moveResult.FailedCount:N0}\n" : "") +
                $"Time: {moveResult.Duration.TotalSeconds:F1}s",
                "Move Complete",
                MessageBoxButton.OK,
                moveResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async void CompressSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = Duplicates?.SelectMany(d => d.Files).Where(f => f.IsSelected).ToList();
            if (selectedFiles == null || !selectedFiles.Any())
            {
                MessageBox.Show("No files selected to compress.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ZIP Archive|*.zip",
                Title = "Save Compressed Archive",
                FileName = $"duplicates_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            var zipPath = saveDialog.FileName;
            var totalSize = selectedFiles.Sum(f => f.Size);

            var result = MessageBox.Show(
                $"Compress {selectedFiles.Count:N0} files ({FormatSize(totalSize)}) to:\n{zipPath}?",
                "Confirm Compression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var progressWindow = new Window
            {
                Title = "Compressing Files",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "Compressing files...", FontSize = 14, Margin = new Thickness(0, 0, 0, 10) },
                        new ProgressBar { Name = "CompressProgressBar", Height = 25, IsIndeterminate = false, Maximum = 100 }
                    }
                }
            };

            var progress = new Progress<int>(percent =>
            {
                var progressBar = ((StackPanel)progressWindow.Content).Children.OfType<ProgressBar>().First();
                progressBar.Value = percent;
            });

            progressWindow.Show();

            var compressResult = await _fileOperationsService.CompressFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(),
                zipPath,
                progress);

            progressWindow.Close();

            if (compressResult.Success)
            {
                var zipFileInfo = new FileInfo(zipPath);
                var compressionRatio = (1.0 - (double)zipFileInfo.Length / totalSize) * 100;

                MessageBox.Show(
                    $"Successfully compressed {compressResult.SuccessCount:N0} files.\n\n" +
                    $"Original Size: {FormatSize(totalSize)}\n" +
                    $"Compressed Size: {FormatSize(zipFileInfo.Length)}\n" +
                    $"Compression Ratio: {compressionRatio:F1}%\n" +
                    $"Time: {compressResult.Duration.TotalSeconds:F1}s",
                    "Compression Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Compression completed with errors.\n" +
                    $"Successful: {compressResult.SuccessCount:N0}\n" +
                    $"Failed: {compressResult.FailedCount:N0}",
                    "Compression Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelected_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.E)
            {
                OpenInExplorerSelected();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F)
            {
                ApplyFilters_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5)
            {
                ScanButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                StopScanButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.D1 || e.Key == Key.NumPad1)
                {
                    AutoSelect_KeepNewest_AllGroups_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                }
                if (e.Key == Key.D2 || e.Key == Key.NumPad2)
                {
                    AutoSelect_KeepOldest_AllGroups_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                }
                if (e.Key == Key.D3 || e.Key == Key.NumPad3)
                {
                    AutoSelect_KeepOnePerFolder_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                }
            }
        }

        private void OpenInExplorerSelected()
        {
            if (DuplicatesTreeView.IsKeyboardFocusWithin && DuplicatesTreeView.SelectedItem is FileSystemNode dupFile && File.Exists(dupFile.FullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{dupFile.FullPath}\"");
                return;
            }

            if (ResultsDataGrid.IsKeyboardFocusWithin && ResultsDataGrid.SelectedItem is FileSystemNode file && File.Exists(file.FullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
                return;
            }
        }
        #endregion

        #region HTML Report
        private async void ExportHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null)
            {
                MessageBox.Show("Please run a scan before exporting a report.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "HTML file (*.html)|*.html",
                FileName = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // Snapshot collections to avoid modification during write
                    var all = AllFiles?.ToList() ?? new List<FileSystemNode>();
                    var dups = Duplicates?.ToList() ?? new List<DuplicateSet>();
                    var types = FileTypes?.ToList() ?? new List<FileTypeStats>();
                    var ages = FileAgeStats?.ToList() ?? new List<FileAgeStats>();
                    var largest = LargestFiles?
                        .OrderByDescending(f => f.Size)
                        .Take(200) // cap for readability
                        .ToList() ?? new List<FileSystemNode>();
                    var empty = EmptyFolders?.ToList() ?? new List<FileSystemNode>();

                    await Task.Run(() => HtmlReportExporter.Export(
                        path: sfd.FileName,
                        rootNode: root,
                        duplicateGroups: Duplicates?.ToList() ?? new List<DuplicateSet>(),
                        lastRule: _lastAutoSelectRuleDescription
                    ));


                    MessageBox.Show($"Report exported:\n{sfd.FileName}", "Export HTML",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export HTML report: {ex.Message}",
                        "Export HTML", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExportCsvReport_Click(object sender, RoutedEventArgs e)
        {
            await ExportReport("CSV");
        }

        private async void ExportJsonReport_Click(object sender, RoutedEventArgs e)
        {
            await ExportReport("JSON");
        }

        private async void ExportEnhancedHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            await ExportReport("HTML");
        }

        private async Task ExportReport(string format)
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null)
            {
                MessageBox.Show("Please run a scan before exporting a report.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var extension = format.ToLower();
            var filter = format switch
            {
                "CSV" => "CSV file (*.csv)|*.csv",
                "JSON" => "JSON file (*.json)|*.json",
                "HTML" => "HTML file (*.html)|*.html",
                _ => "All files (*.*)|*.*"
            };

            var sfd = new SaveFileDialog
            {
                Filter = filter,
                FileName = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var exportOptions = new ExportOptions
                    {
                        IncludeFileList = true,
                        IncludeDuplicates = true,
                        IncludeFileTypes = true,
                        IncludeLargestFiles = true,
                        IncludeTemporaryFiles = true,
                        IncludeStaleFiles = true,
                        MaxFilesToExport = 10000
                    };

                    var exportData = _exportService.PrepareExportData(
                        root.FullPath,
                        root,
                        AllFiles?.ToList() ?? new List<FileSystemNode>(),
                        Duplicates?.ToList() ?? new List<DuplicateSet>(),
                        FileTypes?.ToList() ?? new List<FileTypeStats>(),
                        LargestFiles?.ToList() ?? new List<FileSystemNode>(),
                        TemporaryFileCategories?.ToList() ?? new List<TemporaryFileCategory>(),
                        StaleFiles?.ToList() ?? new List<StaleFileInfo>(),
                        exportOptions
                    );

                    await Task.Run(() =>
                    {
                        string content = format switch
                        {
                            "CSV" => _exportService.ExportToCsv(exportData, exportOptions),
                            "JSON" => _exportService.ExportToJson(exportData),
                            "HTML" => _exportService.ExportToHtml(exportData, exportOptions),
                            _ => throw new NotSupportedException($"Format {format} not supported")
                        };

                        File.WriteAllText(sfd.FileName, content);
                    });

                    MessageBox.Show($"{format} report exported successfully:\n{sfd.FileName}",
                        $"Export {format}", MessageBoxButton.OK, MessageBoxImage.Information);

                    _logger.Info($"Exported {format} report to {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export {format} report: {ex.Message}",
                        $"Export {format}", MessageBoxButton.OK, MessageBoxImage.Error);
                    _logger.Error($"Export {format} failed", ex);
                }
            }
        }
        #endregion

        #region Quick Filters
        private void QuickFilter_AllFiles_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("All");
        private void QuickFilter_ModifiedToday_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("ModifiedToday");
        private void QuickFilter_ModifiedThisWeek_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("ModifiedThisWeek");
        private void QuickFilter_ModifiedThisMonth_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("ModifiedThisMonth");
        private void QuickFilter_LargerThan100MB_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("LargerThan100MB");
        private void QuickFilter_LargerThan1GB_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("LargerThan1GB");
        private void QuickFilter_Videos_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("Videos");
        private void QuickFilter_Images_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("Images");
        private void QuickFilter_Documents_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("Documents");
        private void QuickFilter_Archives_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter("Archives");

        private void ApplyQuickFilter(string filterType)
        {
            if (AllFiles == null || AllFiles.Count == 0) return;

            SelectedFiles.Clear();

            var filtered = filterType == "All"
                ? AllFiles.ToList()
                : _advancedAnalysisService.ApplyQuickFilter(AllFiles.ToList(), filterType);

            foreach (var file in filtered)
            {
                SelectedFiles.Add(file);
            }

            _logger.Info($"Quick filter applied: {filterType}, Results: {filtered.Count}");
        }
        #endregion

        #region Advanced Search
        private void ExecuteSearch_Click(object sender, RoutedEventArgs e)
        {
            if (AllFiles == null || AllFiles.Count == 0)
            {
                MessageBox.Show("Please run a scan first before searching.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var criteria = BuildSearchCriteria();
                var results = _searchService.Search(AllFiles.ToList(), criteria);

                SearchResults.Clear();
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }

                _searchService.AddToHistory(criteria);
                UpdateSearchStatus(results.Count, criteria);
                _logger.Info($"Search executed: {_searchService.GetSearchSummary(criteria)}, Results: {results.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Search error: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.Error("Search execution failed", ex);
            }
        }

        private SearchCriteria BuildSearchCriteria()
        {
            var searchNameText = FindName("SearchNameTextBox") as TextBox;
            var useRegexCheckBox = FindName("UseRegexCheckBox") as CheckBox;
            var caseSensitiveCheckBox = FindName("CaseSensitiveCheckBox") as CheckBox;
            var minSizeTextBox = FindName("MinSizeTextBox") as TextBox;
            var maxSizeTextBox = FindName("MaxSizeTextBox") as TextBox;
            var modifiedAfterPicker = FindName("ModifiedAfterPicker") as DatePicker;
            var modifiedBeforePicker = FindName("ModifiedBeforePicker") as DatePicker;
            var extensionTextBox = FindName("ExtensionTextBox") as TextBox;
            var fileTypeComboBox = FindName("FileTypeComboBox") as ComboBox;
            var pathContainsTextBox = FindName("PathContainsTextBox") as TextBox;

            var criteria = new SearchCriteria
            {
                NamePattern = string.IsNullOrWhiteSpace(searchNameText?.Text) ? null : searchNameText.Text,
                UseRegex = useRegexCheckBox?.IsChecked == true,
                CaseSensitive = caseSensitiveCheckBox?.IsChecked == true,
                Extension = string.IsNullOrWhiteSpace(extensionTextBox?.Text) ? null : extensionTextBox.Text,
                PathContains = string.IsNullOrWhiteSpace(pathContainsTextBox?.Text) ? null : pathContainsTextBox.Text,
                ModifiedAfter = modifiedAfterPicker?.SelectedDate,
                ModifiedBefore = modifiedBeforePicker?.SelectedDate
            };

            // Parse size filters
            if (!string.IsNullOrWhiteSpace(minSizeTextBox?.Text) && long.TryParse(minSizeTextBox.Text, out long minSize))
            {
                criteria.MinSize = minSize * 1024 * 1024; // Assume MB
            }

            if (!string.IsNullOrWhiteSpace(maxSizeTextBox?.Text) && long.TryParse(maxSizeTextBox.Text, out long maxSize))
            {
                criteria.MaxSize = maxSize * 1024 * 1024; // Assume MB
            }

            // File type filter
            if (fileTypeComboBox?.SelectedIndex > 0)
            {
                var selectedItem = fileTypeComboBox.SelectedItem as ComboBoxItem;
                var tag = selectedItem?.Tag?.ToString();
                if (tag == "FilesOnly")
                    criteria.IsDirectory = false;
                else if (tag == "FoldersOnly")
                    criteria.IsDirectory = true;
            }

            return criteria;
        }

        private void UpdateSearchStatus(int resultCount, SearchCriteria criteria)
        {
            var statusTextBlock = FindName("SearchStatusTextBlock") as TextBlock;
            if (statusTextBlock != null)
            {
                statusTextBlock.Text = $"Found {resultCount:N0} results - {_searchService.GetSearchSummary(criteria)}";
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            // Clear all search fields
            var searchNameText = FindName("SearchNameTextBox") as TextBox;
            var minSizeTextBox = FindName("MinSizeTextBox") as TextBox;
            var maxSizeTextBox = FindName("MaxSizeTextBox") as TextBox;
            var extensionTextBox = FindName("ExtensionTextBox") as TextBox;
            var pathContainsTextBox = FindName("PathContainsTextBox") as TextBox;
            var modifiedAfterPicker = FindName("ModifiedAfterPicker") as DatePicker;
            var modifiedBeforePicker = FindName("ModifiedBeforePicker") as DatePicker;
            var fileTypeComboBox = FindName("FileTypeComboBox") as ComboBox;
            var useRegexCheckBox = FindName("UseRegexCheckBox") as CheckBox;
            var caseSensitiveCheckBox = FindName("CaseSensitiveCheckBox") as CheckBox;

            if (searchNameText != null) searchNameText.Text = string.Empty;
            if (minSizeTextBox != null) minSizeTextBox.Text = string.Empty;
            if (maxSizeTextBox != null) maxSizeTextBox.Text = string.Empty;
            if (extensionTextBox != null) extensionTextBox.Text = string.Empty;
            if (pathContainsTextBox != null) pathContainsTextBox.Text = string.Empty;
            if (modifiedAfterPicker != null) modifiedAfterPicker.SelectedDate = null;
            if (modifiedBeforePicker != null) modifiedBeforePicker.SelectedDate = null;
            if (fileTypeComboBox != null) fileTypeComboBox.SelectedIndex = 0;
            if (useRegexCheckBox != null) useRegexCheckBox.IsChecked = false;
            if (caseSensitiveCheckBox != null) caseSensitiveCheckBox.IsChecked = false;

            SearchResults.Clear();

            var statusTextBlock = FindName("SearchStatusTextBlock") as TextBlock;
            if (statusTextBlock != null)
            {
                statusTextBlock.Text = "Search cleared";
            }
        }
        #endregion

        #region Trends & Analytics
        private void RefreshTrends_Click(object sender, RoutedEventArgs e)
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null)
            {
                MessageBox.Show("Please run a scan first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var analysis = _trendsService.AnalyzeTrends(root.FullPath);
            var breakdown = _trendsService.GetSpaceBreakdown(AllFiles?.ToList() ?? new List<FileSystemNode>());

            // Update Trends Summary
            var trendsSummaryTextBlock = FindName("TrendsSummaryTextBlock") as TextBlock;
            if (trendsSummaryTextBlock != null)
            {
                trendsSummaryTextBlock.Text = analysis.Summary;
            }

            // Update Recommendations
            var trendsRecommendationTextBlock = FindName("TrendsRecommendationTextBlock") as TextBlock;
            if (trendsRecommendationTextBlock != null)
            {
                trendsRecommendationTextBlock.Text = analysis.Recommendation;
            }

            // Update Predictions
            var predictionsList = FindName("PredictionsList") as ItemsControl;
            if (predictionsList != null)
            {
                var predictions = _trendsService.GetGrowthPredictions(analysis);
                predictionsList.ItemsSource = predictions;
            }

            // Update Extension Breakdown
            var extensionBreakdownList = FindName("ExtensionBreakdownList") as ItemsControl;
            if (extensionBreakdownList != null)
            {
                var extData = breakdown.ByExtension
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .Select(kv => $"{kv.Key}: {FormatSize(kv.Value)}")
                    .ToList();
                extensionBreakdownList.ItemsSource = extData;
            }

            // Update Age Breakdown
            var ageBreakdownList = FindName("AgeBreakdownList") as ItemsControl;
            if (ageBreakdownList != null)
            {
                var ageData = breakdown.ByAgeCategory
                    .Select(kv => $"{kv.Key}: {FormatSize(kv.Value)}")
                    .ToList();
                ageBreakdownList.ItemsSource = ageData;
            }

            // Update Category Breakdown
            var categoryBreakdownList = FindName("CategoryBreakdownList") as ItemsControl;
            if (categoryBreakdownList != null)
            {
                var catData = breakdown.ByCategory
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}: {FormatSize(kv.Value)}")
                    .ToList();
                categoryBreakdownList.ItemsSource = catData;
            }

            _logger.Info("Trends refreshed successfully");
        }

        private void GenerateRecommendations_Click(object sender, RoutedEventArgs e)
        {
            if (AllFiles == null || !AllFiles.Any())
            {
                MessageBox.Show("Please run a scan first to get cleanup recommendations.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Generate recommendations
                var recommendations = _cleanupRecommendationService.GenerateRecommendations(AllFiles.ToList());

                // Update summary
                var summary = _cleanupRecommendationService.GetOverallSummary(recommendations);
                RecommendationSummaryTextBlock.Text = summary;

                // Update recommendations list
                CleanupRecommendations.Clear();
                foreach (var recommendation in recommendations)
                {
                    CleanupRecommendations.Add(recommendation);
                }

                // Bind to the ItemsControl
                RecommendationsList.ItemsSource = CleanupRecommendations;

                _logger.Info($"Generated {recommendations.Count} cleanup recommendations");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating recommendations: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.Error($"Error generating recommendations: {ex.Message}");
            }
        }

        private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            var rootNode = RootNodes.FirstOrDefault();
            if (rootNode == null || AllFiles == null || !AllFiles.Any())
            {
                MessageBox.Show("Please run a scan first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Generate dashboard data
                var dashboard = _dashboardService.GenerateDashboard(AllFiles.ToList(), rootNode);
                var summary = _dashboardService.GetDashboardSummary(dashboard);

                // Update summary
                DashboardSummaryTextBlock.Text = summary;

                // Update file type chart (with calculated bar widths)
                var maxTypeSize = dashboard.FileTypeData.Any() ? dashboard.FileTypeData.Max(ft => ft.Size) : 1;
                var fileTypeChartData = dashboard.FileTypeData.Select(ft => new
                {
                    ft.Extension,
                    ft.FormattedSize,
                    BarWidth = (ft.Size / (double)maxTypeSize) * 250 // Max bar width 250px
                }).ToList();
                FileTypeChartList.ItemsSource = fileTypeChartData;

                // Update largest folders chart
                LargestFoldersChartList.ItemsSource = dashboard.LargestFolders;

                // Update disk usage
                TotalScannedText.Text = dashboard.FormattedTotalSize;
                DiskUsedText.Text = dashboard.FormattedUsedSpace;
                DiskFreeText.Text = dashboard.FormattedFreeSpace;
                UsagePercentageText.Text = $"{dashboard.UsagePercentage:F1}%";

                // Update gauge (using StrokeDashArray to create arc effect)
                var circumference = 2 * Math.PI * 42.5; // radius = 42.5 (half of 100 - strokeThickness)
                var dashLength = (dashboard.UsagePercentage / 100.0) * circumference;
                var gapLength = circumference - dashLength;
                UsageGaugeEllipse.StrokeDashArray = new System.Windows.Media.DoubleCollection { dashLength, gapLength };

                // Set gauge color based on usage
                var gaugeColor = dashboard.UsagePercentage switch
                {
                    >= 90 => "#F44336", // Red
                    >= 75 => "#FF9800", // Orange
                    >= 50 => "#FFC107", // Yellow
                    _ => "#4CAF50"      // Green
                };
                UsageGaugeEllipse.Stroke = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(gaugeColor));

                _logger.Info("Dashboard refreshed successfully");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing dashboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.Error($"Error refreshing dashboard: {ex.Message}");
            }
        }
        #endregion

        #region Filtering
        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            if (AllFiles == null) return;
            SelectedFiles.Clear();
            var filtered = ApplyFilters(AllFiles);
            foreach (var file in filtered)
            {
                SelectedFiles.Add(file);
            }
        }

        private IEnumerable<FileSystemNode> ApplyFilters(IEnumerable<FileSystemNode> files)
        {
            var criteria = new FilterCriteria
            {
                SizeFilter = (SizeFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                Extensions = ExtensionFilterTextBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().Replace("*", "")).ToArray(),
                DateFilter = (DateFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                StartDate = StartDatePicker?.SelectedDate,
                EndDate = EndDatePicker?.SelectedDate
            };

            return _filterService.ApplyFilters(files, criteria);
        }
        #endregion

    }

    // ===== Self-contained HTML report exporter =====
    internal static class HtmlReportExporter
    {
        public static void Export(string path, FileSystemNode rootNode, List<DuplicateSet> duplicateGroups, string lastRule)
        {
            var sb = new StringBuilder(1 << 20);

            var treemapItems = rootNode.Children?
                .Where(c => c.Size > 0)
                .Select(c => (name: Safe(c.FullPath), size: c.Size))
                .ToList()
                ?? new List<(string name, long size)>();

            var dupRows = duplicateGroups
                .Select(g => new
                {
                    name = Safe(g.FileName),
                    count = g.Files?.Count ?? 0,
                    sizeEach = g.Files?.FirstOrDefault()?.Size ?? 0L,
                    saving = (g.Files?.FirstOrDefault()?.Size ?? 0L) * Math.Max(0, (g.Files?.Count ?? 0) - 1),
                    files = g.Files?.Select(f => Safe(f.FullPath)).ToList() ?? new List<string>()
                })
                .OrderByDescending(r => r.saving)
                .ToList();

            sb.AppendLine("<!doctype html><html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>FileSizeAnalyzer Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
body{font-family:Segoe UI,system-ui,Arial,sans-serif;background:#0b0d10;color:#e8e8e8;margin:0}
header{padding:16px 20px;border-bottom:1px solid #2a2f36}
h1{margin:0;font-size:20px}
.container{padding:16px 20px}
.section{margin:20px 0}
.card{background:#12151a;border:1px solid #2a2f36;border-radius:8px;padding:12px}
.grid{display:grid;gap:12px}
#treemap{width:100%;height:420px;background:#0f1116;border:1px solid #2a2f36;border-radius:8px;position:relative;overflow:hidden}
.tile{position:absolute;border:1px solid rgba(255,255,255,0.1);box-sizing:border-box}
.tile span{position:absolute;left:6px;top:6px;font-size:12px;color:#fff;text-shadow:0 1px 2px rgba(0,0,0,.6)}
table{width:100%;border-collapse:collapse}
th,td{padding:8px 10px;border-bottom:1px solid #2a2f36;font-size:14px}
th{text-align:left;color:#aeb4be}
tr:hover{background:#151a22}
kbd{background:#1b1f27;border:1px solid #2a2f36;border-radius:4px;padding:2px 6px;font-size:12px}
.small{opacity:.75}
details summary{cursor:pointer}
");
            sb.AppendLine("</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<header><h1>FileSizeAnalyzer Report</h1><div class='small'>Generated " + Safe(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "</div></header>");
            sb.AppendLine("<div class='container grid'>");

            sb.AppendLine("<div class='section card'>");
            sb.AppendLine("<h2 style='margin:0 0 8px 0'>Treemap (top-level)</h2>");
            sb.AppendLine("<div id='treemap'></div>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class='section card'>");
            sb.AppendLine("<h2 style='margin:0 0 8px 0'>Duplicate Groups</h2>");
            sb.AppendLine("<div class='small'>Sorted by potential savings. Last auto-select rule: <b>" + Safe(lastRule) + "</b></div>");
            sb.AppendLine("<table><thead><tr><th>Name</th><th>Count</th><th>Size (each)</th><th>Potential Savings</th><th>Files</th></tr></thead><tbody>");

            foreach (var r in dupRows)
            {
                sb.Append("<tr>");
                sb.Append("<td>").Append(r.name).Append("</td>");
                sb.Append("<td>").Append(r.count).Append("</td>");
                sb.Append("<td>").Append(FormatBytes(r.sizeEach)).Append("</td>");
                sb.Append("<td>").Append(FormatBytes(r.saving)).Append("</td>");
                sb.Append("<td><details><summary>Show paths</summary><ul style='margin:6px 0 0 18px'>");
                foreach (var p in r.files) sb.Append("<li>").Append(p).Append("</li>");
                sb.Append("</ul></details></td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class='section card small'>");
            sb.AppendLine("<b>Keyboard Shortcuts</b>: <kbd>Del</kbd> delete selected, <kbd>Ctrl+E</kbd> open in Explorer, <kbd>Ctrl+F</kbd> apply filters, <kbd>F5</kbd> scan, <kbd>Esc</kbd> stop, <kbd>Ctrl+1/2/3</kbd> select rules.");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");

            sb.AppendLine("<script>");
            sb.Append("const treemapData = [");
            for (int i = 0; i < treemapItems.Count; i++)
            {
                var it = treemapItems[i];
                sb.Append("{name:\"").Append(it.name).Append("\",size:").Append(it.size).Append("}");
                if (i < treemapItems.Count - 1) sb.Append(",");
            }
            sb.AppendLine("];");

            sb.AppendLine(@"
function formatBytes(n){if(n===0)return '0 B';const u=['B','KB','MB','GB','TB'];let i=Math.floor(Math.log(n)/Math.log(1024));i=Math.min(i,u.length-1);return (n/Math.pow(1024,i)).toFixed(2)+' '+u[i];}
function color(i){const hues=[205,260,180,330,20,45,140,190,280,10];return `hsl(${hues[i%hues.length]},60%,35%)`;}

function drawTreemap(){
  const el = document.getElementById('treemap');
  const W = el.clientWidth, H = el.clientHeight;
  const total = treemapData.reduce((a,b)=>a+b.size,0)||1;

  let x=0,y=0;
  const horiz = W>=H;
  treemapData.sort((a,b)=>b.size-a.size);
  for(let i=0;i<treemapData.length;i++){
    const item = treemapData[i];
    const frac = item.size/total;
    if(horiz){
      const ww = Math.max(1, Math.round(W*frac));
      addTile(x,0,ww,H,item.name,item.size,i);
      x += ww;
    }else{
      const hh = Math.max(1, Math.round(H*frac));
      addTile(0,y,W,hh,item.name,item.size,i);
      y += hh;
    }
  }

  function addTile(x,y,w,h,name,size,i){
    const d = document.createElement('div');
    d.className='tile';
    d.style.left=x+'px'; d.style.top=y+'px';
    d.style.width=w+'px'; d.style.height=h+'px';
    d.style.background=color(i);
    const s=document.createElement('span');
    s.textContent = (w>80&&h>30)? (name.split(/[\\/]/).slice(-1)[0]+' • '+formatBytes(size)) : '';
    d.title = name+'\n'+formatBytes(size);
    d.appendChild(s);
    el.appendChild(d);
  }
}
window.addEventListener('load', drawTreemap);
window.addEventListener('resize', ()=>{const el=document.getElementById('treemap'); el.innerHTML=''; drawTreemap();});
");
            sb.AppendLine("</script>");

            sb.AppendLine("</body></html>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string Safe(string s) =>
            string.IsNullOrEmpty(s) ? "" :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&#39;");

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = (int)Math.Floor(Math.Log(bytes, 1024));
            i = Math.Min(i, u.Length - 1);
            double v = bytes / Math.Pow(1024, i);
            return $"{v:0.##} {u[i]}";
        }
    }

    public class BreadcrumbItem
    {
        public string Name { get; set; } = string.Empty;
        public FileSystemNode? Node { get; set; }
        public bool ShowSeparator { get; set; }
    }
}