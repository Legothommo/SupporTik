using System;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;

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

		public void ExportData() => CompositionRoot.Current.Hotkeys.ExportData();
		public void ImportData() => CompositionRoot.Current.Hotkeys.ImportData();
	}
}
