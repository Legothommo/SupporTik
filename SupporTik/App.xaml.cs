using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Classes;
using SupporTik.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SupporTik
{
	/// <summary>
	/// Логика взаимодействия для App.xaml
	/// </summary>
	public partial class App : Application
	{
		public static TaskbarIcon _notifyIcon;
		public static List<BindKeys> _bindKeys;

		public static IHotkeyService _hotkeyService;
		public static ITextPasteService _pasteService;
		public static StorageService _storageService;
		public static QuickTextWindow _quickMenu;

		private const string MutexName = "Global\\SupporTik_SingleInstance_Mutex_Guid";
		private static Mutex _mutex;
		private static bool _hasHandle = false;

		protected override async void OnStartup(StartupEventArgs e)
		{
			_notifyIcon = (TaskbarIcon)Application.Current.FindResource("MyNotifyIcon");

			try
			{
				// Запрашиваем владение мьютексом
				_mutex = new Mutex(true, MutexName, out bool isNewInstance);
				_hasHandle = isNewInstance;

				if (!_hasHandle)
				{
					_notifyIcon.ShowBalloonTip(
						"Предупреждение",
						"Приложение уже запущено!",
						BalloonIcon.Warning);

					await Task.Delay(1000);

					Shutdown();
					return; // Важно: прерываем выполнение метода
				}
			}
			catch (Exception ex)
			{
				// На случай проблем с правами доступа к системному мьютексу
				_hasHandle = false;
			}

			base.OnStartup(e);

			// Инициализируем сервисы
			_pasteService = new TextPasteService();
			_hotkeyService = new HotkeyService();
			_storageService = new StorageService();
			_quickMenu = new QuickTextWindow();

			RegisterDefaultHotkeys();

			MainWindow mainWindow = new MainWindow();
			if (!SupporTik.Properties.Settings.Default.StartMinimized)
			{
				mainWindow.Show();
			}
		}

		private static void OnQuickMenuHotkeyPressed(List<BindKeys> keys)
		{
			// Вызываем показ окна возле мыши
			if (!_pasteService.IsPaused)
			{
				_quickMenu.SetEntries(BuildQuickMenuEntries(keys));
				_quickMenu.ShowAtCursor();
			}
		}

		/// <summary>
		/// Собирает пункты всплывающего меню для группы биндов с общим сочетанием клавиш.
		/// QuickTextWindow сам ничего не знает про BindKeys/настройки — вся эта логика здесь.
		/// </summary>
		private static List<QuickMenuEntry> BuildQuickMenuEntries(List<BindKeys> binds)
		{
			var entries = binds
				.Select(bind => new QuickMenuEntry
				{
					Name = bind.Name,
					Action = () => _pasteService.PasteText(bind.Text)
				})
				.ToList();

			// Если это сочетание совпадает с хоткеем NDA-замены — прямой хоткей для него
			// не сработает (см. RegisterDefaultHotkeys), поэтому даём доступ к нему отсюда
			var firstBind = binds[0];
			bool matchesNdaHotkey =
				firstBind.Key == (Key)SupporTik.Properties.Settings.Default.SelectedKey &&
				firstBind.Modifiers == (ModifierKeys)SupporTik.Properties.Settings.Default.SelectedModifiers;

			if (matchesNdaHotkey)
			{
				entries.Add(new QuickMenuEntry
				{
					Name = "NDA Замена",
					Action = () => _pasteService.ReplaceSelectionInExternalApp(),
					IsSpecial = true
				});
			}

			return entries;
		}

		public static void RegisterDefaultHotkeys()
		{
			_bindKeys = _storageService.LoadData<BindKeys>()
									  .OrderBy(b => b.Modifiers)
									  .ThenBy(b => b.Key)
									  .ToList();

			_hotkeyService.UnregisterAll();

			var groups = _bindKeys.GroupBy(b => new { b.Key, b.Modifiers });

			foreach (var group in groups)
			{
				var binds = group.ToList();

				if (binds.Count == 1)
				{
					var bind = binds[0];
					_hotkeyService.RegisterHotkey(
						bind.Name,
						bind.Key,
						bind.Modifiers,
						() => _pasteService.PasteText(bind.Text));
				}
				else
				{
					var firstBind = binds[0];
					_hotkeyService.RegisterHotkey(
						"OpenQuickMenu" + firstBind.Name,
						firstBind.Key,
						firstBind.Modifiers,
						() => OnQuickMenuHotkeyPressed(binds));
				}
			}

			// Регистрация горячей клавиши по умолчанию из настроек
			_hotkeyService.RegisterHotkey(
				"NDAReplace",
				(Key)SupporTik.Properties.Settings.Default.SelectedKey,
				(ModifierKeys)SupporTik.Properties.Settings.Default.SelectedModifiers,
				() => _pasteService.ReplaceSelectionInExternalApp());
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

			_storageService?.SaveData(_bindKeys);

			// Чистим хоткеи при выходе из приложения
			_hotkeyService?.UnregisterAll();
			(_hotkeyService as IDisposable)?.Dispose();

			base.OnExit(e);
		}
	}
}