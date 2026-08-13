using System;
using System.IO;

namespace SupporTik.Services
{
	/// <summary>
	/// Простое файловое логирование ошибок — единственный способ узнать, что упало
	/// в фоне (хук хоткеев, WebView2, таймеры, необработанные исключения), если
	/// рядом с пользователем никого не было с открытой консолью. Один файл в день,
	/// старые чистятся сами (см. CleanupOldLogs).
	/// </summary>
	public static class LoggingService
	{
		private const int RetainDays = 14;

		private static readonly string _logDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"SupporTik", "logs");

		private static readonly object _writeLock = new object();

		public static void LogError(string context, Exception ex)
		{
			try
			{
				lock (_writeLock)
				{
					Directory.CreateDirectory(_logDirectory);

					string filePath = Path.Combine(_logDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
					string entry =
						$"[{DateTime.Now:HH:mm:ss}] {context}{Environment.NewLine}" +
						$"{ex}{Environment.NewLine}" +
						$"{new string('-', 60)}{Environment.NewLine}";

					File.AppendAllText(filePath, entry);
				}
			}
			catch (Exception)
			{
				// Логирование не должно само по себе валить приложение
			}
		}

		/// <summary>Вызывается один раз при старте — удаляет файлы логов старше RetainDays.</summary>
		public static void CleanupOldLogs()
		{
			try
			{
				if (!Directory.Exists(_logDirectory))
				{
					return;
				}

				DateTime cutoff = DateTime.Now.AddDays(-RetainDays);

				foreach (string file in Directory.GetFiles(_logDirectory, "log-*.txt"))
				{
					if (File.GetLastWriteTime(file) < cutoff)
					{
						File.Delete(file);
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
