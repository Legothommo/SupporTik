using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SupporTik.Classes;
using SupporTik.Services;
using SupporTik.ViewModels;

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

		private readonly QuickMenuViewModel _viewModel = new QuickMenuViewModel();

		public QuickTextWindow()
		{
			InitializeComponent();

			DataContext = _viewModel;
			_viewModel.EntryInvoked += (s, e) => Hide();
		}

		/// <param name="groupTitle">Название папки, если пользователь его задал (иначе null/пусто — заголовок скрыт).</param>
		public void SetEntries(string groupTitle, List<QuickMenuEntry> entries)
		{
			_viewModel.SetEntries(groupTitle, entries);
		}

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
