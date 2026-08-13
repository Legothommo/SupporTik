using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Web.WebView2.Core;
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

		// URL, который открывается по клику на последний показанный баллун (обновление,
		// отсутствие WebView2 Runtime) — общий на оба случая, чтобы не плодить отдельные
		// поля/обработчики клика. Отдельного значения на поток не нужно: клик может прийти
		// только после того, как баллун реально показан, то есть после того, как поле уже
		// установлено.
		private static string _pendingBalloonUrl;

		private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

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
			LoggingService.CleanupOldLogs();

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
			notifyIcon.TrayBalloonTipClicked += NotifyIcon_TrayBalloonTipClicked;

			RegisterGlobalExceptionHandlers(notifyIcon);

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

			// Применяем тему (системную или последнюю выбранную вручную) и пастельный акцент
			// поверх неё — иначе кнопки Appearance="Primary" и подобные элементы взяли бы
			// системный синий
			ThemeService.ApplyStartupTheme();

			CheckWebView2Runtime(notifyIcon);

			CompositionRoot.Initialize(notifyIcon);
			CompositionRoot.Current.Hotkeys.RegisterDefaultHotkeys();

			MainWindow mainWindow = new MainWindow();
			if (!SupporTik.Properties.Settings.Default.StartMinimized)
			{
				mainWindow.Show();
			}

			// Не await — не должно задерживать запуск приложения; при неудаче (нет сети,
			// GitHub недоступен) UpdateCheckService сам тихо возвращает null
			_ = CheckForUpdatesAsync(notifyIcon);
		}

		private static async Task CheckForUpdatesAsync(TaskbarIcon notifyIcon)
		{
			UpdateInfo update = await new UpdateCheckService().CheckAsync();

			if (update != null)
			{
				ShowUpdateBalloon(notifyIcon, update);
			}
		}

		/// <summary>
		/// Общая точка показа баллуна "доступно обновление" — используется и при
		/// автопроверке на старте, и при ручной проверке из настроек (см.
		/// SettingsPageViewModel), чтобы клик по баллуну в обоих случаях открывал
		/// нужную ссылку одним и тем же механизмом.
		/// </summary>
		public static void ShowUpdateBalloon(TaskbarIcon notifyIcon, UpdateInfo update)
		{
			_pendingBalloonUrl = update.ReleaseUrl;
			notifyIcon.ShowBalloonTip("Доступно обновление", $"Вышла версия {update.Version} — нажмите, чтобы открыть страницу релиза.", BalloonIcon.Info);
		}

		/// <summary>
		/// "Меню рекламы" держится на WebView2 (авторизация и парсинг через встроенный
		/// браузер) — без установленного Evergreen-рантайма оно просто не заработает, с
		/// малопонятной ошибкой в момент открытия окна. Проверяем один раз при старте и,
		/// если рантайма нет, сразу предупреждаем со ссылкой на установку, а не оставляем
		/// пользователя гадать, почему окно не открылось.
		/// </summary>
		private static void CheckWebView2Runtime(TaskbarIcon notifyIcon)
		{
			try
			{
				string version = CoreWebView2Environment.GetAvailableBrowserVersionString();

				if (!string.IsNullOrEmpty(version))
				{
					return;
				}
			}
			catch (Exception)
			{
				// WebView2RuntimeNotFoundException и подобные — считаем, что рантайма нет
			}

			_pendingBalloonUrl = WebView2DownloadUrl;
			notifyIcon.ShowBalloonTip("WebView2 Runtime не найден", "«Меню рекламы» не сможет работать без него — нажмите, чтобы скачать.", BalloonIcon.Warning);
		}

		private static void NotifyIcon_TrayBalloonTipClicked(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrEmpty(_pendingBalloonUrl))
			{
				return;
			}

			Process.Start(new ProcessStartInfo(_pendingBalloonUrl) { UseShellExecute = true });
		}

		/// <summary>
		/// Ничего из этого не должно ронять приложение молча — раньше единственным
		/// способом узнать о фоновом падении (хук хоткеев, WebView2, таймеры) было
		/// оказаться рядом с открытой консолью в момент сбоя. Теперь любое
		/// необработанное исключение уходит в файл лога (см. LoggingService), а
		/// исключения потока UI дополнительно не роняют окно — просто логируются.
		/// </summary>
		private static void RegisterGlobalExceptionHandlers(TaskbarIcon notifyIcon)
		{
			AppDomain.CurrentDomain.UnhandledException += (s, args) =>
			{
				LoggingService.LogError("AppDomain.UnhandledException", args.ExceptionObject as Exception);
			};

			TaskScheduler.UnobservedTaskException += (s, args) =>
			{
				LoggingService.LogError("TaskScheduler.UnobservedTaskException", args.Exception);
				args.SetObserved();
			};

			Current.DispatcherUnhandledException += (s, args) =>
			{
				LoggingService.LogError("DispatcherUnhandledException", args.Exception);
				notifyIcon.ShowBalloonTip("Произошла ошибка", "SupporTik продолжает работать. Подробности сохранены в лог.", BalloonIcon.Warning);
				args.Handled = true;
			};
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
