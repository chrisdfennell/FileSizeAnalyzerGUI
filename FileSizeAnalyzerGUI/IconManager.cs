using System;
using System.Collections.Generic;
using System.Drawing; // <-- This line is added to fix the error
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileSizeAnalyzerGUI
{
    public static class IconManager
    {
        private static readonly Dictionary<string, ImageSource> _iconCache = new Dictionary<string, ImageSource>();
        private static readonly object _lock = new object();

        public static ImageSource GetIcon(string path, bool isDirectory)
        {
            string key = isDirectory ? "##DIRECTORY##" : System.IO.Path.GetExtension(path).ToLower();
            if (string.IsNullOrEmpty(key)) key = "##FILE##"; // Key for files with no extension

            lock (_lock)
            {
                if (_iconCache.TryGetValue(key, out var icon))
                {
                    return icon;
                }
            }

            var newIcon = GetIconFromShell(path, isDirectory);
            if (newIcon != null)
            {
                lock (_lock)
                {
                    if (!_iconCache.ContainsKey(key))
                    {
                        _iconCache.Add(key, newIcon);
                    }
                }
            }
            return newIcon;
        }

        private static ImageSource GetIconFromShell(string path, bool isDirectory)
        {
            var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            var shfi = new SHFILEINFO();
            var res = SHGetFileInfo(path, attributes, out shfi, (uint)Marshal.SizeOf(shfi), flags);

            if (res == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // This line now works because we added 'using System.Drawing;'
                using (var icon = Icon.FromHandle(shfi.hIcon))
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }

        #region P/Invoke Signatures
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint SHGFI_SMALLICON = 0x1;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        #endregion
    }
}
 