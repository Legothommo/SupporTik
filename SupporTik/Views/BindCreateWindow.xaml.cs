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
		private readonly BindKeys _editingBind;

		/// <param name="bind">Редактируемый бинд, либо (при presetHotkeyOnly) только источник сочетания клавиш.</param>
		/// <param name="presetHotkeyOnly">
		/// true — не редактирование, а добавление нового шаблона с уже готовым сочетанием
		/// клавиш (например, "+ Добавить шаблон" внутри группы биндов с общим хоткеем).
		/// </param>
		public BindCreateWindow(BindKeys bind = null, bool presetHotkeyOnly = false)
		{
			InitializeComponent();

			if (bind != null && !presetHotkeyOnly)
			{
				_editingBind = bind;
				TbName.Text = bind.Name;
				TbText.Text = bind.Text;
				TbLabel.Text = "✨ Изменение бинда";
			}

			if (bind != null)
			{
				_selectedKey = bind.Key;
				_selectedModifiers = bind.Modifiers;

				TbHotkeyDisplay.Text = KeyExtensions.ToFriendlyShortcut(_selectedModifiers, _selectedKey);
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
			}
		}

		#region Обработка захвата Хоткея

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
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
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderColor");
		}

		private void HotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			App._hotkeyService.CancelCapture();
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