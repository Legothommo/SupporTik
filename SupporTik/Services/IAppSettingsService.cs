using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Точка входа для SettingsPageViewModel — настройки поведения приложения,
	/// автозапуск через реестр, хоткеи NDA/меню рекламы, импорт/экспорт.
	/// </summary>
	public interface IAppSettingsService
	{
		bool AutoStartEnabled { get; }
		bool MinimizeToTray { get; }
		bool StartMinimized { get; }

		Key NdaKey { get; }
		ModifierKeys NdaModifiers { get; }
		Key MarketingKey { get; }
		ModifierKeys MarketingModifiers { get; }

		/// <summary>true — окно меню рекламы выезжает слева, false (по умолчанию) — справа.</summary>
		bool MarketingMenuFromLeft { get; }

		void Save(Key ndaKey, ModifierKeys ndaModifiers, Key marketingKey, ModifierKeys marketingModifiers,
			bool autoStart, bool minimizeToTray, bool startMinimized, bool marketingMenuFromLeft);

		void SetAutoStart(bool enable);

		void StartHotkeyCapture(Action<Key, ModifierKeys> onCaptured);
		void CancelHotkeyCapture();

		void ExportData();
		void ImportData();

		Task ClearAuthorizationAsync();

		/// <summary>Возвращает все настройки (включая хоткеи, тему, историю UID) к значениям по умолчанию. Бинды (keybinds.json) не трогает.</summary>
		void ResetToDefaults();
	}
}
