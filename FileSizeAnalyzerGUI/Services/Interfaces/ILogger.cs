namespace FileSizeAnalyzerGUI.Services.Interfaces
{
	public enum LogLevel
	{
		Debug,
		Info,
		Warning,
		Error,
		Critical
	}

	public interface ILogger
	{
		void Log(LogLevel level, string message, Exception? exception = null);
		void Debug(string message);
		void Info(string message);
		void Warning(string message, Exception? exception = null);
		void Error(string message, Exception? exception = null);
		void Critical(string message, Exception exception);
		IEnumerable<string> GetRecentLogs(int count = 100);
		void Clear();
	}
}
