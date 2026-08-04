using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Services;

namespace SupporTik
{
	/// <summary>
	/// Единственное место, которое реально создаёт долгоживущие сервисы приложения —
	/// заменяет статический service-locator (App._xxx). Инициализируется один раз в
	/// App.OnStartup; Views/адаптеры обращаются к нему через CompositionRoot.Current
	/// в своих (обязательных для XAML-дизайнера) parameterless-конструкторах.
	/// </summary>
	public class CompositionRoot
	{
		public static CompositionRoot Current { get; private set; }

		public TaskbarIcon NotifyIcon { get; }
		public ITextPasteService PasteService { get; }
		public IHotkeyService HotkeyService { get; }
		public StorageService Storage { get; }
		public QuickTextWindow QuickMenu { get; }
		public HotkeyRegistrationService Hotkeys { get; }

		private CompositionRoot(TaskbarIcon notifyIcon)
		{
			NotifyIcon = notifyIcon;

			PasteService = new TextPasteService();
			HotkeyService = new HotkeyService(PasteService);
			Storage = new StorageService();
			QuickMenu = new QuickTextWindow();

			Hotkeys = new HotkeyRegistrationService(Storage, HotkeyService, PasteService, QuickMenu, NotifyIcon);
		}

		public static CompositionRoot Initialize(TaskbarIcon notifyIcon)
		{
			return Current = new CompositionRoot(notifyIcon);
		}

		public void Shutdown()
		{
			Hotkeys.SaveOnExit();
			Hotkeys.UnregisterAllOnExit();
		}
	}
}
