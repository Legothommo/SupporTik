using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Форвардит вызовы в CompositionRoot.Current / Properties.Settings.Default / реестр.
	/// </summary>
	public class AppSettingsServiceAdapter : IAppSettingsService
	{
		private const string AppName = "SupporTik";
		private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		public bool AutoStartEnabled
		{
			get
			{
				using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
				{
					return key?.GetValue(AppName) != null;
				}
			}
		}

		public bool MinimizeToTray => Properties.Settings.Default.MinimizeToTray;
		public bool StartMinimized => Properties.Settings.Default.StartMinimized;

		public Key NdaKey => (Key)Properties.Settings.Default.SelectedKey;
		public ModifierKeys NdaModifiers => (ModifierKeys)Properties.Settings.Default.SelectedModifiers;
		public Key MarketingKey => (Key)Properties.Settings.Default.MarketingMenuKey;
		public ModifierKeys MarketingModifiers => (ModifierKeys)Properties.Settings.Default.MarketingMenuModifiers;
		public bool MarketingMenuFromLeft => Properties.Settings.Default.MarketingMenuFromLeft;

		public void Save(Key ndaKey, ModifierKeys ndaModifiers, Key marketingKey, ModifierKeys marketingModifiers,
			bool autoStart, bool minimizeToTray, bool startMinimized, bool marketingMenuFromLeft)
		{
			Properties.Settings.Default.MinimizeToTray = minimizeToTray;
			Properties.Settings.Default.StartMinimized = startMinimized;
			Properties.Settings.Default.SelectedModifiers = (int)ndaModifiers;
			Properties.Settings.Default.SelectedKey = (int)ndaKey;
			Properties.Settings.Default.MarketingMenuModifiers = (int)marketingModifiers;
			Properties.Settings.Default.MarketingMenuKey = (int)marketingKey;
			Properties.Settings.Default.MarketingMenuFromLeft = marketingMenuFromLeft;

			Properties.Settings.Default.Save();

			// Перерегистрация всех горячих клавиш в системе
			CompositionRoot.Current.Hotkeys.RegisterDefaultHotkeys();

			SetAutoStart(autoStart);

			CompositionRoot.Current.NotifyIcon?.ShowBalloonTip(
				"Сохранение",
				"Настройки успешно сохранены!",
				BalloonIcon.None);
		}

		public void SetAutoStart(bool enable)
		{
			try
			{
				using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
				{
					if (key != null)
					{
						if (enable)
						{
							string executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
							key.SetValue(AppName, $"\"{executablePath}\"");
						}
						else if (key.GetValue(AppName) != null)
						{
							key.DeleteValue(AppName, false);
						}
					}
				}
			}
			catch (Exception ex)
			{
				CompositionRoot.Current.NotifyIcon?.ShowBalloonTip(
					"Ошибка",
					$"Ошибка настройки автозапуска: {ex.Message}",
					BalloonIcon.None);
			}
		}

		public void StartHotkeyCapture(Action<Key, ModifierKeys> onCaptured) => CompositionRoot.Current.HotkeyService.StartCapture(onCaptured);
		public void CancelHotkeyCapture() => CompositionRoot.Current.HotkeyService.CancelCapture();

		public void ExportData(bool includeBinds, bool includeSettings, bool includeMarketingTemplates) =>
			CompositionRoot.Current.Hotkeys.ExportData(includeBinds, includeSettings, includeMarketingTemplates, AutoStartEnabled);
		public void ImportData() => CompositionRoot.Current.Hotkeys.ImportData();

		public Task ClearAuthorizationAsync()
		{
			return CompositionRoot.Current.Hotkeys.ClearAuthorizationAsync();
		}

		/// <summary>
		/// Бинды (keybinds.json через StorageService) не трогает — это отдельные пользовательские
		/// данные, а не настройки приложения; сбрасывается только то, что лежит в
		/// Properties.Settings (хоткеи, автозапуск, тема, история UID меню рекламы и т.п.).
		/// </summary>
		public void ResetToDefaults()
		{
			Properties.Settings.Default.Reset();
			Properties.Settings.Default.Save();

			SetAutoStart(false);

			// После сброса снова следуем системной теме — это новое значение по умолчанию.
			// (Reset() выше не проходит через её API, поэтому живая подписка на
			// SystemEvents могла бы иначе остаться висеть), и синхронно перерисовывает
			// окна под актуальную тему Windows.
			new ThemeService().SetFollowSystem(true);

			CompositionRoot.Current.Hotkeys.RegisterDefaultHotkeys();

			CompositionRoot.Current.NotifyIcon?.ShowBalloonTip(
				"Настройки сброшены",
				"Все настройки возвращены к значениям по умолчанию.",
				BalloonIcon.None);
		}
	}
}
