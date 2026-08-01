using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using SupporTik.Classes;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для SettingsPage.xaml
	/// </summary>
	public partial class SettingsPage : Page
	{
		private const string AppName = "SupporTik";
		private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		private Key _selectedKey = Key.None;
		private ModifierKeys _selectedModifiers = ModifierKeys.None;

		public SettingsPage()
		{
			InitializeComponent();
			LoadSettings();
			LoadHotkeyDisplay();
		}

		private void LoadSettings()
		{
			ChkAutoStart.IsChecked = IsInAutoStart();
			ChkMinimizeToTray.IsChecked = Properties.Settings.Default.MinimizeToTray;
			ChkStartMinimized.IsChecked = Properties.Settings.Default.StartMinimized;
		}

		private void LoadHotkeyDisplay()
		{
			if (Properties.Settings.Default.SelectedKey != 0 && (ModifierKeys)Properties.Settings.Default.SelectedModifiers != 0)
			{
				_selectedKey = (Key)Properties.Settings.Default.SelectedKey;
				_selectedModifiers = (ModifierKeys)Properties.Settings.Default.SelectedModifiers;
				TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
			}
			else
			{
				_selectedKey = Key.None;
				_selectedModifiers = ModifierKeys.None;
				TbHotkeyDisplay.Text = "Нажмите для назначения...";
			}
		}

		#region Обработка захвата Хоткея (NDA Замена)

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			e.Handled = true;
			HotkeyCaptureArea.Focus();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("AccentGreen");
			TbHotkeyDisplay.Text = "Нажмите сочетание клавиш...";
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("AccentGreen");

			// Захватываем сочетание напрямую через хук — так нажатие достаётся нам раньше,
			// чем его успела бы перехватить сторонняя программа через RegisterHotKey
			App._hotkeyService.StartCapture(OnHotkeyCaptured);
		}

		private void OnHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_selectedModifiers = modifiers;
			_selectedKey = key;

			TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextSecondary");
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderColor");
		}

		private void HotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			App._hotkeyService.CancelCapture();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderColor");

			if (_selectedKey != Key.None)
			{
				TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextSecondary");
			}
			else
			{
				TbHotkeyDisplay.Text = "Нажмите для назначения...";
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextSecondary");
			}
		}

		#endregion

		#region Сохранение Настроек

		private void BtnSave_Click(object sender, RoutedEventArgs e)
		{
			// Проверка: конфликт нового хоткея NDA с существующими шаблонами биндов
			if (_selectedKey != Key.None)
			{
				bool isDuplicate = App._bindKeys.Any(x => x.Key == _selectedKey && x.Modifiers == _selectedModifiers);
				if (isDuplicate)
				{
					App._notifyIcon?.ShowBalloonTip(
						"Ошибка",
						"Этот хоткей уже используется одним из текстовых биндов!",
						BalloonIcon.Warning);
					return;
				}
			}

			// Сохранение параметров
			Properties.Settings.Default.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
			Properties.Settings.Default.StartMinimized = ChkStartMinimized.IsChecked == true;
			Properties.Settings.Default.SelectedModifiers = (int)_selectedModifiers;
			Properties.Settings.Default.SelectedKey = (int)_selectedKey;

			Properties.Settings.Default.Save();

			// Перерегистрация всех горячих клавиш в системе
			App.RegisterDefaultHotkeys();

			// Применение автозапуска
			SetAutoStart(ChkAutoStart.IsChecked == true);

			App._notifyIcon?.ShowBalloonTip(
				"Сохранение",
				"Настройки успешно сохранены!",
				BalloonIcon.None);
		}

		private void ChkAutoStart_Click(object sender, RoutedEventArgs e)
		{
			SetAutoStart(ChkAutoStart.IsChecked == true);
		}

		#endregion

		#region Автозапуск через Реестр Windows

		private bool IsInAutoStart()
		{
			using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
			{
				return key?.GetValue(AppName) != null;
			}
		}

		private void SetAutoStart(bool enable)
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
						else
						{
							if (key.GetValue(AppName) != null)
							{
								key.DeleteValue(AppName, false);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Ошибка",
					$"Ошибка настройки автозапуска: {ex.Message}",
					BalloonIcon.None);
			}
		}

		#endregion

		#region Импорт и Экспорт

		private void Export_Click(object sender, RoutedEventArgs e)
		{
			App._storageService.ExportData();
		}

		private void Import_Click(object sender, RoutedEventArgs e)
		{
			App._storageService.ImportData();

			// Импорт мог изменить настройки/хоткей NDA-замены — обновляем то, что показано на экране
			LoadSettings();
			LoadHotkeyDisplay();
		}

		#endregion
	}
}