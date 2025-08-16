using System;
using System.Collections.Generic;
using System.Windows;

namespace FileSizeAnalyzerGUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.ApplyTheme();
            base.OnStartup(e);

            if (e.Args.Length > 0)
            {
                if (e.Args[0].StartsWith("-"))
                {
                    var cliArgs = ParseCliArguments(e.Args);
                    if (cliArgs.ContainsKey("-path"))
                    {
                        MainWindow mainWindow = new MainWindow(cliArgs);
                        mainWindow.Show();
                    }
                    else
                    {
                        MessageBox.Show("Command-line usage requires at least the -path argument.\n\nOptional arguments:\n-export \"report.csv\"\n-exit\n-no-skip-system\n-no-skip-windows\n\nExample:\nFileSizeAnalyzerGUI.exe -path \"C:\\Windows\" -no-skip-system -no-skip-windows", "Invalid Command-Line Arguments");
                        Application.Current.Shutdown();
                    }
                }
                else
                {
                    string folderPath = e.Args[0];
                    MainWindow mainWindow = new MainWindow(folderPath);
                    mainWindow.Show();
                }
            }
            else
            {
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        private Dictionary<string, string> ParseCliArguments(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    var key = args[i];
                    var value = (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        ? args[i + 1]
                        : string.Empty;

                    dict[key] = value;
                }
            }
            return dict;
        }
    }
}