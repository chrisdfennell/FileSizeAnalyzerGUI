namespace FileSizeAnalyzerGUI
{
	public static class Constants
	{
		public static class FileSize
		{
			public const long Kilobyte = 1024;
			public const long Megabyte = 1024 * Kilobyte;
			public const long Gigabyte = 1024 * Megabyte;
			public const long Terabyte = 1024 * Gigabyte;
		}

		public static class DuplicateDetection
		{
			public const long DefaultMinDupSizeBytes = 256 * FileSize.Kilobyte;
			public const long DefaultForceVerifyAboveBytes = 32 * FileSize.Megabyte;
			public const bool DefaultVerifyByteByByte = false;
			public const int ComparisonBufferSize = 256 * (int)FileSize.Kilobyte;
			public const int PartialHashWindowSize = 64 * (int)FileSize.Kilobyte;
			public const int PartialHashThreshold = 256 * (int)FileSize.Kilobyte;
		}

		public static class FileIO
		{
			public const int StreamBufferSize = 128 * (int)FileSize.Kilobyte;
		}

		public static class UI
		{
			public const int StatusUpdateThrottleMilliseconds = 100;
			public const int MaxPreviewFileSizeBytes = 1 * (int)FileSize.Megabyte;
			public const int MaxPreviewLines = 1000;
		}

		public static class Scanning
		{
			public const string WindowsDirectoryName = "Windows";
			public const string SystemVolumeInformationName = "System Volume Information";
			public const string RecycleBinName = "$RECYCLE.BIN";
		}

		public static class HashCache
		{
			public const string CacheFileName = "hashCache.db";
			public const int MaxCacheEntries = 100000;
		}

		public static class Export
		{
			public const string CsvFileFilter = "CSV files (*.csv)|*.csv";
			public const string DefaultCsvFileName = "file_analysis.csv";
		}

		public static class Settings
		{
			public const string SettingsFileName = "settings.json";
		}
	}
}
