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
		public MarketingTemplateService MarketingTemplates { get; }
		public QuickTextWindow QuickMenu { get; }
		public HotkeyRegistrationService Hotkeys { get; }

		private CompositionRoot(TaskbarIcon notifyIcon)
		{
			NotifyIcon = notifyIcon;

			PasteService = new TextPasteService();
			HotkeyService = new HotkeyService(PasteService);
			Storage = new StorageService();
			MarketingTemplates = new MarketingTemplateService(Storage);
			QuickMenu = new QuickTextWindow();

			Hotkeys = new HotkeyRegistrationService(Storage, HotkeyService, PasteService, QuickMenu, NotifyIcon);
		}

		public static CompositionRoot Initialize(TaskbarIcon notifyIcon)
		{
			return Current = new CompositionRoot(notifyIcon);
		}

		public void Shutdown()
		{
			try
			{
				Hotkeys.SaveOnExit();
			}
			catch
			{
				// StorageService уже записал подробности ошибки в лог; завершение работы
				// всё равно должно снять системный хук и освободить его ресурсы.
			}
			finally
			{
				Hotkeys.UnregisterAllOnExit();
			}
		}
	}
}
