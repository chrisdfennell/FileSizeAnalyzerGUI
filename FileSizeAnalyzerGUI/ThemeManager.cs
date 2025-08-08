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

        // ####################################################################
        // ## THEME FIX: The logic for detecting the Windows theme has been
        // ## corrected to properly default to Light mode and handle registry
        // ## values correctly.
        // ####################################################################
        private static AppTheme GetWindowsTheme()
        {
            try
            {
                // Use Registry.GetValue for a more direct and safer approach.
                // It allows specifying a default value if the key or value doesn't exist.
                // We'll default to '1' (Light theme) which is a safe fallback.
                var registryValueObject = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);

                if (registryValueObject is int registryValue)
                {
                    // A value of 0 means the user has selected the dark theme for apps.
                    // Any other value (typically 1) means light theme.
                    return registryValue == 0 ? AppTheme.Dark : AppTheme.Light;
                }
            }
            catch (Exception)
            {
                // If reading the registry fails for any reason, default to the Light theme.
                return AppTheme.Light;
            }

            // As a final fallback, default to Light theme.
            return AppTheme.Light;
        }
    }
}
