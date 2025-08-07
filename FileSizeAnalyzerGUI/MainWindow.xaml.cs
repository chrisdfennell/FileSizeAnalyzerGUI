using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        private CancellationTokenSource _cts;
        private readonly StringBuilder _scanErrors;
        private DateTime _lastStatusUpdateTime; // For throttling UI updates

        private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        public MainWindow()
        {
            InitializeComponent();

            RootNodes = new ObservableCollection<FileSystemNode>();
            AllFiles = new ObservableCollection<FileSystemNode>();
            SelectedFiles = new ObservableCollection<FileSystemNode>();
            Duplicates = new ObservableCollection<DuplicateSet>();
            FileTypes = new ObservableCollection<FileTypeStats>();
            FileAgeStats = new ObservableCollection<FileAgeStats>();
            LargestFiles = new ObservableCollection<FileSystemNode>();
            EmptyFolders = new ObservableCollection<FileSystemNode>();
            ScanHistory = new ObservableCollection<ScanHistoryEntry>();
            _scanErrors = new StringBuilder();

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
            string scanPath = DirectoryPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
            {
                MessageBox.Show($"The specified path '{scanPath}' does not exist.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
                else // Is a file
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

            bool skipSystem = SkipSystemFilesCheckBox.IsChecked == true;
            bool skipWindows = SkipWindowsDirCheckBox.IsChecked == true;

            try
            {
                await Task.Run(() => ScanDirectoryRecursive(scanPath, null, nodeProgress, textProgress, _cts.Token, skipSystem, skipWindows), _cts.Token);

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
                ReportsTextBox.Text = "Scan cancelled by user.\n" + _scanErrors.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unhandled error occurred: {ex.Message}", "Error");
                ReportsTextBox.Text = $"Error: {ex.Message}\n" + _scanErrors.ToString();
            }
            finally
            {
                ResetUIAfterScan();
                _cts?.Dispose();
            }
        }

        private long ScanDirectoryRecursive(string path, FileSystemNode parent, IProgress<FileSystemNode> nodeProgress, IProgress<string> textProgress, CancellationToken token, bool skipSystem, bool skipWindows)
        {
            if (token.IsCancellationRequested) return 0;

            if (skipWindows && path.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if ((DateTime.Now - _lastStatusUpdateTime).TotalMilliseconds > 100)
            {
                textProgress.Report($"Scanning: {path}");
                _lastStatusUpdateTime = DateTime.Now;
            }

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(path);
                // ####################################################################
                // ## BUG FIX: Added 'parent != null' to ensure the root scan
                // ## directory is NEVER skipped, even if it has the System attribute.
                // ####################################################################
                if (parent != null && skipSystem && dirInfo.Attributes.HasFlag(FileAttributes.System))
                {
                    return 0;
                }
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Error accessing '{path}': {ex.Message}");
                return 0;
            }

            var currentNode = new FileSystemNode
            {
                FullPath = dirInfo.FullName,
                IsDirectory = true,
                Parent = parent,
                CreationTime = dirInfo.CreationTime,
                LastWriteTime = dirInfo.LastWriteTime,
                Icon = IconManager.GetIcon(dirInfo.FullName, true)
            };
            nodeProgress.Report(currentNode);

            long currentSize = 0;

            try
            {
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if (token.IsCancellationRequested) return 0;

                    if (skipSystem && file.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    currentSize += file.Length;
                    var fileNode = new FileSystemNode
                    {
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Parent = currentNode,
                        Size = file.Length,
                        CreationTime = file.CreationTime,
                        LastWriteTime = file.LastWriteTime,
                        Icon = IconManager.GetIcon(file.FullName, false)
                    };
                    nodeProgress.Report(fileNode);
                }
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Error reading files in '{path}': {ex.Message}");
            }

            try
            {
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    currentSize += ScanDirectoryRecursive(subDir.FullName, currentNode, nodeProgress, textProgress, token, skipSystem, skipWindows);
                }
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Error reading subdirectories in '{path}': {ex.Message}");
            }

            currentNode.Size = currentSize;
            currentNode.FormattedSize = FormatSize(currentSize);

            return currentSize;
        }

        private async Task FinalizeAnalysisAsync(string scanPath)
        {
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

            var analysisTasks = new List<Task>
            {
                Task.Run(() => FindDuplicates(allFoundFiles, duplicateProgress, _cts.Token)),
                Task.Run(() => FindEmptyFolders(rootNode, emptyFolderProgress, _cts.Token)),
                Task.Run(() => GetFileTypeStats(allFoundFiles, fileTypeProgress, _cts.Token)),
                Task.Run(() => GetFileAgeStats(allFoundFiles, fileAgeProgress, _cts.Token))
            };

            await Task.WhenAll(analysisTasks);

            ScanHistory.Add(new ScanHistoryEntry { ScanDate = DateTime.Now, Path = scanPath, TotalSize = rootNode.Size });
            ReportsTextBox.Text = GenerateReport(rootNode.Size) + "\n--- Scan Errors ---\n" + _scanErrors.ToString();
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
            ScanProgressBar.IsIndeterminate = true;
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
            TreemapCanvas.Children.Clear();
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

        #region Analysis and Filtering
        private string GenerateReport(long totalSize) =>
            $"Scan Report - {DateTime.Now}\n" +
            $"Directory: {DirectoryPathTextBox.Text}\n" +
            $"Total Size: {FormatSize(totalSize)}\n" +
            $"Total Files Found: {AllFiles?.Count ?? 0}\n" +
            $"Duplicates Found: {Duplicates?.Count ?? 0}\n" +
            $"Empty Folders Found: {EmptyFolders?.Count ?? 0}\n";

        private void GetFileTypeStats(List<FileSystemNode> files, IProgress<FileTypeStats> progress, CancellationToken token)
        {
            if (files == null) return;

            var stats = files.GroupBy(f => f.Extension)
                 .Select(g => new FileTypeStats
                 {
                     Extension = string.IsNullOrEmpty(g.Key) ? "No Extension" : g.Key,
                     TotalSize = g.Sum(f => f.Size),
                     FileCount = g.Count()
                 }).OrderByDescending(s => s.TotalSize);

            foreach (var stat in stats)
            {
                if (token.IsCancellationRequested) return;
                progress.Report(stat);
            }
        }

        private void GetFileAgeStats(List<FileSystemNode> files, IProgress<FileAgeStats> progress, CancellationToken token)
        {
            if (files == null) return;
            var now = DateTime.Now;
            var stats = new Dictionary<string, FileAgeStats>
            {
                { "Last Month", new FileAgeStats { Category = "Last Month" } },
                { "Last Year", new FileAgeStats { Category = "Last Year" } },
                { "Older Than 1 Year", new FileAgeStats { Category = "Older Than 1 Year" } }
            };

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) return;
                if (file.LastWriteTime >= now.AddMonths(-1))
                {
                    stats["Last Month"].AddFile(file.Size);
                }
                else if (file.LastWriteTime >= now.AddYears(-1))
                {
                    stats["Last Year"].AddFile(file.Size);
                }
                else
                {
                    stats["Older Than 1 Year"].AddFile(file.Size);
                }
            }

            foreach (var stat in stats.Values.Where(s => s.FileCount > 0))
            {
                if (token.IsCancellationRequested) return;
                progress.Report(stat);
            }
        }

        private void FindDuplicates(List<FileSystemNode> files, IProgress<DuplicateSet> progress, CancellationToken token)
        {
            if (files == null || files.Count < 2) return;

            var filesBySize = new Dictionary<long, List<FileSystemNode>>();
            foreach (var file in files)
            {
                if (token.IsCancellationRequested) return;
                if (file.Size <= 4096) continue;

                if (!filesBySize.TryGetValue(file.Size, out var list))
                {
                    list = new List<FileSystemNode>();
                    filesBySize[file.Size] = list;
                }
                list.Add(file);
            }

            foreach (var sizeGroup in filesBySize.Values)
            {
                if (token.IsCancellationRequested) return;
                if (sizeGroup.Count < 2) continue;

                var filesByPartialHash = new Dictionary<string, List<FileSystemNode>>();
                foreach (var file in sizeGroup)
                {
                    if (token.IsCancellationRequested) return;
                    string partialHash = ComputePartialHash(file.FullPath);
                    if (string.IsNullOrEmpty(partialHash)) continue;

                    if (!filesByPartialHash.TryGetValue(partialHash, out var list))
                    {
                        list = new List<FileSystemNode>();
                        filesByPartialHash[partialHash] = list;
                    }
                    list.Add(file);
                }

                foreach (var partialHashGroup in filesByPartialHash.Values)
                {
                    if (token.IsCancellationRequested) return;
                    if (partialHashGroup.Count < 2) continue;

                    var filesByFullHash = new Dictionary<string, List<FileSystemNode>>();
                    foreach (var file in partialHashGroup)
                    {
                        if (token.IsCancellationRequested) return;
                        string fullHash = ComputeFastHash(file.FullPath);
                        if (string.IsNullOrEmpty(fullHash)) continue;

                        if (!filesByFullHash.TryGetValue(fullHash, out var list))
                        {
                            list = new List<FileSystemNode>();
                            filesByFullHash[fullHash] = list;
                        }
                        list.Add(file);
                    }

                    foreach (var confirmedGroup in filesByFullHash.Values)
                    {
                        if (token.IsCancellationRequested) return;
                        if (confirmedGroup.Count > 1)
                        {
                            var duplicateSet = new DuplicateSet
                            {
                                FileName = Path.GetFileName(confirmedGroup.First().FullPath),
                                Count = confirmedGroup.Count(),
                                FormattedSize = FormatSize(confirmedGroup.First().Size * confirmedGroup.Count()),
                                Icon = confirmedGroup.First().Icon,
                                Files = new ObservableCollection<FileSystemNode>(confirmedGroup)
                            };
                            progress.Report(duplicateSet);
                        }
                    }
                }
            }
        }

        private string ComputePartialHash(string filePath, int bytesToRead = 4096)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length == 0) return "EMPTY";

                if (stream.Length < bytesToRead) return ComputeFastHash(filePath);

                byte[] buffer = new byte[bytesToRead];
                int bytesRead = stream.Read(buffer, 0, bytesToRead);

                var hasher = new System.IO.Hashing.XxHash64();
                hasher.Append(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
                byte[] hash = hasher.GetHashAndReset();
                return BitConverter.ToString(hash).Replace("-", "");
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Could not perform partial hash on file {filePath}: {ex.Message}");
                return null;
            }
        }

        private void FindEmptyFolders(FileSystemNode root, IProgress<FileSystemNode> progress, CancellationToken token)
        {
            if (root == null) return;
            var stack = new Stack<FileSystemNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested) return;
                var current = stack.Pop();
                if (current.IsDirectory && !current.Children.Any())
                {
                    progress.Report(current);
                }

                if (current.Children == null) continue;
                foreach (var child in current.Children.Reverse())
                {
                    stack.Push(child);
                }
            }
        }

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
            var filteredFiles = files;
            if (SizeFilterComboBox.SelectedItem is ComboBoxItem sizeItem && sizeItem.Content.ToString() != "All Sizes")
            {
                long minSize = sizeItem.Content.ToString() switch
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

            string[] extensionsToFilter = ExtensionFilterTextBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().Replace("*", "")).ToArray();
            if (extensionsToFilter.Any())
            {
                filteredFiles = filteredFiles.Where(f => extensionsToFilter.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));
            }

            if (DateFilterComboBox.SelectedItem is ComboBoxItem dateItem && dateItem.Content.ToString() != "All Dates")
            {
                var now = DateTime.Now;
                filteredFiles = dateItem.Content.ToString() switch
                {
                    "Last Month" => filteredFiles.Where(f => f.LastWriteTime >= now.AddMonths(-1)),
                    "Last Year" => filteredFiles.Where(f => f.LastWriteTime >= now.AddYears(-1)),
                    "Older Than 1 Year" => filteredFiles.Where(f => f.LastWriteTime < now.AddYears(-1)),
                    "Custom Range" when StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue =>
                        filteredFiles.Where(f => f.LastWriteTime >= StartDatePicker.SelectedDate.Value && f.LastWriteTime <= EndDatePicker.SelectedDate.Value),
                    _ => filteredFiles
                };
            }
            return filteredFiles;
        }
        #endregion

        #region UI Handlers
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
            if (SelectedFiles == null || !SelectedFiles.Any())
            {
                MessageBox.Show("No data to export.");
                return;
            }
            string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "file_size_report.csv");
            await Task.Run(() => CsvExporter.ExportToCsv(SelectedFiles.ToList(), csvPath));
            MessageBox.Show($"Exported to {csvPath}");
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
        private void AboutButton_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

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
                    ScanButton_Click(null, null); // Rescan to refresh data
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error moving item to Recycle Bin: {ex.Message}");
                }
            }
        }

        private async void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private async void DuplicatesTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (DuplicatesTreeView.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private void ScanHistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ScanHistoryDataGrid.SelectedItem is ScanHistoryEntry se) { DirectoryPathTextBox.Text = se.Path; ScanButton_Click(null, null); } }
        private async void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await PreviewFileAsync(sf); }

        private async Task PreviewFileAsync(FileSystemNode file)
        {
            if (file == null) return;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewMessage.Text = "";

            PreviewTextBox.FontFamily = new FontFamily("Consolas");

            try
            {
                if (file.IsDirectory)
                {
                    PreviewMessage.Text = "Select a file to preview.";
                    return;
                }

                string[] textExtensions = { ".txt", ".log", ".xml", ".cs", ".xaml", ".json", ".config", ".md", ".py", ".js" };
                string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

                if (textExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(file.FullPath);
                    char[] buffer = new char[4096];
                    int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                    PreviewTextBox.Text = new string(buffer, 0, charsRead);
                    PreviewTextBox.Visibility = Visibility.Visible;
                }
                else if (imageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(file.FullPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                else
                {
                    PreviewMessage.Text = $"Preview not supported for '{file.Extension}' files.";
                }
            }
            catch (Exception ex)
            {
                PreviewMessage.Text = $"Error previewing file: {ex.Message}";
            }
        }
        #endregion

        #region Treemap and Visualization
        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => DrawTreemap();
        private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTreemap();

        private void DrawTreemap()
        {
            TreemapCanvas.Children.Clear();
            FileSystemNode currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? RootNodes.FirstOrDefault();
            if (currentNode == null) return;
            if (!currentNode.IsDirectory) currentNode = currentNode.Parent;
            if (currentNode == null) return;

            var nodesToDraw = currentNode.Children.Where(n => n.Size > 0).ToList();
            if (!nodesToDraw.Any()) return;

            var totalSize = (double)nodesToDraw.Sum(n => n.Size);
            var bounds = new Rect(0, 0, TreemapCanvas.ActualWidth, TreemapCanvas.ActualHeight);
            RenderTreemapNodes(nodesToDraw, bounds, totalSize);
        }

        private void RenderTreemapNodes(List<FileSystemNode> nodes, Rect bounds, double totalSize)
        {
            if (!nodes.Any() || bounds.Width <= 1 || bounds.Height <= 1) return;

            var node = nodes.First();
            var remainingNodes = nodes.Skip(1).ToList();
            double nodeArea = (node.Size / totalSize) * (bounds.Width * bounds.Height);

            Rect nodeRect, remainingBounds;
            if (bounds.Width > bounds.Height)
            {
                double nodeWidth = Math.Min(bounds.Width, node.Size > 0 ? nodeArea / bounds.Height : 0);
                nodeRect = new Rect(bounds.Left, bounds.Top, nodeWidth, bounds.Height);
                remainingBounds = new Rect(bounds.Left + nodeWidth, bounds.Top, bounds.Width - nodeWidth, bounds.Height);
            }
            else
            {
                double nodeHeight = Math.Min(bounds.Height, node.Size > 0 ? nodeArea / bounds.Width : 0);
                nodeRect = new Rect(bounds.Left, bounds.Top, bounds.Width, nodeHeight);
                remainingBounds = new Rect(bounds.Left, bounds.Top + nodeHeight, bounds.Width, bounds.Height - nodeHeight);
            }

            var border = new Border
            {
                Width = nodeRect.Width,
                Height = nodeRect.Height,
                Background = new SolidColorBrush(GetRandomColor()),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                ToolTip = $"{node.FullPath}\nSize: {FormatSize(node.Size)}",
                Tag = node
            };

            if (border.Width > 30 && border.Height > 15)
            {
                border.Child = new TextBlock
                {
                    Text = Path.GetFileName(node.FullPath),
                    Foreground = Brushes.White,
                    Margin = new Thickness(2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                };
            }

            border.MouseLeftButtonDown += (s, e) => TreemapRectangle_Click(node);
            Canvas.SetLeft(border, nodeRect.Left);
            Canvas.SetTop(border, nodeRect.Top);
            TreemapCanvas.Children.Add(border);

            if (remainingNodes.Any())
            {
                RenderTreemapNodes(remainingNodes, remainingBounds, remainingNodes.Sum(n => n.Size));
            }
        }

        private void TreemapRectangle_Click(FileSystemNode node)
        {
            var nodeToSelect = node.IsDirectory ? node : node.Parent;
            if (nodeToSelect != null && MainTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Header.ToString() == "Directory Tree") is TabItem treeViewTab)
            {
                MainTabControl.SelectedItem = treeViewTab;
                Dispatcher.InvokeAsync(() => SelectTreeViewItem(nodeToSelect));
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

        private string ComputeFastHash(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length == 0) return "EMPTY";
                var hasher = new System.IO.Hashing.XxHash64();
                hasher.Append(stream);
                byte[] hash = hasher.GetHashAndReset();
                return BitConverter.ToString(hash).Replace("-", "");
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Could not hash file {filePath}: {ex.Message}");
                return Guid.NewGuid().ToString();
            }
        }

        public void UpdateAllSizesAndFormatting(FileSystemNode node)
        {
            if (node == null) return;
            if (node.IsDirectory)
            {
                long totalSize = 0;
                foreach (var child in node.Children)
                {
                    UpdateAllSizesAndFormatting(child);
                    totalSize += child.Size;
                }
                node.Size = totalSize;
            }
            node.FormattedSize = FormatSize(node.Size);
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
        #endregion
    }
}