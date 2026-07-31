using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace SupporTik.Services
{
	public static class MouseHelper
	{
		#region WinAPI

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool GetCursorPos(out POINT lpPoint);

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		#endregion

		/// <summary>
		/// Возвращает позицию курсора мыши с учетом DPI-масштабирования главного окна приложения
		/// </summary>
		public static Point GetCursorPosition()
		{
			return GetCursorPosition(Application.Current.MainWindow);
		}

		/// <summary>
		/// Возвращает позицию курсора мыши с учетом DPI-масштабирования передаваемого элемента Visual
		/// </summary>
		public static Point GetCursorPosition(Visual visual)
		{
			if (!GetCursorPos(out POINT point))
			{
				return new Point(0, 0);
			}

			// Получаем коэффициенты масштабирования экрана (например, 125% или 150%)
			if (visual != null)
			{
				PresentationSource source = PresentationSource.FromVisual(visual);
				if (source?.CompositionTarget != null)
				{
					double dpiX = source.CompositionTarget.TransformFromDevice.M11;
					double dpiY = source.CompositionTarget.TransformFromDevice.M22;

					return new Point(point.X * dpiX, point.Y * dpiY);
				}
			}

			return new Point(point.X, point.Y);
		}
	}
}