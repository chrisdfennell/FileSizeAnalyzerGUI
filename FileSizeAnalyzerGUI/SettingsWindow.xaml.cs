using System;
using System.Linq;
using System.Windows;

namespace FileSizeAnalyzerGUI
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = SettingsManager.LoadSettings();
            ExclusionsTextBox.Text = string.Join(Environment.NewLine, settings.ExclusionPatterns);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new AppSettings
            {
                ExclusionPatterns = ExclusionsTextBox.Text
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToList()
            };
            SettingsManager.SaveSettings(settings);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
