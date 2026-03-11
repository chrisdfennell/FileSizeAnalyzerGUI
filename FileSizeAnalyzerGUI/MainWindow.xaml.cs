using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileSizeAnalyzerGUI.Services;
using FileSizeAnalyzerGUI.Services.Interfaces;
using FileSizeAnalyzerGUI.ViewModels;

namespace FileSizeAnalyzerGUI
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        private Dictionary<string, string>? _cliArgs;
        private readonly TreemapService _treemapService;
        private readonly SunburstService _sunburstService;

        public MainWindow(
            MainViewModel viewModel,
            TreemapService treemapService,
            SunburstService sunburstService)
        {
            ViewModel = viewModel;
            _treemapService = treemapService;
            _sunburstService = sunburstService;

            InitializeComponent();
            DataContext = ViewModel;

            PopulateDrives();
            DirectoryTreeView.SelectedItemChanged += DirectoryTreeView_SelectedItemChanged;
            WireDuplicateToolbar();

            ViewModel.TreemapRedrawRequested += DrawTreemap;
            ViewModel.SunburstRedrawRequested += DrawSunburst;
        }

        public void InitializeWithPath(string initialPath)
        {
            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            {
                DirectoryPathTextBox.Text = initialPath;
                ViewModel.ScanPath = initialPath;
                Loaded += async (s, e) => await ViewModel.RunScanAsync();
            }
        }

        public void InitializeWithCliArgs(Dictionary<string, string> cliArgs)
        {
            _cliArgs = cliArgs;

            if (_cliArgs.ContainsKey("-no-skip-system"))
                ViewModel.SkipSystemFiles = false;
            if (_cliArgs.ContainsKey("-no-skip-windows"))
                ViewModel.SkipWindowsDirectory = false;

            Loaded += MainWindow_Loaded_Cli;
        }

        private void WireDuplicateToolbar()
        {
            if (FindName("VerifyDuplicatesCheckBox") is CheckBox verifyBox)
            {
                ViewModel.VerifyByteByByte = verifyBox.IsChecked == true;
                verifyBox.Checked += (_, __) => ViewModel.VerifyByteByByte = true;
                verifyBox.Unchecked += (_, __) => ViewModel.VerifyByteByByte = false;
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
                ViewModel.MinDupSizeBytes = val;
            }
        }

        private async void MainWindow_Loaded_Cli(object sender, RoutedEventArgs e)
        {
            if (_cliArgs == null) return;

            ViewModel.ScanPath = _cliArgs["-path"];
            DirectoryPathTextBox.Text = _cliArgs["-path"];
            await ViewModel.RunScanAsync();

            if (_cliArgs.TryGetValue("-export", out var exportPath))
            {
                if (string.IsNullOrWhiteSpace(exportPath))
                    exportPath = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
                await Task.Run(() => CsvExporter.ExportToCsv(ViewModel.AllFiles.ToList(), exportPath));
            }

            if (_cliArgs.ContainsKey("-exit"))
                Application.Current.Shutdown();
        }

        #region Window Control and Setup

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;

        private void PopulateDrives()
        {
            DriveSelectionComboBox.ItemsSource = ViewModel.AvailableDrives;
        }

        private void DriveSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveSelectionComboBox.SelectedItem is string drive)
            {
                DirectoryPathTextBox.Text = drive;
                ViewModel.ScanPath = drive;
            }
        }

        #endregion

        #region Scanning Logic

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ScanPath = DirectoryPathTextBox.Text;
            ScanButton.Visibility = Visibility.Collapsed;
            StopScanButton.Visibility = Visibility.Visible;
            ScanProgressBar.Visibility = Visibility.Visible;
            ProgressTextBlock.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Minimum = 0;
            ScanProgressBar.Maximum = 100;

            TreemapCanvas.Children.Clear();
            var sunburstCanvas = FindName("SunburstCanvas") as Canvas;
            sunburstCanvas?.Children.Clear();

            await ViewModel.RunScanAsync();

            // Reset UI after scan
            ProgressTextBlock.Text = "Analysis Complete.";
            await Task.Delay(2000);
            ProgressTextBlock.Visibility = Visibility.Collapsed;
            ScanProgressBar.Visibility = Visibility.Collapsed;
            StopScanButton.Visibility = Visibility.Collapsed;
            ScanButton.Visibility = Visibility.Visible;

            // Redraw visualizations
            ApplyFilters_Click(null!, null!);
            DrawTreemap();
        }

        private void StopScanButton_Click(object sender, RoutedEventArgs e) => ViewModel.CancelScan();

        #endregion

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
                ViewModel.ScanPath = dialog.FolderName;
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.AllFiles == null || !ViewModel.AllFiles.Any())
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
                await Task.Run(() => CsvExporter.ExportToCsv(ViewModel.AllFiles.ToList(), sfd.FileName));
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
                        Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
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
                await DeleteFileAsync(node);
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

            var warningMessage = $"About to delete {totalFiles:N0} files totaling {MainViewModel.FormatSize(totalSize)}";
            if (hasUnsafeCategories)
                warningMessage += "\n\nWARNING: Some selected categories are marked as potentially unsafe to delete!";
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
            var itemsToDelete = ViewModel.GetSelectedItemsForDeletion();

            if (!itemsToDelete.Any())
            {
                MessageBox.Show("No items selected for deletion.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string message = $"Are you sure you want to move {itemsToDelete.Count} selected item(s) to the Recycle Bin?";
            if (MessageBox.Show(message, "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await DeleteMultipleFilesAsync(itemsToDelete.ToList());
        }

        private async void MoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.AllFiles.Where(f => f.IsSelected && !f.IsDirectory).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No files selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFolderDialog { Title = "Select destination folder" };
            if (dialog.ShowDialog() == true)
                await MoveFilesAsync(selectedItems, dialog.FolderName);
        }

        private async void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.AllFiles.Where(f => f.IsSelected && !f.IsDirectory).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No files selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFolderDialog { Title = "Select destination folder" };
            if (dialog.ShowDialog() == true)
                await CopyFilesAsync(selectedItems, dialog.FolderName);
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

            MessageBox.Show($"{movedCount} file(s) moved successfully." +
                          (errors.Length > 0 ? $"\n\nErrors:\n{errors}" : ""),
                          "Move Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            ViewModel.ScanPath = DirectoryPathTextBox.Text;
            await ViewModel.RunScanAsync();
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
                        deleteErrors.AppendLine($"Failed to delete {item.FullPath}: {ex.Message}");
                    }
                }
            });

            string summaryMessage = $"{deletedCount} item(s) moved to Recycle Bin.";
            if (deleteErrors.Length > 0)
                summaryMessage += "\n\nSome errors occurred:\n" + deleteErrors.ToString();
            MessageBox.Show(summaryMessage, "Deletion Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            ViewModel.ScanPath = DirectoryPathTextBox.Text;
            await ViewModel.RunScanAsync();
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
                    ViewModel.ScanPath = DirectoryPathTextBox.Text;
                    await ViewModel.RunScanAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error moving item to Recycle Bin: {ex.Message}");
                }
            }
        }

        #endregion

        #region Selection & Preview handlers

        private async void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf);
        }

        private async void DuplicatesTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DuplicatesTreeView.SelectedItem is FileSystemNode sf) await DeleteFileAsync(sf);
        }

        private void ScanHistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ScanHistoryDataGrid.SelectedItem is ScanHistoryEntry se)
            {
                DirectoryPathTextBox.Text = se.Path;
                ViewModel.ScanPath = se.Path;
                ScanButton_Click(null!, null!);
            }
        }

        private async void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is FileSystemNode sf)
                await PreviewFileInUIAsync(sf);
        }

        private async Task PreviewFileInUIAsync(FileSystemNode file)
        {
            if (file == null) return;

            var previewImage = FindName("PreviewImage") as System.Windows.Controls.Image;
            if (previewImage != null) previewImage.Visibility = Visibility.Collapsed;

            await ViewModel.PreviewFileAsync(file);

            // Handle image preview (WPF-specific, must stay in code-behind)
            if (previewImage != null && !file.IsDirectory)
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
                        bitmap.DecodePixelWidth = 400;
                        bitmap.EndInit();
                        previewImage.Source = bitmap;
                        previewImage.Visibility = Visibility.Visible;
                    }
                    catch { /* If image loading fails, just show text preview */ }
                }
            }
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

                ApplyFilters_Click(null!, null!);
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
            FileSystemNode? currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? ViewModel.RootNodes.FirstOrDefault();
            if (currentNode == null) return;
            if (!currentNode.IsDirectory) currentNode = currentNode.Parent;
            if (currentNode == null) return;

            UpdateBreadcrumb(currentNode);

            var nodesToDraw = currentNode.Children.Where(n => n.Size > 0).ToList();
            if (!nodesToDraw.Any()) return;

            var bounds = new Rect(0, 0, TreemapCanvas.ActualWidth, TreemapCanvas.ActualHeight);
            var rectangles = _treemapService.GenerateTreemap(nodesToDraw, bounds);

            foreach (var rect in rectangles)
                RenderTreemapRectangle(rect);
        }

        private void UpdateBreadcrumb(FileSystemNode currentNode)
        {
            var breadcrumbs = new List<BreadcrumbItem>();
            var pathStack = new Stack<FileSystemNode>();

            for (var node = currentNode; node != null; node = node.Parent)
                pathStack.Push(node);

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
                        Text = MainViewModel.FormatSize(rect.Node.Size),
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
                Text = $"Size: {MainViewModel.FormatSize(rect.Node.Size)} ({rect.Percentage:F2}%)"
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

            border.MouseLeftButtonDown += (s, e) => TreemapRectangle_Click(rect.Node);

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
            FileSystemNode? currentNode = (DirectoryTreeView.SelectedItem as FileSystemNode) ?? ViewModel.RootNodes.FirstOrDefault();
            if (currentNode == null) return;
            if (!currentNode.IsDirectory) currentNode = currentNode.Parent;
            if (currentNode == null) return;

            UpdateBreadcrumb(currentNode);

            double centerX = sunburstCanvas.ActualWidth / 2;
            double centerY = sunburstCanvas.ActualHeight / 2;
            double maxRadius = Math.Min(centerX, centerY) - 10;

            if (maxRadius < 50) return;

            var segments = _sunburstService.GenerateSunburst(currentNode, centerX, centerY, maxRadius);

            foreach (var segment in segments)
            {
                var path = _sunburstService.CreateSegmentPath(segment, centerX, centerY);

                var tooltip = new StackPanel();
                tooltip.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(segment.Node.FullPath) ?? segment.Node.FullPath,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                tooltip.Children.Add(new TextBlock
                {
                    Text = $"Size: {MainViewModel.FormatSize(segment.Node.Size)}"
                });
                tooltip.Children.Add(new TextBlock
                {
                    Text = $"Type: {(segment.Node.IsDirectory ? "Folder" : segment.Node.Extension ?? "File")}"
                });
                path.ToolTip = tooltip;

                path.MouseEnter += (s, ev) =>
                {
                    path.StrokeThickness = 2;
                    path.Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                };

                path.MouseLeave += (s, ev) =>
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
                pathStack.Push(current);

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

        #region Auto-select rules + keyboard shortcuts

        private void SelectDuplicates_KeepNewest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
                ViewModel.ApplyAutoSelectForGroup(group, g => g.Files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault());
        }

        private void SelectDuplicates_KeepOldest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
                ViewModel.ApplyAutoSelectForGroup(group, g => g.Files.OrderBy(f => f.LastWriteTime).FirstOrDefault());
        }

        private void SelectDuplicates_Clear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateSet group)
                ViewModel.ClearGroupSelection(group);
        }

        private void AutoSelect_KeepNewest_AllGroups_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepNewestCommand.Execute(null);
        private void AutoSelect_KeepOldest_AllGroups_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepOldestCommand.Execute(null);
        private void AutoSelect_KeepOnePerFolder_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepOnePerFolderCommand.Execute(null);
        private void AutoSelect_KeepShortestPath_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepShortestPathCommand.Execute(null);
        private void AutoSelect_KeepFastestDrive_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepFastestDriveCommand.Execute(null);
        private void AutoSelect_KeepHighestQuality_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepHighestQualityCommand.Execute(null);
        private void AutoSelect_KeepLargest_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepLargestCommand.Execute(null);
        private void AutoSelect_KeepSmallest_Click(object sender, RoutedEventArgs e) => ViewModel.AutoSelectKeepSmallestCommand.Execute(null);

        private async void DeleteSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = ViewModel.GetSelectedDuplicateFiles();
            if (!selectedFiles.Any())
            {
                MessageBox.Show("No files selected for deletion.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalSize = selectedFiles.Sum(f => f.Size);
            var result = MessageBox.Show(
                $"Delete {selectedFiles.Count:N0} selected duplicate files ({MainViewModel.FormatSize(totalSize)})?\n\n" +
                "Files will be sent to the Recycle Bin and can be restored if needed.",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var progressWindow = CreateProgressWindow("Deleting Files", "Deleting files...");
            var progress = CreateProgressReporter(progressWindow);
            progressWindow.Show();

            var deleteResult = await ViewModel.DeleteFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(), useRecycleBin: true, progress);

            progressWindow.Close();

            if (deleteResult.Success)
            {
                MessageBox.Show(
                    $"Successfully deleted {deleteResult.SuccessCount:N0} files ({MainViewModel.FormatSize(deleteResult.TotalSize)}).",
                    "Deletion Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                ViewModel.RemoveDeletedFilesFromDuplicates(selectedFiles);
            }
            else
            {
                var errorMsg = $"Deleted {deleteResult.SuccessCount:N0} files, {deleteResult.FailedCount:N0} failed.\n\n";
                if (deleteResult.Errors.Any())
                    errorMsg += "First few errors:\n" + string.Join("\n", deleteResult.Errors.Take(5));
                MessageBox.Show(errorMsg, "Deletion Completed with Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void MoveSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = ViewModel.GetSelectedDuplicateFiles();
            if (!selectedFiles.Any())
            {
                MessageBox.Show("No files selected to move.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var inputDialog = new Window
            {
                Title = "Select Destination Folder",
                Width = 500, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(15) };
            sp.Children.Add(new TextBlock { Text = "Enter destination folder path:", Margin = new Thickness(0, 0, 0, 10) });
            var pathTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 15) };
            sp.Children.Add(pathTextBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75 };

            bool dialogResult = false;
            okButton.Click += (s, args) => { dialogResult = true; inputDialog.Close(); };
            cancelButton.Click += (s, args) => { dialogResult = false; inputDialog.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            sp.Children.Add(buttonPanel);
            inputDialog.Content = sp;
            inputDialog.ShowDialog();

            if (!dialogResult || string.IsNullOrWhiteSpace(pathTextBox.Text)) return;

            var destinationFolder = pathTextBox.Text.Trim();
            var totalSize = selectedFiles.Sum(f => f.Size);

            var confirmResult = MessageBox.Show(
                $"Move {selectedFiles.Count:N0} files ({MainViewModel.FormatSize(totalSize)}) to:\n{destinationFolder}?",
                "Confirm Move", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            var progressWindow = CreateProgressWindow("Moving Files", "Moving files...");
            var progress = CreateProgressReporter(progressWindow);
            progressWindow.Show();

            var moveResult = await ViewModel.MoveFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(), destinationFolder, progress);

            progressWindow.Close();

            MessageBox.Show(
                $"Moved {moveResult.SuccessCount:N0} files successfully.\n" +
                (moveResult.FailedCount > 0 ? $"Failed: {moveResult.FailedCount:N0}\n" : "") +
                $"Time: {moveResult.Duration.TotalSeconds:F1}s",
                "Move Complete", MessageBoxButton.OK,
                moveResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async void CompressSelectedDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = ViewModel.GetSelectedDuplicateFiles();
            if (!selectedFiles.Any())
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

            if (saveDialog.ShowDialog() != true) return;

            var zipPath = saveDialog.FileName;
            var totalSize = selectedFiles.Sum(f => f.Size);

            var confirmResult = MessageBox.Show(
                $"Compress {selectedFiles.Count:N0} files ({MainViewModel.FormatSize(totalSize)}) to:\n{zipPath}?",
                "Confirm Compression", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            var progressWindow = CreateProgressWindow("Compressing Files", "Compressing files...");
            var progress = CreateProgressReporter(progressWindow);
            progressWindow.Show();

            var compressResult = await ViewModel.CompressFilesAsync(
                selectedFiles.Select(f => f.FullPath).ToList(), zipPath, progress);

            progressWindow.Close();

            if (compressResult.Success)
            {
                var zipFileInfo = new FileInfo(zipPath);
                var compressionRatio = (1.0 - (double)zipFileInfo.Length / totalSize) * 100;
                MessageBox.Show(
                    $"Successfully compressed {compressResult.SuccessCount:N0} files.\n\n" +
                    $"Original Size: {MainViewModel.FormatSize(totalSize)}\n" +
                    $"Compressed Size: {MainViewModel.FormatSize(zipFileInfo.Length)}\n" +
                    $"Compression Ratio: {compressionRatio:F1}%\n" +
                    $"Time: {compressResult.Duration.TotalSeconds:F1}s",
                    "Compression Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Compression completed with errors.\nSuccessful: {compressResult.SuccessCount:N0}\nFailed: {compressResult.FailedCount:N0}",
                    "Compression Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { DeleteSelected_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.E) { OpenInExplorerSelected(); e.Handled = true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F) { ApplyFilters_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.F5) { ScanButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.Escape) { StopScanButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.D1 || e.Key == Key.NumPad1) { ViewModel.AutoSelectKeepNewestCommand.Execute(null); e.Handled = true; return; }
                if (e.Key == Key.D2 || e.Key == Key.NumPad2) { ViewModel.AutoSelectKeepOldestCommand.Execute(null); e.Handled = true; return; }
                if (e.Key == Key.D3 || e.Key == Key.NumPad3) { ViewModel.AutoSelectKeepOnePerFolderCommand.Execute(null); e.Handled = true; return; }
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
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
        }

        #endregion

        #region HTML Report

        private async void ExportHtmlReport_Click(object sender, RoutedEventArgs e)
        {
            var root = ViewModel.RootNodes.FirstOrDefault();
            if (root == null) { MessageBox.Show("Please run a scan before exporting a report."); return; }

            var sfd = new SaveFileDialog
            {
                Filter = "HTML file (*.html)|*.html",
                FileName = $"FileSizeAnalyzer-Report-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    await Task.Run(() => HtmlReportExporter.Export(
                        path: sfd.FileName,
                        rootNode: root,
                        duplicateGroups: ViewModel.Duplicates?.ToList() ?? new List<DuplicateSet>(),
                        lastRule: ViewModel.LastAutoSelectRuleDescription));
                    MessageBox.Show($"Report exported:\n{sfd.FileName}", "Export HTML", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export HTML report: {ex.Message}", "Export HTML", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExportCsvReport_Click(object sender, RoutedEventArgs e) => await ExportReport("CSV");
        private async void ExportJsonReport_Click(object sender, RoutedEventArgs e) => await ExportReport("JSON");
        private async void ExportEnhancedHtmlReport_Click(object sender, RoutedEventArgs e) => await ExportReport("HTML");

        private async Task ExportReport(string format)
        {
            var root = ViewModel.RootNodes.FirstOrDefault();
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
                        IncludeFileList = true, IncludeDuplicates = true, IncludeFileTypes = true,
                        IncludeLargestFiles = true, IncludeTemporaryFiles = true, IncludeStaleFiles = true,
                        MaxFilesToExport = 10000
                    };

                    var exportData = ViewModel.PrepareExportData(exportOptions);

                    await Task.Run(() =>
                    {
                        string content = format switch
                        {
                            "CSV" => ViewModel.ExportToCsv(exportData, exportOptions),
                            "JSON" => ViewModel.ExportToJson(exportData),
                            "HTML" => ViewModel.ExportToHtml(exportData, exportOptions),
                            _ => throw new NotSupportedException($"Format {format} not supported")
                        };
                        File.WriteAllText(sfd.FileName, content);
                    });

                    MessageBox.Show($"{format} report exported successfully:\n{sfd.FileName}",
                        $"Export {format}", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export {format} report: {ex.Message}",
                        $"Export {format}", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Quick Filters

        private void QuickFilter_AllFiles_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("All");
        private void QuickFilter_ModifiedToday_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("ModifiedToday");
        private void QuickFilter_ModifiedThisWeek_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("ModifiedThisWeek");
        private void QuickFilter_ModifiedThisMonth_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("ModifiedThisMonth");
        private void QuickFilter_LargerThan100MB_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("LargerThan100MB");
        private void QuickFilter_LargerThan1GB_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("LargerThan1GB");
        private void QuickFilter_Videos_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("Videos");
        private void QuickFilter_Images_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("Images");
        private void QuickFilter_Documents_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("Documents");
        private void QuickFilter_Archives_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyQuickFilter("Archives");

        #endregion

        #region Advanced Search

        private void ExecuteSearch_Click(object sender, RoutedEventArgs e)
        {
            var criteria = BuildSearchCriteria();
            ViewModel.ExecuteSearch(criteria);
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

            if (!string.IsNullOrWhiteSpace(minSizeTextBox?.Text) && long.TryParse(minSizeTextBox.Text, out long minSize))
                criteria.MinSize = minSize * 1024 * 1024;
            if (!string.IsNullOrWhiteSpace(maxSizeTextBox?.Text) && long.TryParse(maxSizeTextBox.Text, out long maxSize))
                criteria.MaxSize = maxSize * 1024 * 1024;

            if (fileTypeComboBox?.SelectedIndex > 0)
            {
                var tag = (fileTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                if (tag == "FilesOnly") criteria.IsDirectory = false;
                else if (tag == "FoldersOnly") criteria.IsDirectory = true;
            }

            return criteria;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
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

            ViewModel.ClearSearch();
        }

        #endregion

        #region Trends & Analytics

        private void RefreshTrends_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshTrends();

            var analysis = ViewModel.GetTrendAnalysis();
            var breakdown = ViewModel.GetSpaceBreakdown();
            if (analysis == null) return;

            if (FindName("TrendsSummaryTextBlock") is TextBlock trendsSummary) trendsSummary.Text = analysis.Summary;
            if (FindName("TrendsRecommendationTextBlock") is TextBlock trendsRec) trendsRec.Text = analysis.Recommendation;

            if (FindName("PredictionsList") is ItemsControl predictions)
            {
                predictions.ItemsSource = ViewModel.GetGrowthPredictions(analysis);
            }

            if (breakdown != null)
            {
                if (FindName("ExtensionBreakdownList") is ItemsControl extList)
                    extList.ItemsSource = breakdown.ByExtension.OrderByDescending(kv => kv.Value).Take(10)
                        .Select(kv => $"{kv.Key}: {MainViewModel.FormatSize(kv.Value)}").ToList();

                if (FindName("AgeBreakdownList") is ItemsControl ageList)
                    ageList.ItemsSource = breakdown.ByAgeCategory
                        .Select(kv => $"{kv.Key}: {MainViewModel.FormatSize(kv.Value)}").ToList();

                if (FindName("CategoryBreakdownList") is ItemsControl catList)
                    catList.ItemsSource = breakdown.ByCategory.OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{kv.Key}: {MainViewModel.FormatSize(kv.Value)}").ToList();
            }
        }

        private void GenerateRecommendations_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.GenerateRecommendations();
            RecommendationsList.ItemsSource = ViewModel.CleanupRecommendations;
        }

        private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshDashboard();
            var dashboard = ViewModel.DashboardData;
            if (dashboard == null) return;

            try
            {
                var maxTypeSize = dashboard.FileTypeData.Any() ? dashboard.FileTypeData.Max(ft => ft.Size) : 1;
                FileTypeChartList.ItemsSource = dashboard.FileTypeData.Select(ft => new
                {
                    ft.Extension, ft.FormattedSize,
                    BarWidth = (ft.Size / (double)maxTypeSize) * 250
                }).ToList();
                LargestFoldersChartList.ItemsSource = dashboard.LargestFolders;

                TotalScannedText.Text = dashboard.FormattedTotalSize;
                DiskUsedText.Text = dashboard.FormattedUsedSpace;
                DiskFreeText.Text = dashboard.FormattedFreeSpace;
                UsagePercentageText.Text = $"{dashboard.UsagePercentage:F1}%";

                var circumference = 2 * Math.PI * 42.5;
                var dashLength = (dashboard.UsagePercentage / 100.0) * circumference;
                UsageGaugeEllipse.StrokeDashArray = new DoubleCollection { dashLength, circumference - dashLength };

                var gaugeColor = dashboard.UsagePercentage switch
                {
                    >= 90 => "#F44336", >= 75 => "#FF9800", >= 50 => "#FFC107", _ => "#4CAF50"
                };
                UsageGaugeEllipse.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(gaugeColor));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing dashboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Filtering

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.AllFiles == null) return;

            var criteria = new FilterCriteria
            {
                SizeFilter = (SizeFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                Extensions = ExtensionFilterTextBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().Replace("*", "")).ToArray(),
                DateFilter = (DateFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                StartDate = StartDatePicker?.SelectedDate,
                EndDate = EndDatePicker?.SelectedDate
            };

            ViewModel.ApplyFilters(criteria);
        }

        #endregion

        #region Helper Methods

        private Window CreateProgressWindow(string title, string message)
        {
            return new Window
            {
                Title = title, Width = 400, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = message, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) },
                        new ProgressBar { Name = "ProgressBar", Height = 25, IsIndeterminate = false, Maximum = 100 }
                    }
                }
            };
        }

        private Progress<int> CreateProgressReporter(Window progressWindow)
        {
            return new Progress<int>(percent =>
            {
                var progressBar = ((StackPanel)progressWindow.Content).Children.OfType<ProgressBar>().First();
                progressBar.Value = percent;
            });
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
                .ToList() ?? new List<(string name, long size)>();

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
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<header><h1>FileSizeAnalyzer Report</h1><div class='small'>Generated " + Safe(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "</div></header>");
            sb.AppendLine("<div class='container grid'>");
            sb.AppendLine("<div class='section card'><h2 style='margin:0 0 8px 0'>Treemap (top-level)</h2><div id='treemap'></div></div>");
            sb.AppendLine("<div class='section card'><h2 style='margin:0 0 8px 0'>Duplicate Groups</h2>");
            sb.AppendLine("<div class='small'>Sorted by potential savings. Last auto-select rule: <b>" + Safe(lastRule) + "</b></div>");
            sb.AppendLine("<table><thead><tr><th>Name</th><th>Count</th><th>Size (each)</th><th>Potential Savings</th><th>Files</th></tr></thead><tbody>");

            foreach (var r in dupRows)
            {
                sb.Append("<tr><td>").Append(r.name).Append("</td><td>").Append(r.count).Append("</td><td>")
                  .Append(FormatBytes(r.sizeEach)).Append("</td><td>").Append(FormatBytes(r.saving))
                  .Append("</td><td><details><summary>Show paths</summary><ul style='margin:6px 0 0 18px'>");
                foreach (var p in r.files) sb.Append("<li>").Append(p).Append("</li>");
                sb.AppendLine("</ul></details></td></tr>");
            }

            sb.AppendLine("</tbody></table></div>");
            sb.AppendLine("<div class='section card small'><b>Keyboard Shortcuts</b>: <kbd>Del</kbd> delete selected, <kbd>Ctrl+E</kbd> open in Explorer, <kbd>Ctrl+F</kbd> apply filters, <kbd>F5</kbd> scan, <kbd>Esc</kbd> stop, <kbd>Ctrl+1/2/3</kbd> select rules.</div>");
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
  const el=document.getElementById('treemap');const W=el.clientWidth,H=el.clientHeight;
  const total=treemapData.reduce((a,b)=>a+b.size,0)||1;
  let x=0,y=0;const horiz=W>=H;treemapData.sort((a,b)=>b.size-a.size);
  for(let i=0;i<treemapData.length;i++){const item=treemapData[i];const frac=item.size/total;
    if(horiz){const ww=Math.max(1,Math.round(W*frac));addTile(x,0,ww,H,item.name,item.size,i);x+=ww;}
    else{const hh=Math.max(1,Math.round(H*frac));addTile(0,y,W,hh,item.name,item.size,i);y+=hh;}}
  function addTile(x,y,w,h,name,size,i){const d=document.createElement('div');d.className='tile';
    d.style.left=x+'px';d.style.top=y+'px';d.style.width=w+'px';d.style.height=h+'px';d.style.background=color(i);
    const s=document.createElement('span');s.textContent=(w>80&&h>30)?(name.split(/[\\/]/).slice(-1)[0]+' \u2022 '+formatBytes(size)):'';
    d.title=name+'\n'+formatBytes(size);d.appendChild(s);el.appendChild(d);}}
window.addEventListener('load',drawTreemap);
window.addEventListener('resize',()=>{document.getElementById('treemap').innerHTML='';drawTreemap();});
");
            sb.AppendLine("</script></body></html>");
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
            return $"{bytes / Math.Pow(1024, i):0.##} {u[i]}";
        }
    }

    public class BreadcrumbItem
    {
        public string Name { get; set; } = string.Empty;
        public FileSystemNode? Node { get; set; }
        public bool ShowSeparator { get; set; }
    }
}
