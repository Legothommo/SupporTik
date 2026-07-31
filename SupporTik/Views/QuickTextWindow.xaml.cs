using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SupporTik.Classes;
using SupporTik.Services;

namespace SupporTik
{
	/// <summary>
	/// Логика взаимодействия для QuickTextWindow.xaml
	/// </summary>
	public partial class QuickTextWindow : Window
	{
		#region WinAPI

		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_NOACTIVATE = 0x08000000;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		#endregion

		public QuickTextWindow()
		{
			InitializeComponent();
		}

		#region Инициализация элементов списка

		public void SetBinds(List<BindKeys> bindKeys)
		{
			sp_binds.Children.Clear();

			foreach (BindKeys bindKey in bindKeys)
			{
				Button button = new Button();

				// Обрезаем длинное имя
				string name = bindKey.Name.Length > 15
					? bindKey.Name.Substring(0, 14) + "..."
					: bindKey.Name;

				button.Content = name;
				button.Style = (Style)Application.Current.FindResource("MenuBtnStyle");
				button.Click += (sender, e) => TemplateClick(bindKey);

				sp_binds.Children.Add(button);
			}

			Key key = (Key)Properties.Settings.Default.SelectedKey;
			ModifierKeys mod = (ModifierKeys)Properties.Settings.Default.SelectedModifiers;

			// Проверяем наличие сочетания для NDA замены
			if (bindKeys.Exists(x => x.Key == key && x.Modifiers == mod))
			{
				Separator separator = new Separator
				{
					Style = (Style)Application.Current.FindResource("TraySeparatorStyle")
				};

				Button button = new Button
				{
					Content = "NDA Замена",
					Style = (Style)Application.Current.FindResource("MenuBtnStyle")
				};

				button.Click += (sender, e) =>
				{
					App._pasteService?.ReplaceSelectionInExternalApp();
					Hide();
				};

				sp_binds.Children.Add(separator);
				sp_binds.Children.Add(button);
			}
		}

		private void TemplateClick(BindKeys bindKey)
		{
			if (App._pasteService != null && bindKey != null)
			{
				App._pasteService.PasteText(bindKey.Text);
				this.Hide();
			}
		}

		#endregion

		#region Позиционирование и поведение окна

		/// <summary>
		/// Позиционирует окно рядом с мышкой и активирует его
		/// </summary>
		public void ShowAtCursor()
		{
			// Берем координаты мыши
			Point cursorPos = MouseHelper.GetCursorPosition(this);

			// Небольшой отступ от самого острия курсора (чтобы не перекрывать клик)
			double offsetX = 10;
			double offsetY = 10;

			double left = cursorPos.X + offsetX;
			double top = cursorPos.Y + offsetY;

			// Защита от вылета за границы экрана (WorkArea)
			double screenWidth = SystemParameters.WorkArea.Width;
			double screenHeight = SystemParameters.WorkArea.Height;

			if (left + this.ActualWidth > screenWidth)
			{
				left = cursorPos.X - this.ActualWidth - offsetX; // Переносим влево от курсора
			}

			if (top + this.ActualHeight > screenHeight)
			{
				top = cursorPos.Y - this.ActualHeight - offsetY; // Переносим вверх от курсора
			}

			// Применяем координаты
			this.Left = left;
			this.Top = top;

			this.Show();
			this.Activate();
		}

		// Закрываем или скрываем окно, если пользователь кликнул мимо (потерял фокус)
		private void Window_Deactivated(object sender, EventArgs e)
		{
			this.Hide();
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			// Получаем хэндл окна WPF и добавляем стиль WS_EX_NOACTIVATE (чтобы окно не перехватывало фокус ввода)
			var helper = new WindowInteropHelper(this);
			IntPtr hwnd = helper.Handle;

			int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
		}

		#endregion
	}
}