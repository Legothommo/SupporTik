using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SupporTik
{
	/// <summary>
	/// Логика взаимодействия для App.xaml
	/// </summary>
	public partial class App : Application
	{
		private const string MutexName = "Global\\SupporTik_SingleInstance_Mutex_Guid";
		private static Mutex _mutex;
		private static bool _hasHandle = false;

		protected override async void OnStartup(StartupEventArgs e)
		{
			var notifyIcon = (TaskbarIcon)Application.Current.FindResource("MyNotifyIcon");

			try
			{
				// Запрашиваем владение мьютексом
				_mutex = new Mutex(true, MutexName, out bool isNewInstance);
				_hasHandle = isNewInstance;

				if (!_hasHandle)
				{
					notifyIcon.ShowBalloonTip(
						"Предупреждение",
						"Приложение уже запущено!",
						BalloonIcon.Warning);

					await Task.Delay(1000);

					Shutdown();
					return; // Важно: прерываем выполнение метода
				}
			}
			catch (Exception)
			{
				// На случай проблем с правами доступа к системному мьютексу
				_hasHandle = false;
			}

			base.OnStartup(e);

			// Применяем сохранённую тему (светлая/тёмная) и пастельный акцент поверх неё —
			// иначе кнопки Appearance="Primary" и подобные элементы взяли бы системный синий
			ThemeService.Apply(SupporTik.Properties.Settings.Default.IsLightTheme);

			CompositionRoot.Initialize(notifyIcon);
			CompositionRoot.Current.Hotkeys.RegisterDefaultHotkeys();

			MainWindow mainWindow = new MainWindow();
			if (!SupporTik.Properties.Settings.Default.StartMinimized)
			{
				mainWindow.Show();
			}
		}

		protected override void OnExit(ExitEventArgs e)
		{
			if (_hasHandle && _mutex != null)
			{
				try
				{
					_mutex.ReleaseMutex();
				}
				catch (ApplicationException)
				{
					// Игнорируем, если поток уже завершился или потерял контекст владения
				}
				finally
				{
					_mutex.Close();
					_mutex = null;
				}
			}

			CompositionRoot.Current?.Shutdown();

			base.OnExit(e);
		}
	}
}
