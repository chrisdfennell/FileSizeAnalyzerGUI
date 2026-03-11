using Microsoft.Extensions.DependencyInjection;
using FileSizeAnalyzerGUI.Services;
using FileSizeAnalyzerGUI.Services.Interfaces;
using FileSizeAnalyzerGUI.ViewModels;

namespace FileSizeAnalyzerGUI
{
	public static class ServiceConfiguration
	{
		public static IServiceProvider ConfigureServices()
		{
			var services = new ServiceCollection();

			// Infrastructure
			services.AddSingleton<ILogger, FileLogger>();
			services.AddSingleton<HashCache>();

			// Services
			services.AddSingleton<TreemapService>();
			services.AddSingleton<SunburstService>();
			services.AddSingleton<ScanMetadataService>();
			services.AddSingleton<TemporaryFilesService>();
			services.AddSingleton<AdvancedAnalysisService>();
			services.AddSingleton<ExportService>();
			services.AddSingleton<SearchService>();
			services.AddSingleton<TrendsService>();
			services.AddSingleton<FilePreviewService>();
			services.AddSingleton<CleanupRecommendationService>();
			services.AddSingleton<FileOperationsService>();
			services.AddSingleton<DashboardService>();
			services.AddSingleton<DuplicateManagementService>();
			services.AddSingleton<CLIHandler>();

			services.AddTransient<IFileSystemScannerService, FileSystemScannerService>();
			services.AddTransient<IDuplicateDetectionService, DuplicateDetectionService>();
			services.AddTransient<IFilterService, FilterService>();
			services.AddTransient<IAnalysisService, AnalysisService>();

			// ViewModel
			services.AddTransient<MainViewModel>();

			// View
			services.AddTransient<MainWindow>();

			return services.BuildServiceProvider();
		}
	}
}
