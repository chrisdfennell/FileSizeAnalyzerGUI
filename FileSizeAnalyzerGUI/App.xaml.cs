using System.Windows;

namespace FileSizeAnalyzerGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.ApplyTheme();
            base.OnStartup(e);
        }
    }
}