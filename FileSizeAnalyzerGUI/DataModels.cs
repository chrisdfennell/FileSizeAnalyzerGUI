using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileSizeAnalyzerGUI
{
    public class FileSystemNode : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public ObservableCollection<FileSystemNode> Children { get; set; } = new ObservableCollection<FileSystemNode>();
        public FileSystemNode Parent { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Extension => IsDirectory ? "" : Path.GetExtension(FullPath)?.ToLower();
        public Color BarFill { get; set; }
        public string FormattedSize { get; set; }
        public ImageSource Icon { get; set; }
        public bool IsCloudOnly { get; set; }

        private double _barWidth;
        public double BarWidth
        {
            get => _barWidth;
            set { _barWidth = value; OnPropertyChanged(); }
        }

        // ####################################################################
        // ## NEW FEATURE: Added IsSelected property for data binding with
        // ## checkboxes in the UI to enable bulk operations.
        // ####################################################################
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DuplicateSet
    {
        public string FileName { get; set; }
        public int Count { get; set; }
        public string FormattedSize { get; set; }
        public ImageSource Icon { get; set; }
        public ObservableCollection<FileSystemNode> Files { get; set; } = new ObservableCollection<FileSystemNode>();
    }

    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class FormatSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
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
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PathToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                return Path.GetFileName(path);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
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
        public void AddFile(long size) { TotalSize += size; FileCount++; }
    }

    public class ScanHistoryEntry
    {
        public DateTime ScanDate { get; set; }
        public string Path { get; set; }
        public long TotalSize { get; set; }
    }
}