using Microsoft.Win32;
using System;
using System.Windows;

namespace FileSizeAnalyzerGUI
{
    public static class ThemeManager
    {
        public enum AppTheme
        {
            Light,
            Dark
        }

        public static void ApplyTheme()
        {
            var appTheme = GetWindowsTheme();
            var themeDictionary = new ResourceDictionary();

            if (appTheme == AppTheme.Dark)
            {
                themeDictionary.Source = new Uri("DarkTheme.xaml", UriKind.Relative);
            }
            else
            {
                themeDictionary.Source = new Uri("LightTheme.xaml", UriKind.Relative);
            }

            Application.Current.Resources.MergedDictionaries.Add(themeDictionary);
        }

        private static AppTheme GetWindowsTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null && key.GetValue("AppsUseLightTheme") is int value)
                {
                    return value > 0 ? AppTheme.Light : AppTheme.Dark;
                }
            }
            catch
            {
                // Default to dark theme if registry can't be read
                return AppTheme.Dark;
            }
            // Default to dark theme
            return AppTheme.Dark;
        }
    }
}
