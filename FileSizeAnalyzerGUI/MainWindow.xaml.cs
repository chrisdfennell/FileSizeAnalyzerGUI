using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
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
using System.Windows.Shapes;

namespace FileSizeAnalyzerGUI
{
    public partial class MainWindow : Window
    {
        private List<FileSystemNode> selectedFiles;
        private List<FileSystemNode> allFiles;
        private List<DuplicateSet> duplicates;
        private List<FileTypeStats> fileTypes;
        private List<FileAgeStats> fileAgeStats;
        private List<FileSystemNode> largestFiles;
        private List<FileSystemNode> emptyFolders;
        private FileSystemNode rootNode;
        private List<ScanHistoryEntry> scanHistory;
        private readonly ObservableCollection<FileSystemNode> rootNodes;
        private CancellationTokenSource cts;
        private readonly StringBuilder scanErrors;

        public MainWindow()
        {
            InitializeComponent();

            scanHistory = new List<ScanHistoryEntry>();
            rootNodes = new ObservableCollection<FileSystemNode>();
            scanErrors = new StringBuilder();

            PopulateDrives();
            ScanHistoryDataGrid.ItemsSource = scanHistory;
            DirectoryTreeView.ItemsSource = rootNodes;
            DirectoryTreeView.SelectedItemChanged += DirectoryTreeView_SelectedItemChanged;
        }

        #region Custom Window Chrome Handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }
        #endregion

        private void PopulateDrives()
        {
            try
            {
                DriveSelectionComboBox.ItemsSource = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                scanErrors.AppendLine($"Could not retrieve drive list: {ex.Message}");
            }
        }

        private void DriveSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveSelectionComboBox.SelectedItem is string drive)
            {
                DirectoryPathTextBox.Text = drive;
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            string scanPath = DirectoryPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(scanPath)) { scanPath = Directory.GetCurrentDirectory(); DirectoryPathTextBox.Text = scanPath; }
            if (!Directory.Exists(scanPath)) { MessageBox.Show($"The specified path '{scanPath}' does not exist.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            try
            {
                cts = new CancellationTokenSource();
                scanErrors.Clear();
                StopScanButton.IsEnabled = true;
                ScanButton.Visibility = Visibility.Collapsed;
                StopScanButton.Visibility = Visibility.Visible;

                await Dispatcher.InvokeAsync(() => {
                    ScanProgressBar.IsIndeterminate = true;
                    ScanProgressBar.Visibility = Visibility.Visible;
                    ProgressTextBlock.Text = "Preparing to scan...";
                    ProgressTextBlock.Visibility = Visibility.Visible;
                    rootNodes.Clear();
                    ResultsDataGrid.ItemsSource = null;
                    DuplicatesTreeView.ItemsSource = null;
                    FileTypesDataGrid.ItemsSource = null;
                    FileAgeDataGrid.ItemsSource = null;
                    LargestFilesDataGrid.ItemsSource = null;
                    EmptyFoldersDataGrid.ItemsSource = null;
                    TreemapCanvas.Children.Clear();
                    ReportsTextBox.Text = "";
                });

                var progress = new Progress<(int percent, string status)>(update => {
                    if (cts.IsCancellationRequested) return;
                    ScanProgressBar.IsIndeterminate = update.percent < 0;
                    if (!ScanProgressBar.IsIndeterminate) ScanProgressBar.Value = update.percent;
                    ProgressTextBlock.Text = update.status;
                });

                var scanner = new DirectoryScanner(progress, scanErrors);
                rootNode = await scanner.ScanDirectoryAsync(scanPath, cts.Token);

                if (cts.Token.IsCancellationRequested) { await ResetUIState("Scan cancelled by user.\n" + scanErrors.ToString()); return; }
                if (rootNode == null) { await ResetUIState("Scan failed.\n" + scanErrors.ToString()); return; }

                ProgressTextBlock.Text = "Scan complete. Calculating sizes and sorting...";
                await Task.Run(() => {
                    scanner.UpdateAllSizesAndFormatting(rootNode);
                    SortAllNodesBySize(rootNode);
                }, cts.Token);
                rootNodes.Add(rootNode);

                ProgressTextBlock.Text = "Analyzing files...";
                allFiles = GetAllFiles(rootNode).Where(f => !f.IsDirectory).ToList();
                ApplyFilters_Click(null, null);
                largestFiles = allFiles.OrderByDescending(f => f.Size).Take(100).ToList();
                emptyFolders = FindEmptyFolders(rootNode);
                duplicates = await Task.Run(() => FindDuplicates(allFiles), cts.Token);
                fileTypes = GetFileTypeStats();
                fileAgeStats = GetFileAgeStats();

                scanHistory.Add(new ScanHistoryEntry { ScanDate = DateTime.Now, Path = scanPath, TotalSize = rootNode.Size });
                string report = GenerateReport(rootNode.Size) + "\n--- Scan Errors ---\n" + scanErrors.ToString();

                await Dispatcher.InvokeAsync(() => {
                    DuplicatesTreeView.ItemsSource = duplicates;
                    FileTypesDataGrid.ItemsSource = fileTypes;
                    FileAgeDataGrid.ItemsSource = fileAgeStats;
                    LargestFilesDataGrid.ItemsSource = largestFiles;
                    EmptyFoldersDataGrid.ItemsSource = emptyFolders;
                    ScanHistoryDataGrid.ItemsSource = null;
                    ScanHistoryDataGrid.ItemsSource = scanHistory;
                    DrawTreemap();
                });

                await ResetUIState(report);
                ProgressTextBlock.Text = "Analysis complete.";
                await Task.Delay(2000);
            }
            catch (OperationCanceledException) { await ResetUIState("Scan cancelled by user.\n" + scanErrors.ToString()); }
            catch (Exception ex) { MessageBox.Show($"An error occurred: {ex.Message}", "Error"); await ResetUIState($"Error: {ex.Message}\n" + scanErrors.ToString()); }
            finally
            {
                await Dispatcher.InvokeAsync(() => {
                    ProgressTextBlock.Visibility = Visibility.Collapsed;
                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    StopScanButton.Visibility = Visibility.Collapsed;
                    ScanButton.Visibility = Visibility.Visible;
                });
                cts?.Dispose(); cts = null;
            }
        }

        private List<FileSystemNode> FindEmptyFolders(FileSystemNode root)
        {
            return GetAllFiles(root)
                .Where(node => node.IsDirectory && !node.Children.Any())
                .ToList();
        }

        private async Task ResetUIState(string reportMessage) => await Dispatcher.InvokeAsync(() => ReportsTextBox.Text = reportMessage);

        private void StopScanButton_Click(object sender, RoutedEventArgs e) { if (cts != null) { cts.Cancel(); StopScanButton.IsEnabled = false; } }

        private void DateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DateFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                bool isCustomRange = selectedItem.Content.ToString() == "Custom Range";
                if (StartDatePicker != null) StartDatePicker.Visibility = isCustomRange ? Visibility.Visible : Visibility.Collapsed;
                if (EndDatePicker != null) EndDatePicker.Visibility = isCustomRange ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            if (allFiles == null) return;
            selectedFiles = ApplyFilters(allFiles);
            ResultsDataGrid.ItemsSource = null;
            ResultsDataGrid.ItemsSource = selectedFiles;
        }

        #region Context Menu and Button Click Handlers
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.DataContext is FileSystemNode node)
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
            if ((sender as MenuItem)?.DataContext is FileSystemNode node)
            {
                await DeleteFileAsync(node);
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow
            {
                Owner = this
            };
            helpWindow.ShowDialog();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow
            {
                Owner = this
            };
            aboutWindow.ShowDialog();
        }
        #endregion

        private void SortAllNodesBySize(FileSystemNode node)
        {
            if (!node.IsDirectory || node.Children == null || node.Children.Count == 0) return;

            var sortedChildren = node.Children.OrderByDescending(c => c.Size).ToList();
            node.Children.Clear();
            foreach (var child in sortedChildren)
            {
                node.Children.Add(child);
                SortAllNodesBySize(child);
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
            if (selectedFiles == null || !selectedFiles.Any())
            {
                MessageBox.Show("No data to export.");
                return;
            }
            string csvPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "file_size_report.csv");
            await Task.Run(() => CsvExporter.ExportToCsv(selectedFiles, csvPath));
            MessageBox.Show($"Exported to {csvPath}");
        }

        private async Task DeleteFileAsync(FileSystemNode file)
        {
            string message = file.IsDirectory
                ? $"Are you sure you want to move the folder '{System.IO.Path.GetFileName(file.FullPath)}' and all its contents to the Recycle Bin?"
                : $"Are you sure you want to move '{System.IO.Path.GetFileName(file.FullPath)}' to the Recycle Bin?";

            var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() => {
                        if (file.IsDirectory)
                        {
                            FileSystem.DeleteDirectory(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                        else
                        {
                            FileSystem.DeleteFile(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                    });

                    MessageBox.Show("Item(s) moved to Recycle Bin.");

                    allFiles?.RemoveAll(f => f.FullPath.StartsWith(file.FullPath, StringComparison.OrdinalIgnoreCase));
                    selectedFiles?.RemoveAll(f => f.FullPath.StartsWith(file.FullPath, StringComparison.OrdinalIgnoreCase));
                    duplicates?.RemoveAll(d => d.Files.Any(f => f.FullPath.StartsWith(file.FullPath, StringComparison.OrdinalIgnoreCase)));
                    largestFiles?.RemoveAll(f => f.FullPath.StartsWith(file.FullPath, StringComparison.OrdinalIgnoreCase));
                    emptyFolders?.RemoveAll(f => f.FullPath.StartsWith(file.FullPath, StringComparison.OrdinalIgnoreCase));
                    file.Parent?.Children.Remove(file);

                    if (allFiles != null)
                    {
                        fileTypes = GetFileTypeStats();
                        fileAgeStats = GetFileAgeStats();
                        var scanner = new DirectoryScanner(null, scanErrors);
                        scanner.UpdateAllSizesAndFormatting(rootNode);

                        Dispatcher.Invoke(() => {
                            ResultsDataGrid.ItemsSource = null;
                            ResultsDataGrid.ItemsSource = selectedFiles;
                            DuplicatesTreeView.ItemsSource = null;
                            DuplicatesTreeView.ItemsSource = duplicates;
                            FileTypesDataGrid.ItemsSource = null;
                            FileTypesDataGrid.ItemsSource = fileTypes;
                            FileAgeDataGrid.ItemsSource = null;
                            FileAgeDataGrid.ItemsSource = fileAgeStats;
                            LargestFilesDataGrid.ItemsSource = null;
                            LargestFilesDataGrid.ItemsSource = largestFiles;
                            EmptyFoldersDataGrid.ItemsSource = null;
                            EmptyFoldersDataGrid.ItemsSource = emptyFolders;
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error moving item to Recycle Bin: {ex.Message}");
                }
            }
        }

        #region UI Event Handlers
        private async void ResultsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private async void DuplicatesTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (DuplicatesTreeView.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf); }
        private async void ScanHistoryDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (ScanHistoryDataGrid.SelectedItem is ScanHistoryEntry se) { DirectoryPathTextBox.Text = se.Path; await Dispatcher.InvokeAsync(() => ScanButton_Click(null, null)); } }
        private async void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await PreviewFileAsync(sf); }
        #endregion

        private async Task PreviewFileAsync(FileSystemNode file)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewMessage.Text = "";

            try
            {
                if (file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    string content = await Task.Run(() => File.ReadAllText(file.FullPath));
                    PreviewTextBox.Text = content;
                    PreviewTextBox.Visibility = Visibility.Visible;
                }
                else if (new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(file.FullPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                else
                {
                    PreviewMessage.Text = "Preview not supported for this file type.";
                }
            }
            catch (Exception ex)
            {
                PreviewMessage.Text = $"Error previewing file: {ex.Message}";
            }
        }

        #region Analysis and Reporting
        private string GenerateReport(long totalSize)
        {
            return $"Scan Report - {DateTime.Now}\n" +
                   $"Directory: {DirectoryPathTextBox.Text}\n" +
                   $"Total Size: {FormatSize(totalSize)}\n" +
                   $"Total Files Found: {allFiles?.Count ?? 0}\n" +
                   $"Duplicates Found: {duplicates?.Count ?? 0}\n" +
                   $"Empty Folders Found: {emptyFolders?.Count ?? 0}\n";
        }

        private List<FileSystemNode> GetTopFiles(FileSystemNode root, int count) => GetAllFiles(root).Where(f => !f.IsDirectory).OrderByDescending(f => f.Size).Take(count).ToList();

        private List<FileSystemNode> ApplyFilters(IEnumerable<FileSystemNode> files)
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

            string[] extensionsToFilter = ExtensionFilterTextBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().Replace("*", ""))
                .ToArray();
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

            return filteredFiles.ToList();
        }

        private List<DuplicateSet> FindDuplicates(List<FileSystemNode> files)
        {
            if (files == null) return new List<DuplicateSet>();

            return files.Where(f => f.Size > 0)
                .GroupBy(f => f.Size)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.GroupBy(f => ComputeFastHash(f.FullPath)))
                .Where(hg => hg.Count() > 1)
                .Select(hg => new DuplicateSet
                {
                    FileName = System.IO.Path.GetFileName(hg.First().FullPath),
                    Count = hg.Count(),
                    FormattedSize = FormatSize(hg.First().Size * hg.Count()),
                    Icon = hg.First().Icon,
                    Files = new ObservableCollection<FileSystemNode>(hg)
                })
                .OrderByDescending(ds => ds.Files.Sum(f => f.Size))
                .ToList();
        }

        private List<FileTypeStats> GetFileTypeStats()
        {
            if (allFiles == null) return new List<FileTypeStats>();
            return allFiles.GroupBy(f => f.Extension)
                           .Select(g => new FileTypeStats
                           {
                               Extension = string.IsNullOrEmpty(g.Key) ? "No Extension" : g.Key,
                               TotalSize = g.Sum(f => f.Size),
                               FileCount = g.Count()
                           }).OrderByDescending(s => s.TotalSize).ToList();
        }

        private List<FileAgeStats> GetFileAgeStats()
        {
            if (allFiles == null) return new List<FileAgeStats>();
            var now = DateTime.Now;
            var stats = new List<FileAgeStats>
            {
                new FileAgeStats { Category = "Last Month" },
                new FileAgeStats { Category = "Last Year" },
                new FileAgeStats { Category = "Older Than 1 Year" }
            };
            foreach (var file in allFiles)
            {
                if (file.LastWriteTime >= now.AddMonths(-1)) stats[0].AddFile(file.Size);
                else if (file.LastWriteTime >= now.AddYears(-1)) stats[1].AddFile(file.Size);
                else stats[2].AddFile(file.Size);
            }
            return stats.Where(s => s.FileCount > 0).ToList();
        }
        #endregion

        #region Treemap and Visualization
        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            DrawTreemap();
        }

        private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTreemap();

        private void DrawTreemap()
        {
            TreemapCanvas.Children.Clear();
            FileSystemNode currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? rootNode;
            if (currentNode == null || !currentNode.IsDirectory)
            {
                currentNode = currentNode?.Parent ?? rootNode;
            }
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

            double nodeArea = (node.Size / totalSize) * bounds.Width * bounds.Height;
            Rect nodeRect;
            Rect remainingBounds;

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
                var textBlock = new TextBlock
                {
                    Text = System.IO.Path.GetFileName(node.FullPath),
                    Foreground = Brushes.White,
                    Margin = new Thickness(2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                };
                border.Child = textBlock;
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

            if (nodeToSelect != null)
            {
                if (FindName("MainTabControl") is TabControl tabControl)
                {
                    var treeViewTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Header.ToString() == "Directory Tree");
                    if (treeViewTab != null)
                    {
                        tabControl.SelectedItem = treeViewTab;
                        Dispatcher.InvokeAsync(() => SelectTreeViewItem(nodeToSelect));
                    }
                }
            }
        }

        private void SelectTreeViewItem(FileSystemNode node)
        {
            var pathStack = new Stack<FileSystemNode>();
            var current = node;
            while (current != null)
            {
                pathStack.Push(current);
                current = current.Parent;
            }

            ItemsControl parentContainer = DirectoryTreeView;
            while (pathStack.Count > 0)
            {
                var itemToFind = pathStack.Pop();
                var currentContainer = parentContainer.ItemContainerGenerator.ContainerFromItem(itemToFind) as TreeViewItem;

                if (currentContainer == null)
                {
                    parentContainer.UpdateLayout();
                    currentContainer = parentContainer.ItemContainerGenerator.ContainerFromItem(itemToFind) as TreeViewItem;
                }

                if (currentContainer != null)
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
                    break;
                }
            }
        }

        private Color GetRandomColor()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());
            return Color.FromRgb((byte)random.Next(100, 220), (byte)random.Next(100, 220), (byte)random.Next(100, 220));
        }

        private IEnumerable<FileSystemNode> GetAllFiles(FileSystemNode node)
        {
            if (node == null) yield break;
            var stack = new Stack<FileSystemNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                foreach (var child in current.Children)
                {
                    stack.Push(child);
                }
            }
        }
        #endregion

        private string ComputeFastHash(string filePath)
        {
            try
            {
                const int bufferSize = 4096;
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
                if (stream.Length == 0) return "EMPTY";

                var hasher = new System.IO.Hashing.XxHash64();
                hasher.Append(stream);
                byte[] hash = hasher.GetHashAndReset();
                return BitConverter.ToString(hash).Replace("-", "");
            }
            catch (IOException ex)
            {
                scanErrors.AppendLine($"Could not hash file {filePath}: {ex.Message}");
                return Guid.NewGuid().ToString();
            }
            catch (Exception ex)
            {
                scanErrors.AppendLine($"An unexpected error occurred while hashing {filePath}: {ex.Message}");
                return Guid.NewGuid().ToString();
            }
        }
    }
}
