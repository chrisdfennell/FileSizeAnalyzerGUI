using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileSizeAnalyzerGUI.Services.Interfaces;

namespace FileSizeAnalyzerGUI.Services
{
	public class FileLogger : ILogger
	{
		private readonly string _logFilePath;
		private readonly ConcurrentQueue<string> _recentLogs;
		private readonly object _fileLock = new object();
		private const int MaxRecentLogs = 1000;

		public FileLogger()
		{
			var appDataPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"FileSizeAnalyzer"
			);
			Directory.CreateDirectory(appDataPath);
			_logFilePath = Path.Combine(appDataPath, $"log_{DateTime.Now:yyyyMMdd}.txt");
			_recentLogs = new ConcurrentQueue<string>();
		}

		public void Log(LogLevel level, string message, Exception? exception = null)
		{
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			var logEntry = $"[{timestamp}] [{level}] {message}";
			
			if (exception != null)
			{
				logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
			}

			_recentLogs.Enqueue(logEntry);
			while (_recentLogs.Count > MaxRecentLogs)
			{
				_recentLogs.TryDequeue(out _);
			}

			try
			{
				lock (_fileLock)
				{
					File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
				}
			}
			catch
			{
			}
		}

		public void Debug(string message) => Log(LogLevel.Debug, message);
		public void Info(string message) => Log(LogLevel.Info, message);
		public void Warning(string message, Exception? exception = null) => Log(LogLevel.Warning, message, exception);
		public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
		public void Critical(string message, Exception exception) => Log(LogLevel.Critical, message, exception);

		public IEnumerable<string> GetRecentLogs(int count = 100)
		{
			return _recentLogs.TakeLast(count);
		}

		public void Clear()
		{
			_recentLogs.Clear();
		}
	}
}
