using Microsoft.Web.WebView2.Core;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace SupporTik.ViewModels
{
	public class DiagnosticsPageViewModel : ViewModelBase
	{
		private readonly CompositionRoot _root;
		private readonly INotificationService _notifications;

		private string _diagnosticInfo;
		public string DiagnosticInfo
		{
			get => _diagnosticInfo;
			private set => SetProperty(ref _diagnosticInfo, value);
		}

		public string DataDirectory => _root.Storage.DataDirectory;
		public string LogDirectory => LoggingService.LogDirectory;

		public ICommand RefreshCommand { get; }
		public ICommand CopyCommand { get; }
		public ICommand OpenDataDirectoryCommand { get; }
		public ICommand OpenLogDirectoryCommand { get; }

		public DiagnosticsPageViewModel(
			CompositionRoot root,
			INotificationService notifications)
		{
			_root = root;
			_notifications = notifications;
			RefreshCommand = new RelayCommand(Refresh);
			CopyCommand = new RelayCommand(Copy);
			OpenDataDirectoryCommand = new RelayCommand(() => OpenDirectory(DataDirectory));
			OpenLogDirectoryCommand = new RelayCommand(() => OpenDirectory(LogDirectory));
			Refresh();
		}

		private void Refresh()
		{
			Version version = Assembly.GetExecutingAssembly().GetName().Version;
			string webViewVersion;
			try
			{
				webViewVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
			}
			catch (Exception)
			{
				webViewVersion = "не найден";
			}

			int jsonFiles = CountFiles(DataDirectory, "*.json");
			long jsonBytes = SumFileSizes(DataDirectory, "*.json");
			int logFiles = CountFiles(LogDirectory, "log-*.txt");

			DiagnosticInfo =
				$"SupporTik: {version}\r\n" +
				$"Windows: {Environment.OSVersion}\r\n" +
				$"Процесс: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}\r\n" +
				$"WebView2 Runtime: {webViewVersion}\r\n" +
				$"Биндов: {_root.Hotkeys.BindKeys.Count}\r\n" +
				$"Текстовых шаблонов предложений: {_root.MarketingTemplates.GetAll().Count}\r\n" +
				$"Файлов данных: {jsonFiles} ({FormatBytes(jsonBytes)})\r\n" +
				$"Файлов журнала: {logFiles}\r\n" +
				$"Кэш: предложения — 3 мин / до 1000; кампании — 5 мин / до 500\r\n" +
				$"Данные: {DataDirectory}\r\n" +
				$"Журнал: {LogDirectory}";
		}

		private void Copy()
		{
			try
			{
				Clipboard.SetText(DiagnosticInfo ?? string.Empty);
				_notifications.ShowBalloon("Диагностика", "Информация скопирована.", isWarning: false);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("DiagnosticsPageViewModel.Copy", ex);
				_notifications.ShowBalloon("Диагностика", "Не удалось скопировать информацию.", isWarning: true);
			}
		}

		private void OpenDirectory(string path)
		{
			try
			{
				Directory.CreateDirectory(path);
				Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				LoggingService.LogError("DiagnosticsPageViewModel.OpenDirectory", ex);
				_notifications.ShowBalloon("Диагностика", "Не удалось открыть папку.", isWarning: true);
			}
		}

		private static int CountFiles(string path, string pattern)
		{
			try { return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern).Count() : 0; }
			catch (Exception) { return 0; }
		}

		private static long SumFileSizes(string path, string pattern)
		{
			try
			{
				return Directory.Exists(path)
					? Directory.EnumerateFiles(path, pattern).Sum(file => new FileInfo(file).Length)
					: 0;
			}
			catch (Exception) { return 0; }
		}

		private static string FormatBytes(long bytes)
		{
			return bytes < 1024 ? $"{bytes} Б" : $"{bytes / 1024d:0.0} КБ";
		}
	}
}
