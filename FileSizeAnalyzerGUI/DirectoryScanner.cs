using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FileSizeAnalyzerGUI
{
    public class DirectoryScanner
    {
        private readonly IProgress<(int percent, string status)> _progress;
        private readonly StringBuilder _scanErrors;
        private DateTime _lastUpdateTime;
        private int _itemsProcessed;

        public DirectoryScanner(IProgress<(int percent, string status)> progress, StringBuilder scanErrors)
        {
            _progress = progress;
            _scanErrors = scanErrors;
            _lastUpdateTime = DateTime.Now;
        }

        public async Task<FileSystemNode> ScanDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(path))
            {
                _scanErrors.AppendLine($"Directory not found: {path}");
                return null;
            }

            var rootNode = new FileSystemNode
            {
                FullPath = path,
                IsDirectory = true,
                CreationTime = Directory.GetCreationTime(path),
                LastWriteTime = Directory.GetLastWriteTime(path),
                Extension = "",
                Children = new ObservableCollection<FileSystemNode>(),
                Icon = IconManager.GetIcon(path, true)
            };

            var directoryQueue = new Queue<FileSystemNode>();
            directoryQueue.Enqueue(rootNode);

            // Get a list of all directories first to have a rough total for progress reporting
            int totalDirs = 0;
            try
            {
                _progress?.Report((-1, "Estimating directory count..."));
                // This can fail on drives with restricted folders, so we handle it.
                totalDirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Length + 1;
            }
            catch (Exception ex)
            {
                _scanErrors.AppendLine($"Could not fully estimate directory count: {ex.Message}. Progress will be indeterminate.");
                totalDirs = 0; // Set to 0 to indicate failure
            }
            int dirsProcessed = 0;


            await Task.Run(async () =>
            {
                while (directoryQueue.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentNode = directoryQueue.Dequeue();
                    dirsProcessed++;

                    try
                    {
                        var entries = Directory.EnumerateFileSystemEntries(currentNode.FullPath);
                        foreach (var entryPath in entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            _itemsProcessed++;

                            FileAttributes attributes;
                            try { attributes = File.GetAttributes(entryPath); }
                            catch (Exception ex) { _scanErrors.AppendLine($"Could not get attributes for {entryPath}: {ex.Message}"); continue; }

                            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                            long fileSize = 0;
                            FileSystemInfo info;

                            if (isDirectory)
                            {
                                info = new DirectoryInfo(entryPath);
                            }
                            else
                            {
                                var fileInfo = new FileInfo(entryPath);
                                info = fileInfo;
                                bool isCloudPlaceholder = attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Offline);
                                fileSize = isCloudPlaceholder ? NativeMethods.GetAllocatedSizeOnDisk(entryPath) : fileInfo.Length;
                            }

                            var childNode = new FileSystemNode
                            {
                                FullPath = info.FullName,
                                IsDirectory = isDirectory,
                                Parent = currentNode,
                                CreationTime = info.CreationTime,
                                LastWriteTime = info.LastWriteTime,
                                Extension = isDirectory ? "" : System.IO.Path.GetExtension(info.FullName).ToLower(),
                                Size = fileSize,
                                Children = new ObservableCollection<FileSystemNode>(),
                                Icon = IconManager.GetIcon(info.FullName, isDirectory)
                            };

                            await Application.Current.Dispatcher.InvokeAsync(() => currentNode.Children.Add(childNode));

                            if (isDirectory)
                            {
                                directoryQueue.Enqueue(childNode);
                            }
                        }

                        // --- UPDATED PROGRESS REPORTING LOGIC ---
                        if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds > 100)
                        {
                            if (totalDirs > 0)
                            {
                                // We have a total, so show percentage progress
                                int percent = (int)((double)dirsProcessed / totalDirs * 100);
                                _progress?.Report((Math.Min(99, percent), $"Scanning... {dirsProcessed}/{totalDirs} directories processed."));
                            }
                            else
                            {
                                // We don't have a total, so just show items processed and keep bar indeterminate
                                _progress?.Report((-1, $"Scanning... {dirsProcessed} directories processed."));
                            }
                            _lastUpdateTime = DateTime.Now;
                        }
                    }
                    catch (UnauthorizedAccessException) { _scanErrors.AppendLine($"Access denied: {currentNode.FullPath}"); }
                    catch (Exception ex) { _scanErrors.AppendLine($"Error scanning {currentNode.FullPath}: {ex.Message}"); }
                }
            }, cancellationToken);
            return rootNode;
        }

        public long UpdateAllSizesAndFormatting(FileSystemNode node, double maxBarWidth = 200.0)
        {
            if (!node.IsDirectory)
            {
                node.FormattedSize = FormatSize(node.Size);
                return node.Size;
            }
            long totalSize = node.Children.Sum(child => UpdateAllSizesAndFormatting(child, maxBarWidth));
            node.Size = totalSize;
            node.FormattedSize = FormatSize(node.Size);
            if (node.Size > 0)
            {
                foreach (var child in node.Children)
                {
                    child.BarWidth = (child.Size / (double)node.Size) * maxBarWidth;
                    child.BarFill = GetRandomColor();
                }
            }
            return totalSize;
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 0) return "0 B";
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

        private Color GetRandomColor()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());
            return Color.FromRgb((byte)random.Next(120, 230), (byte)random.Next(120, 230), (byte)random.Next(120, 230));
        }
    }

    public static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

        public static long GetAllocatedSizeOnDisk(string filePath)
        {
            uint fileSizeHigh;
            uint fileSizeLow = GetCompressedFileSizeW(filePath, out fileSizeHigh);
            if (fileSizeLow == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
            {
                try { return new FileInfo(filePath).Length; } catch { return 0; }
            }
            return ((long)fileSizeHigh << 32) + fileSizeLow;
        }
    }
}
