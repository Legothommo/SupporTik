using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Classes;
using SupporTik.Services;
using System.Collections.Generic;
using System.Linq;
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

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			// Инициализируем сервисы
			_pasteService = new TextPasteService();
			_hotkeyService = new HotkeyService();
			_storageService = new StorageService();
			_quickMenu = new QuickTextWindow();

			_notifyIcon = (TaskbarIcon)Application.Current.FindResource("MyNotifyIcon");

			RegisterDefaultHotkeys();
		}

		private static void OnQuickMenuHotkeyPressed(List<BindKeys> keys)
		{
			// Вызываем показ окна возле мыши
			if (!_pasteService.IsPaused)
			{
				_quickMenu.SetBinds(keys);
				_quickMenu.ShowAtCursor();
			}
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
			_storageService?.SaveData(_bindKeys);

			// Чистим хоткеи при выходе из приложения
			_hotkeyService?.UnregisterAll();

			base.OnExit(e);
		}
	}
}