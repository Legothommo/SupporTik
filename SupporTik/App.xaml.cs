using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using SupporTik.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
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

		// Без явного AppUserModelID Hardcodet.Wpf.TaskbarNotification на каждый запуск сама
		// генерирует Windows-у случайный "NotifyIconGeneratedAumid_..." и регистрирует под
		// ним иконку уведомлений во временный PNG — при частых перезапусках (особенно в
		// разработке) в реестре копится куча таких записей, и уведомления могут годами
		// показывать иконку, замороженную в одной из старых (даже если временный файл давно
		// удалён). Явный, стабильный AUMID — единственный настоящий фикс: Windows начинает
		// переиспользовать одну и ту же запись вместо создания новой на каждый запуск.
		[DllImport("shell32.dll", SetLastError = true)]
		private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

		private const string AppUserModelId = "SupporTik.DesktopApp";

		/// <summary>
		/// SetCurrentProcessExplicitAppUserModelID сам по себе ничего не регистрирует —
		/// без DisplayName/IconUri под этим AUMID в реестре Windows нечего показать (отсюда
		/// сырая строка AUMID вместо названия при первой попытке). IconUri у AppUserModelId
		/// (в отличие от классических DefaultIcon) не понимает синтаксис "путь,индекс" —
		/// нужен путь к самому файлу картинки. Поэтому извлекаем иконку из exe и кладём её
		/// не во временную папку (как делал Hardcodet — оттуда её могло стереть системной
		/// очисткой), а в %LOCALAPPDATA%\SupporTik — переживёт что угодно, кроме удаления
		/// самого приложения. Пишем при каждом запуске (дёшево) — самовосстановится, если
		/// приложение переедет в другую папку или иконку обновят.
		/// </summary>
		private static void RegisterAppUserModelId()
		{
			try
			{
				string exePath = Process.GetCurrentProcess().MainModule.FileName;

				string iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupporTik");
				Directory.CreateDirectory(iconDir);
				string iconPath = Path.Combine(iconDir, "notification_icon.ico");

				using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
				using (var stream = new FileStream(iconPath, FileMode.Create, FileAccess.Write))
				{
					icon.Save(stream);
				}

				using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
				{
					key.SetValue("DisplayName", "SupporTik", RegistryValueKind.String);
					key.SetValue("IconUri", iconPath, RegistryValueKind.String);
				}
			}
			catch (Exception)
			{
				// Не критично — просто уведомления останутся без красивой иконки/названия
			}
		}

		protected override async void OnStartup(StartupEventArgs e)
		{
			// Обязательно ДО первого обращения к TaskbarIcon (FindResource ниже её и создаёт) —
			// иначе Hardcodet уже успеет сгенерировать свой случайный AUMID
			RegisterAppUserModelId();
			SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

			// .NET Framework по умолчанию держит не больше 2 одновременных подключений
			// к одному хосту (ServicePointManager.DefaultConnectionLimit) — без этого
			// параллельные запросы страниц кампаний/апсейлов (см. MarketingCampaignService,
			// UpsaleService) реально шли бы по 2 за раз, вставая в очередь на HttpClient
			ServicePointManager.DefaultConnectionLimit = 20;

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
