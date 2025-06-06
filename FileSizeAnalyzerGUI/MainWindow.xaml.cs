using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FileSizeAnalyzerGUI
{
    public partial class MainWindow : Window
    {
        private List<FileSystemNode> selectedFiles;
        private List<FileSystemNode> allFiles;
        private List<FileSystemNode> duplicates;
        private List<FileTypeStats> fileTypes;
        private List<FileAgeStats> fileAgeStats;
        private FileSystemNode rootNode;
        private List<ScanHistoryEntry> scanHistory;
        private ObservableCollection<FileSystemNode> rootNodes;
        private CancellationTokenSource cts;
        private StringBuilder scanErrors;

        public MainWindow()
        {
            InitializeComponent();
            scanHistory = new List<ScanHistoryEntry>();
            ScanHistoryDataGrid.ItemsSource = scanHistory;
            rootNodes = new ObservableCollection<FileSystemNode>();
            DirectoryTreeView.ItemsSource = rootNodes;
            scanErrors = new StringBuilder();
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            string scanPath = DirectoryPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(scanPath)) scanPath = Directory.GetCurrentDirectory();

            try
            {
                cts = new CancellationTokenSource();

                ScanButton.Visibility = Visibility.Collapsed;
                StopScanButton.Visibility = Visibility.Visible;
                StopScanButton.IsEnabled = true;

                scanErrors.Clear();
                await Dispatcher.InvokeAsync(() =>
                {
                    ScanProgressBar.IsIndeterminate = false;
                    ScanProgressBar.Value = 0;
                    ScanProgressBar.Visibility = Visibility.Visible;
                    ProgressTextBlock.Text = "Scanning... 0%";
                    ProgressTextBlock.Visibility = Visibility.Visible;
                    ResultsDataGrid.ItemsSource = null;
                    DuplicatesDataGrid.ItemsSource = null;
                    FileTypesDataGrid.ItemsSource = null;
                    FileAgeDataGrid.ItemsSource = null;
                    rootNodes.Clear();
                    TreemapCanvas.Children.Clear();
                    ReportsTextBox.Text = "";
                    PreviewMessage.Text = "Select a file to preview";
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewTextBox.Visibility = Visibility.Collapsed;
                }, System.Windows.Threading.DispatcherPriority.Background);

                var progress = new Progress<(int percent, List<FileSystemNode> nodes)>(async update =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (update.percent < 0)
                        {
                            ScanProgressBar.IsIndeterminate = true;
                            ProgressTextBlock.Text = "Scanning... (Estimating)";
                        }
                        else
                        {
                            ScanProgressBar.IsIndeterminate = false;
                            ScanProgressBar.Value = update.percent;
                            ProgressTextBlock.Text = $"Scanning... {update.percent}%";
                        }

                        // Update formatted sizes and bar widths for all nodes in the batch
                        if (update.nodes != null)
                        {
                            foreach (var node in update.nodes)
                            {
                                UpdateFormattedSizes(node);
                            }
                            // Pass all nodes scanned so far to CalculateBarWidths
                            if (rootNodes.Any())
                            {
                                var allNodes = new List<FileSystemNode>();
                                foreach (var root in rootNodes)
                                {
                                    allNodes.Add(root);
                                    CollectNodes(root, allNodes);
                                }
                                CalculateBarWidths(allNodes);
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                });

                var scanner = new DirectoryScanner(progress, scanErrors);
                rootNode = await Task.Run(() => scanner.ScanDirectoryAsync(scanPath, cts.Token));

                await Dispatcher.InvokeAsync(() =>
                {
                    if (rootNode != null)
                    {
                        rootNodes.Add(rootNode);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                if (cts.Token.IsCancellationRequested)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ReportsTextBox.Text = "Scan cancelled by user.\n" + scanErrors.ToString();
                        ScanProgressBar.Visibility = Visibility.Collapsed;
                        ProgressTextBlock.Visibility = Visibility.Collapsed;
                        ScanButton.Visibility = Visibility.Visible;
                        StopScanButton.Visibility = Visibility.Collapsed;
                    }, System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }

                allFiles = GetAllFiles(rootNode).Where(f => !f.IsDirectory).ToList();
                selectedFiles = ApplyFilters(GetTopFiles(rootNode, 50));
                duplicates = FindDuplicates(allFiles);
                fileTypes = GetFileTypeStats();
                fileAgeStats = GetFileAgeStats();

                var totalSize = allFiles.Sum(f => f.Size);
                scanHistory.Add(new ScanHistoryEntry
                {
                    ScanDate = DateTime.Now,
                    Path = scanPath,
                    TotalSize = totalSize
                });

                string report = GenerateReport(totalSize) + "\n" + scanErrors.ToString();

                await Dispatcher.InvokeAsync(() =>
                {
                    ResultsDataGrid.ItemsSource = selectedFiles;
                    DuplicatesDataGrid.ItemsSource = duplicates;
                    FileTypesDataGrid.ItemsSource = fileTypes;
                    FileAgeDataGrid.ItemsSource = fileAgeStats;
                    ScanHistoryDataGrid.ItemsSource = null;
                    ScanHistoryDataGrid.ItemsSource = scanHistory;
                    ReportsTextBox.Text = report;
                    DrawTreemap();

                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    ProgressTextBlock.Visibility = Visibility.Collapsed;
                    ScanButton.Visibility = Visibility.Visible;
                    StopScanButton.Visibility = Visibility.Collapsed;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ReportsTextBox.Text = "Scan cancelled by user.\n" + scanErrors.ToString();
                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    ProgressTextBlock.Visibility = Visibility.Collapsed;
                    ScanButton.Visibility = Visibility.Visible;
                    StopScanButton.Visibility = Visibility.Collapsed;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    ReportsTextBox.Text += $"\nError during scan: {ex.Message}\n";
                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    ProgressTextBlock.Visibility = Visibility.Collapsed;
                    ProgressTextBlock.Text = "Scan failed.";
                    ScanButton.Visibility = Visibility.Visible;
                    StopScanButton.Visibility = Visibility.Collapsed;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            finally
            {
                cts?.Dispose();
                cts = null;
            }
        }

        private async void StopScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (cts != null)
            {
                cts.Cancel();
                StopScanButton.IsEnabled = false;
            }
        }

        private void CalculateBarWidths(List<FileSystemNode> allNodes)
        {
            if (allNodes == null || !allNodes.Any()) return;

            // Find the maximum size among all nodes
            var maxSize = allNodes.Max(n => n.Size);
            if (maxSize == 0) return;

            const double maxBarWidth = 200;
            foreach (var n in allNodes)
            {
                n.BarWidth = (n.Size / (double)maxSize) * maxBarWidth;
                n.BarFill = new SolidColorBrush(GetRandomColor());
            }
        }

        private void UpdateFormattedSizes(FileSystemNode node)
        {
            if (node == null) return;

            node.FormattedSize = FormatSize(node.Size);
            foreach (var child in node.Children)
            {
                UpdateFormattedSizes(child);
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 0) return "0 bytes"; // Handle negative sizes (shouldn't happen)
            if (bytes < 1024)
                return $"{bytes} bytes";

            double kb = bytes / 1024.0;
            if (kb < 1024)
                return $"{kb:F1} KB";

            double mb = kb / 1024.0;
            if (mb < 1024)
                return $"{mb:F1} MB";

            double gb = mb / 1024.0;
            return $"{gb:F1} GB";
        }

        private void CollectNodes(FileSystemNode node, List<FileSystemNode> nodes)
        {
            foreach (var child in node.Children)
            {
                nodes.Add(child);
                CollectNodes(child, nodes);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select a folder to scan"
            };

            if (dialog.ShowDialog() == true)
            {
                DirectoryPathTextBox.Text = dialog.FolderName;
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                MessageBox.Show("No data to export.");
                return;
            }

            string csvPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "top_50_files.csv");
            await Task.Run(() => CsvExporter.ExportToCsv(selectedFiles, csvPath));
            MessageBox.Show($"Exported to {csvPath}");
        }

        private async void ResultsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is FileSystemNode selectedFile)
            {
                await DeleteFileAsync(selectedFile);
            }
        }

        private async void DuplicatesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DuplicatesDataGrid.SelectedItem is FileSystemNode selectedFile)
            {
                await DeleteFileAsync(selectedFile);
            }
        }

        private async void ScanHistoryDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ScanHistoryDataGrid.SelectedItem is ScanHistoryEntry selectedEntry)
            {
                DirectoryPathTextBox.Text = selectedEntry.Path;
                await Dispatcher.InvokeAsync(() => ScanButton_Click(null, null));
            }
        }

        private async void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is FileSystemNode selectedFile)
            {
                await PreviewFileAsync(selectedFile);
            }
        }

        private async Task PreviewFileAsync(FileSystemNode file)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewTextBox.Visibility = Visibility.Collapsed;
                PreviewMessage.Text = "";
            });

            try
            {
                if (file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    string content = await Task.Run(() => File.ReadAllText(file.FullPath));
                    await Dispatcher.InvokeAsync(() =>
                    {
                        PreviewTextBox.Text = content;
                        PreviewTextBox.Visibility = Visibility.Visible;
                    });
                }
                else if (file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(file.FullPath);
                        bitmap.EndInit();
                        PreviewImage.Source = bitmap;
                        PreviewImage.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        PreviewMessage.Text = "Preview not supported for this file type.";
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    PreviewMessage.Text = $"Error previewing file: {ex.Message}";
                });
            }
        }

        private async Task DeleteFileAsync(FileSystemNode file)
        {
            var result = MessageBox.Show($"Are you sure you want to delete {file.FullPath}?", "Confirm Delete", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() => File.Delete(file.FullPath));
                    MessageBox.Show("File deleted successfully.");

                    if (selectedFiles != null) selectedFiles.Remove(file);
                    if (duplicates != null) duplicates.Remove(file);
                    if (allFiles != null) allFiles.Remove(file);
                    if (allFiles != null)
                    {
                        fileTypes = GetFileTypeStats();
                        fileAgeStats = GetFileAgeStats();
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ResultsDataGrid.ItemsSource = null;
                        ResultsDataGrid.ItemsSource = selectedFiles;
                        DuplicatesDataGrid.ItemsSource = null;
                        DuplicatesDataGrid.ItemsSource = duplicates;
                        FileTypesDataGrid.ItemsSource = null;
                        FileTypesDataGrid.ItemsSource = fileTypes;
                        FileAgeDataGrid.ItemsSource = null;
                        FileAgeDataGrid.ItemsSource = fileAgeStats;
                        DrawTreemap();
                        PreviewMessage.Text = "Select a file to preview";
                        PreviewImage.Visibility = Visibility.Collapsed;
                        PreviewTextBox.Visibility = Visibility.Collapsed;

                        // Update sizes for parent directories
                        UpdateSizesUpward(file.Parent);

                        // Collect all nodes and update bar widths
                        var allNodes = new List<FileSystemNode>();
                        if (rootNodes.Any())
                        {
                            foreach (var root in rootNodes)
                            {
                                allNodes.Add(root);
                                CollectNodes(root, allNodes);
                            }
                        }
                        CalculateBarWidths(allNodes);
                        UpdateFormattedSizes(rootNode);
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting file: {ex.Message}");
                }
            }
        }

        private void UpdateSizesUpward(FileSystemNode node)
        {
            if (node == null) return;

            node.Size = node.Children.Sum(c => c.Size);
            UpdateSizesUpward(node.Parent);
        }

        private string GenerateReport(long totalSize)
        {
            return $"Scan Report - {DateTime.Now}\n" +
                   $"Directory: {DirectoryPathTextBox.Text}\n" +
                   $"Total Size: {totalSize} bytes\n" +
                   $"Largest Files: {selectedFiles?.Count ?? 0}\n" +
                   $"Duplicates: {duplicates?.Count ?? 0}\n" +
                   $"File Types: {fileTypes?.Count ?? 0}\n" +
                   $"File Age Categories: {fileAgeStats?.Count ?? 0}\n";
        }

        private List<FileSystemNode> GetTopFiles(FileSystemNode root, int topPercentage)
        {
            var files = GetAllFiles(root).Where(f => !f.IsDirectory).ToList();
            var totalSize = files.Sum(f => f.Size);
            var thresholdSize = totalSize * (topPercentage / 100.0);

            var sortedFiles = files.OrderByDescending(f => f.Size).ToList();
            var selected = new List<FileSystemNode>();
            long cumulativeSize = 0;

            foreach (var file in sortedFiles)
            {
                if (cumulativeSize >= thresholdSize) break;
                selected.Add(file);
                cumulativeSize += file.Size;
            }

            return selected;
        }

        private List<FileSystemNode> ApplyFilters(List<FileSystemNode> files)
        {
            if (files == null) return new List<FileSystemNode>();

            var filteredFiles = files.AsEnumerable();

            string sizeFilter = (SizeFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (sizeFilter != "All Sizes")
            {
                long minSize = sizeFilter switch
                {
                    "> 1MB" => 1 * 1024 * 1024,
                    "> 10MB" => 10 * 1024 * 1024,
                    "> 100MB" => 100 * 1024 * 1024,
                    _ => 0
                };
                filteredFiles = filteredFiles.Where(f => f.Size >= minSize);
            }

            string extFilter = (ExtensionFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (extFilter != "All Types")
            {
                filteredFiles = filteredFiles.Where(f => f.Extension.Equals(extFilter, StringComparison.OrdinalIgnoreCase));
            }

            string dateFilter = (DateFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (dateFilter != "All Dates")
            {
                var now = DateTime.Now;
                filteredFiles = dateFilter switch
                {
                    "Last Month" => filteredFiles.Where(f => f.LastWriteTime >= now.AddMonths(-1)),
                    "Last Year" => filteredFiles.Where(f => f.LastWriteTime >= now.AddYears(-1)),
                    "Older Than 1 Year" => filteredFiles.Where(f => f.LastWriteTime < now.AddYears(-1)),
                    _ => filteredFiles
                };
            }

            return filteredFiles.ToList();
        }

        private List<FileSystemNode> FindDuplicates(List<FileSystemNode> files)
        {
            if (files == null) return new List<FileSystemNode>();

            var duplicates = new List<FileSystemNode>();
            var groupedBySize = files.GroupBy(f => f.Size).Where(g => g.Count() > 1);

            foreach (var group in groupedBySize)
            {
                var groupedByName = group.GroupBy(f => System.IO.Path.GetFileName(f.FullPath));
                foreach (var nameGroup in groupedByName.Where(ng => ng.Count() > 1))
                {
                    foreach (var file in nameGroup)
                    {
                        var fileInfo = new FileInfo(file.FullPath);
                        bool isCloudOnly = (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                                           (fileInfo.Attributes & (FileAttributes)0x4000) != 0;
                        if (isCloudOnly)
                        {
                            file.DuplicateCount = nameGroup.Count();
                            duplicates.Add(file);
                            continue;
                        }

                        var groupedByHash = nameGroup.GroupBy(f => ComputeMD5(f.FullPath));
                        foreach (var hashGroup in groupedByHash.Where(hg => hg.Count() > 1))
                        {
                            foreach (var f in hashGroup)
                            {
                                f.DuplicateCount = hashGroup.Count();
                                duplicates.Add(f);
                            }
                        }
                    }
                }
            }

            return duplicates;
        }

        private List<FileTypeStats> GetFileTypeStats()
        {
            if (allFiles == null) return new List<FileTypeStats>();

            return allFiles.GroupBy(f => f.Extension)
                           .Select(g => new FileTypeStats
                           {
                               Extension = g.Key,
                               TotalSize = g.Sum(f => f.Size),
                               FileCount = g.Count()
                           }).ToList();
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
                if (file.LastWriteTime >= now.AddMonths(-1))
                    stats[0].AddFile(file.Size);
                else if (file.LastWriteTime >= now.AddYears(-1))
                    stats[1].AddFile(file.Size);
                else
                    stats[2].AddFile(file.Size);
            }

            return stats.Where(s => s.FileCount > 0).ToList();
        }

        private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawTreemap();
        }

        private void DrawTreemap()
        {
            if (rootNode == null) return;

            TreemapCanvas.Children.Clear();
            var files = GetAllFiles(rootNode).Take(50).ToList();
            var totalSize = files.Sum(f => f.Size);
            double canvasWidth = TreemapCanvas.ActualWidth;
            double canvasHeight = TreemapCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            DrawTreemapRecursive(files.OrderByDescending(f => f.Size).ToList(), 0, 0, canvasWidth, canvasHeight, totalSize);
        }

        private void DrawTreemapRecursive(List<FileSystemNode> nodes, double x, double y, double width, double height, double totalSize)
        {
            if (nodes.Count == 0 || width < 1 || height < 1) return;

            var node = nodes.First();
            double area = (node.Size / totalSize) * (width * height);
            double nodeWidth, nodeHeight;

            if (width > height)
            {
                nodeWidth = area / height;
                nodeHeight = height;
            }
            else
            {
                nodeWidth = width;
                nodeHeight = area / width;
            }

            if (nodeWidth < 1 || nodeHeight < 1) return;

            var rect = new Rectangle
            {
                Width = nodeWidth,
                Height = nodeHeight,
                Fill = new SolidColorBrush(GetRandomColor()),
                ToolTip = $"{node.FullPath} ({node.Size} bytes)"
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            TreemapCanvas.Children.Add(rect);

            var remainingNodes = nodes.Skip(1).ToList();
            if (width > height)
            {
                DrawTreemapRecursive(remainingNodes, x + nodeWidth, y, width - nodeWidth, height, totalSize);
            }
            else
            {
                DrawTreemapRecursive(remainingNodes, x, y + nodeHeight, width, height - nodeHeight, totalSize);
            }
        }

        private Color GetRandomColor()
        {
            var random = new Random();
            return Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        }

        private IEnumerable<FileSystemNode> GetAllFiles(FileSystemNode node)
        {
            if (node == null) yield break;
            if (!node.IsDirectory) yield return node;
            foreach (var child in node.Children)
                foreach (var descendant in GetAllFiles(child))
                    yield return descendant;
        }

        private string ComputeMD5(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }
    }

    public class DirectoryScanner
    {
        private readonly List<FileSystemNode> allScannedNodes;
        private readonly IProgress<(int percent, List<FileSystemNode> nodes)> progress;
        private readonly StringBuilder scanErrors;
        private int totalItems;
        private int scannedItems;
        private List<FileSystemNode> batchNodes;
        private DateTime lastUpdateTime;

        public DirectoryScanner(IProgress<(int percent, List<FileSystemNode> nodes)> progress, StringBuilder scanErrors)
        {
            this.progress = progress;
            this.scanErrors = scanErrors;
            batchNodes = new List<FileSystemNode>();
            allScannedNodes = new List<FileSystemNode>();
            lastUpdateTime = DateTime.Now;
        }

        public async Task<FileSystemNode> ScanDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Directory not found: {path}");

            progress?.Report((-1, null));

            totalItems = await Task.Run(() => EstimateTotalItems(path), cancellationToken);
            scannedItems = 0;
            allScannedNodes.Clear();

            var root = new FileSystemNode
            {
                FullPath = path,
                IsDirectory = true,
                CreationTime = await Task.Run(() => new DirectoryInfo(path).CreationTime, cancellationToken),
                LastWriteTime = await Task.Run(() => new DirectoryInfo(path).LastWriteTime, cancellationToken),
                Extension = "",
                Children = new ObservableCollection<FileSystemNode>()
            };

            await ScanDirectoryRecursiveAsync(root, null, cancellationToken);
            await FlushBatchAsync();

            return root;
        }

        private int EstimateTotalItems(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length +
                       Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Length;
            }
            catch
            {
                return 1000;
            }
        }

        private async Task ScanDirectoryRecursiveAsync(FileSystemNode node, FileSystemNode parent, CancellationToken cancellationToken)
        {
            node.Parent = parent;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string[] files = null;
                string[] directories = null;

                await Task.Run(() =>
                {
                    try
                    {
                        files = Directory.GetFiles(node.FullPath);
                    }
                    catch (Exception ex)
                    {
                        scanErrors.AppendLine($"Error enumerating files in {node.FullPath}: {ex.Message}");
                        files = new string[0];
                    }

                    try
                    {
                        directories = Directory.GetDirectories(node.FullPath);
                    }
                    catch (Exception ex)
                    {
                        scanErrors.AppendLine($"Error enumerating directories in {node.FullPath}: {ex.Message}");
                        directories = new string[0];
                    }
                }, cancellationToken);

                var tasks = new List<Task>();

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        FileInfo fileInfo = null;
                        await Task.Run(() =>
                        {
                            fileInfo = new FileInfo(file);
                        }, cancellationToken);

                        var attributes = fileInfo.Attributes;
                        bool isCloudOnly = (attributes & FileAttributes.ReparsePoint) != 0 ||
                                           (attributes & (FileAttributes)0x4000) != 0;

                        var fileNode = new FileSystemNode
                        {
                            FullPath = file,
                            Size = isCloudOnly ? 0 : fileInfo.Length,
                            IsDirectory = false,
                            CreationTime = fileInfo.CreationTime,
                            LastWriteTime = fileInfo.LastWriteTime,
                            Extension = fileInfo.Extension,
                            Children = new ObservableCollection<FileSystemNode>()
                        };

                        if (isCloudOnly)
                        {
                            fileNode.Extension += " (Cloud-Only)";
                        }

                        batchNodes.Add(fileNode);
                        await AddNodeToParentAsync(node, fileNode);

                        // Incrementally update sizes
                        node.Size += fileNode.Size;
                        await UpdateSizesUpwardAsync(node.Parent);

                        scannedItems++;
                        await ReportProgressAsync();
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        scanErrors.AppendLine($"Access denied to file {file}: {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        scanErrors.AppendLine($"IO error with file {file}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        scanErrors.AppendLine($"Unexpected error with file {file}: {ex.Message}");
                    }
                }

                foreach (var dir in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        DirectoryInfo dirInfo = null;
                        await Task.Run(() =>
                        {
                            dirInfo = new DirectoryInfo(dir);
                        }, cancellationToken);

                        var dirNode = new FileSystemNode
                        {
                            FullPath = dir,
                            IsDirectory = true,
                            CreationTime = dirInfo.CreationTime,
                            LastWriteTime = dirInfo.LastWriteTime,
                            Extension = "",
                            Children = new ObservableCollection<FileSystemNode>()
                        };

                        batchNodes.Add(dirNode);
                        await AddNodeToParentAsync(node, dirNode);

                        tasks.Add(ScanDirectoryRecursiveAsync(dirNode, node, cancellationToken));
                        scannedItems++;
                        await ReportProgressAsync();
                        await Task.Delay(1); // Yield to keep UI responsive
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        scanErrors.AppendLine($"Access denied to directory {dir}: {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        scanErrors.AppendLine($"IO error with directory {dir}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        scanErrors.AppendLine($"Unexpected error with directory {dir}: {ex.Message}");
                    }
                }

                await Task.WhenAll(tasks);

                // Final size calculation (in case of any missed updates)
                node.Size = node.Children.Sum(c => c.Size);
                await UpdateSizesUpwardAsync(node.Parent);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) throw;
                scanErrors.AppendLine($"Error processing {node.FullPath}: {ex.Message}");
            }
        }

        private async Task UpdateSizesUpwardAsync(FileSystemNode node)
        {
            while (node != null)
            {
                long totalSize = node.Children.Sum(c => c.Size);
                node.Size = totalSize;
                node = node.Parent;
            }

            await Task.Delay(1); // Yield to keep UI responsive
        }

        private async Task AddNodeToParentAsync(FileSystemNode parent, FileSystemNode node)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                parent.Children.Add(node);
                allScannedNodes.Add(node);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task ReportProgressAsync()
        {
            if (totalItems <= 0) return;

            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalMilliseconds >= 100 || batchNodes.Count >= 500) // Changed to 100ms
            {
                int percent = (int)((double)scannedItems / totalItems * 100);
                var nodesToReport = new List<FileSystemNode>(batchNodes);
                batchNodes.Clear();
                lastUpdateTime = now;

                progress?.Report((Math.Min(percent, 100), nodesToReport));
                await Task.Delay(1);
            }
        }

        private async Task FlushBatchAsync()
        {
            if (batchNodes.Any())
            {
                int percent = (int)((double)scannedItems / totalItems * 100);
                var nodesToReport = new List<FileSystemNode>(batchNodes);
                batchNodes.Clear();
                progress?.Report((Math.Min(percent, 100), nodesToReport));
                await Task.Delay(1);
            }
        }
    }

    public class FileSystemNode
    {
        public string FullPath { get; set; }
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public ObservableCollection<FileSystemNode> Children { get; set; }
        public FileSystemNode Parent { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Extension { get; set; }
        public int DuplicateCount { get; set; }
        public double BarWidth { get; set; }
        public SolidColorBrush BarFill { get; set; }
        public string FormattedSize { get; set; }
    }

    public class FileTypeStats
    {
        public string Extension { get; set; }
        public long TotalSize { get; set; }
        public int FileCount { get; set; }
    }

    public class FileAgeStats
    {
        public string Category { get; set; }
        public long TotalSize { get; set; }
        public int FileCount { get; set; }

        public void AddFile(long size)
        {
            TotalSize += size;
            FileCount++;
        }
    }

    public class ScanHistoryEntry
    {
        public DateTime ScanDate { get; set; }
        public string Path { get; set; }
        public long TotalSize { get; set; }
    }

    public static class CsvExporter
    {
        public static void ExportToCsv(List<FileSystemNode> files, string filePath)
        {
            var csvLines = new List<string>
            {
                "Path,Size,Creation Time,Last Write Time,Extension"
            };

            foreach (var file in files)
            {
                string line = $"\"{file.FullPath}\",\"{file.Size}\",\"{file.CreationTime:yyyy-MM-dd HH:mm:ss}\",\"{file.LastWriteTime:yyyy-MM-dd HH:mm:ss}\",\"{file.Extension}\"";
                csvLines.Add(line);
            }

            File.WriteAllLines(filePath, csvLines, Encoding.UTF8);
        }
    }
}