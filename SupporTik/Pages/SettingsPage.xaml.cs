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
		private bool _isCapturing = false;

		public SettingsPage()
		{
			InitializeComponent();
			LoadSettings();

			if (Properties.Settings.Default.SelectedKey != 0 && (ModifierKeys)Properties.Settings.Default.SelectedModifiers != 0)
			{
				_selectedKey = (Key)Properties.Settings.Default.SelectedKey;
				_selectedModifiers = (ModifierKeys)Properties.Settings.Default.SelectedModifiers;
				TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
			}
		}

		private void LoadSettings()
		{
			ChkAutoStart.IsChecked = IsInAutoStart();
			ChkMinimizeToTray.IsChecked = Properties.Settings.Default.MinimizeToTray;
			ChkStartMinimized.IsChecked = Properties.Settings.Default.StartMinimized;
		}

		#region Обработка захвата Хоткея (NDA Замена)

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			_isCapturing = true;
			e.Handled = true;
			HotkeyCaptureArea.Focus();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("AccentGreen");
			TbHotkeyDisplay.Text = "Нажмите сочетание клавиш...";
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("AccentGreen");
		}

		private void HotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			_isCapturing = false;
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

		private void HotkeyCaptureArea_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (!_isCapturing) return;

			e.Handled = true;

			Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;

			// Игнорируем одиночные нажатия клавиш-модификаторов
			if (key == Key.LeftCtrl || key == Key.RightCtrl ||
				key == Key.LeftAlt || key == Key.RightAlt ||
				key == Key.LeftShift || key == Key.RightShift ||
				key == Key.LWin || key == Key.RWin)
			{
				return;
			}

			_selectedModifiers = Keyboard.Modifiers;
			_selectedKey = key;

			TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextSecondary");

			_isCapturing = false;
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderColor");
		}

		private void HotkeyCaptureArea_KeyDown(object sender, KeyEventArgs e)
		{
			if (_isCapturing) e.Handled = true;
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
		}

		#endregion
	}
}