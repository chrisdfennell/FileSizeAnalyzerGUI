using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using FileSizeAnalyzerGUI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FileSizeAnalyzerGUI
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.ApplyTheme();
            base.OnStartup(e);

            _serviceProvider = ServiceConfiguration.ConfigureServices();

            if (e.Args.Length > 0)
            {
                // Check if first argument is a CLI command
                var firstArg = e.Args[0].ToLower();
                if (IsCLICommand(firstArg))
                {
                    // Run in CLI mode (console mode)
                    RunCLIModeAsync(e.Args).GetAwaiter().GetResult();
                    Shutdown();
                    return;
                }

                // Legacy GUI mode with CLI arguments
                if (e.Args[0].StartsWith("-"))
                {
                    var cliArgs = ParseCliArguments(e.Args);
                    if (cliArgs.ContainsKey("-path"))
                    {
                        MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                        mainWindow.InitializeWithCliArgs(cliArgs);
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
                    MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                    mainWindow.InitializeWithPath(folderPath);
                    mainWindow.Show();
                }
            }
            else
            {
                MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
        }

        private bool IsCLICommand(string arg)
        {
            return arg == "scan" || arg == "duplicates" || arg == "export" ||
                   arg == "recommend" || arg == "help" || arg == "version" ||
                   arg == "--help" || arg == "-h" || arg == "/?" ||
                   arg == "--version" || arg == "-v";
        }

        private async Task RunCLIModeAsync(string[] args)
        {
            try
            {
                var cliHandler = _serviceProvider!.GetRequiredService<CLIHandler>();
                var exitCode = await cliHandler.ExecuteAsync(args);
                Environment.ExitCode = exitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Environment.ExitCode = 1;
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