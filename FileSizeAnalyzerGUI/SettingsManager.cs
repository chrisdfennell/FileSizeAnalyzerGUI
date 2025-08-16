using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FileSizeAnalyzerGUI
{
    public class AppSettings
    {
        public List<string> ExclusionPatterns { get; set; } = new List<string>();
    }

    public static class SettingsManager
    {
        private static readonly string _settingsFilePath;

        static SettingsManager()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "FileSizeAnalyzerPro");
            Directory.CreateDirectory(appFolderPath); // Ensure the directory exists
            _settingsFilePath = Path.Combine(appFolderPath, "settings.json");
        }

        public static AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings(); // Return default settings if file doesn't exist
            }

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception)
            {
                // In case of corruption or error, return default settings
                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception)
            {
                // Handle exceptions (e.g., log the error)
            }
        }
    }
}
