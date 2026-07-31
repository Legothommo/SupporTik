using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Classes;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для BindCreateWindow.xaml
	/// </summary>
	public partial class BindCreateWindow : Window
	{
		public BindKeys ResultBind { get; private set; }

		private Key _selectedKey = Key.None;
		private ModifierKeys _selectedModifiers = ModifierKeys.None;
		private bool _isCapturing = false;
		private readonly BindKeys _editingBind;

		public BindCreateWindow(BindKeys bind = null)
		{
			InitializeComponent();
			_editingBind = bind;

			if (_editingBind != null)
			{
				TbName.Text = _editingBind.Name;
				_selectedKey = _editingBind.Key;
				_selectedModifiers = _editingBind.Modifiers;

				TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
				TbText.Text = _editingBind.Text;
				TbLabel.Text = "✨ Изменение бинда";
			}
		}

		#region Обработка захвата Хоткея

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			_isCapturing = true;
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
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
			}
			else
			{
				TbHotkeyDisplay.Text = "Нажмите, чтобы задать хоткей...";
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
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextPrimary");

			_isCapturing = false;
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderColor");
		}

		private void HotkeyCaptureArea_KeyDown(object sender, KeyEventArgs e)
		{
			if (_isCapturing) e.Handled = true;
		}

		#endregion

		#region Сохранение и Управление Окном

		private void BtnSave_Click(object sender, RoutedEventArgs e)
		{
			string name = TbName.Text.Trim();
			string text = TbText.Text;

			if (string.IsNullOrWhiteSpace(name))
			{
				MessageBox.Show("Введите название бинда!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (_selectedKey == Key.None)
			{
				MessageBox.Show("Задайте сочетание клавиш!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (string.IsNullOrEmpty(text))
			{
				MessageBox.Show("Введите текст для автовставки!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			// Проверка на дублирование комбинации клавиш среди других биндов
			bool isDuplicate = App._bindKeys.Any(x =>
				x != _editingBind &&
				x.Key == _selectedKey &&
				x.Modifiers == _selectedModifiers);

			if (isDuplicate)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Ошибка",
					"Такое сочетание клавиш уже используется!",
					BalloonIcon.Warning);
				return;
			}

			ResultBind = new BindKeys
			{
				Name = name,
				Key = _selectedKey,
				Modifiers = _selectedModifiers,
				Text = text
			};

			DialogResult = true;
		}

		private void BtnCancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
		}

		private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left)
			{
				DragMove();
			}
		}

		private void Minimize_Click(object sender, RoutedEventArgs e)
		{
			WindowState = WindowState.Minimized;
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		#endregion
	}
}