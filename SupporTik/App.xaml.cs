using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Classes;
using SupporTik.Services;
using SupporTik.Views;
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
		public static List<BindGroupInfo> _groupInfos;

		public static IHotkeyService _hotkeyService;
		public static ITextPasteService _pasteService;
		public static StorageService _storageService;
		public static QuickTextWindow _quickMenu;
		public static MarketingWindow _marketingWindow;

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

			// Пастельный акцент поверх Fluent-темы, чтобы кнопки Appearance="Primary"
			// и подобные элементы взяли этот цвет, а не системный синий по умолчанию
			Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
				System.Windows.Media.Color.FromRgb(0x9A, 0xA3, 0xEB),
				Wpf.Ui.Appearance.ApplicationTheme.Dark,
				false);

			// Инициализируем сервисы
			_pasteService = new TextPasteService();
			_hotkeyService = new HotkeyService();
			_storageService = new StorageService();
			_groupInfos = _storageService.LoadData<BindGroupInfo>("groups.json");
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
				var firstBind = keys[0];
				string groupTitle = GetGroupName(firstBind.Key, firstBind.Modifiers);

				_quickMenu.SetEntries(groupTitle, BuildQuickMenuEntries(keys));
				_quickMenu.ShowAtCursor();
			}
		}

		private static void OnMarketingMenuHotkeyPressed()
		{
			if (_pasteService.IsPaused)
			{
				return;
			}

			// Переиспользуем уже открытое окно вместо того, чтобы плодить новые при
			// повторных нажатиях хоткея — IsLoaded становится false после закрытия окна
			if (_marketingWindow == null || !_marketingWindow.IsLoaded)
			{
				_marketingWindow = new MarketingWindow();
				_marketingWindow.Show();
			}
			else
			{
				_marketingWindow.Activate();
			}
		}

		/// <summary>
		/// Хоткей, который может совпасть с обычным биндом. В этом случае прямая
		/// регистрация невозможна (см. RegisterDefaultHotkeys) — вместо неё нажатие
		/// открывает QuickTextWindow, где это действие показывается пунктом меню.
		/// </summary>
		private class SpecialHotkey
		{
			public string Name;
			public Key Key;
			public ModifierKeys Modifiers;
			public string MenuLabel;
			public Action Action;
		}

		private static List<SpecialHotkey> GetSpecialHotkeys()
		{
			return new List<SpecialHotkey>
			{
				new SpecialHotkey
				{
					Name = "NDAReplace",
					Key = (Key)SupporTik.Properties.Settings.Default.SelectedKey,
					Modifiers = (ModifierKeys)SupporTik.Properties.Settings.Default.SelectedModifiers,
					MenuLabel = "NDA Замена",
					Action = () => _pasteService.ReplaceSelectionInExternalApp()
				},
				new SpecialHotkey
				{
					Name = "MarketingMenu",
					Key = (Key)SupporTik.Properties.Settings.Default.MarketingMenuKey,
					Modifiers = (ModifierKeys)SupporTik.Properties.Settings.Default.MarketingMenuModifiers,
					MenuLabel = "Меню рекламы",
					Action = () => OnMarketingMenuHotkeyPressed()
				}
			};
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

			// Если это сочетание совпадает со специальным хоткеем (NDA-замена, меню рекламы) —
			// прямая регистрация для него невозможна (см. RegisterDefaultHotkeys), поэтому
			// даём доступ к нему отсюда
			var firstBind = binds[0];

			foreach (var special in GetSpecialHotkeys())
			{
				if (special.Key != Key.None && special.Key == firstBind.Key && special.Modifiers == firstBind.Modifiers)
				{
					entries.Add(new QuickMenuEntry
					{
						Name = special.MenuLabel,
						Action = special.Action,
						IsSpecial = true
					});
				}
			}

			return entries;
		}

		/// Название группы биндов с общим сочетанием клавиш, если пользователь его задал.
		public static string GetGroupName(Key key, ModifierKeys modifiers)
		{
			return _groupInfos.FirstOrDefault(g => g.Key == key && g.Modifiers == modifiers)?.Name;
		}

		public static void SetGroupName(Key key, ModifierKeys modifiers, string name)
		{
			var existing = _groupInfos.FirstOrDefault(g => g.Key == key && g.Modifiers == modifiers);

			if (string.IsNullOrWhiteSpace(name))
			{
				if (existing != null)
				{
					_groupInfos.Remove(existing);
				}
			}
			else if (existing != null)
			{
				existing.Name = name.Trim();
			}
			else
			{
				_groupInfos.Add(new BindGroupInfo { Key = key, Modifiers = modifiers, Name = name.Trim() });
			}

			_storageService.SaveData(_groupInfos, "groups.json");
		}

		public static void RegisterDefaultHotkeys()
		{
			_bindKeys = _storageService.LoadData<BindKeys>()
									  .OrderBy(b => b.Modifiers)
									  .ThenBy(b => b.Key)
									  .ToList();

			_hotkeyService.UnregisterAll();

			var specials = GetSpecialHotkeys();
			var groups = _bindKeys.GroupBy(b => new { b.Key, b.Modifiers }).ToList();

			foreach (var group in groups)
			{
				var binds = group.ToList();
				var firstBind = binds[0];

				bool collidesWithSpecial = specials.Any(s =>
					s.Key != Key.None && s.Key == firstBind.Key && s.Modifiers == firstBind.Modifiers);

				if (binds.Count == 1 && !collidesWithSpecial)
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
					// Либо несколько шаблонов на одном сочетании, либо оно совпадает со
					// специальным хоткеем (или и то, и другое) — в обоих случаях нужен выбор
					_hotkeyService.RegisterHotkey(
						"OpenQuickMenu" + firstBind.Name,
						firstBind.Key,
						firstBind.Modifiers,
						() => OnQuickMenuHotkeyPressed(binds));
				}
			}

			// Специальные хоткеи регистрируем отдельно, только если их сочетание не занято
			// ни одним биндом — если занято, оно уже вошло в QuickTextWindow выше
			foreach (var special in specials)
			{
				if (special.Key == Key.None)
				{
					continue;
				}

				bool usedByBind = groups.Any(g => g.Key.Key == special.Key && g.Key.Modifiers == special.Modifiers);
				if (usedByBind)
				{
					continue;
				}

				_hotkeyService.RegisterHotkey(special.Name, special.Key, special.Modifiers, special.Action);
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

			_storageService?.SaveData(_bindKeys);

			// Чистим хоткеи при выходе из приложения
			_hotkeyService?.UnregisterAll();
			(_hotkeyService as IDisposable)?.Dispose();

			base.OnExit(e);
		}
	}
}