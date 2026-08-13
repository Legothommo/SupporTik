using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace SupporTik.Services
{
	/// <summary>
	/// Границы монитора под заданной точкой — в отличие от SystemParameters.WorkArea/
	/// PrimaryScreenWidth, которые всегда описывают только основной монитор. Нужно,
	/// чтобы всплывающие окна (QuickTextWindow, MarketingWindow) корректно
	/// позиционировались на многомониторных системах — иначе на втором мониторе
	/// расчёты "не вылезать за край экрана" сравнивались бы с шириной первого.
	/// </summary>
	public static class MonitorHelper
	{
		public struct MonitorBounds
		{
			/// <summary>Вся площадь монитора — для расчёта позиции "полностью за экраном".</summary>
			public Rect Bounds;

			/// <summary>Площадь монитора за вычетом панели задач.</summary>
			public Rect WorkArea;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

		private const uint MONITOR_DEFAULTTONEAREST = 2;

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct MONITORINFO
		{
			public int cbSize;
			public RECT rcMonitor;
			public RECT rcWork;
			public uint dwFlags;
		}

		/// <summary>
		/// Границы монитора, ближайшего к точке wpfPoint (в WPF device-independent
		/// единицах — как и SystemParameters.WorkArea). Пересчёт физические
		/// пиксели/WPF-единицы идёт через тот же приём, что и MouseHelper —
		/// TransformToDevice/TransformFromDevice главного окна (единый на всё
		/// приложение коэффициент, т.к. проект не per-monitor DPI aware). При любой
		/// проблеме (нет PresentationSource, WinAPI недоступен) откатывается на
		/// основной монитор.
		/// </summary>
		public static MonitorBounds GetMonitorBoundsForPoint(Point wpfPoint)
		{
			Visual reference = Application.Current?.MainWindow;
			PresentationSource source = reference != null ? PresentationSource.FromVisual(reference) : null;

			if (source?.CompositionTarget == null)
			{
				return new MonitorBounds
				{
					Bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
					WorkArea = SystemParameters.WorkArea
				};
			}

			Point devicePoint = source.CompositionTarget.TransformToDevice.Transform(wpfPoint);
			var pt = new POINT { X = (int)devicePoint.X, Y = (int)devicePoint.Y };

			IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
			var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };

			if (hMonitor == IntPtr.Zero || !GetMonitorInfo(hMonitor, ref info))
			{
				return new MonitorBounds
				{
					Bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
					WorkArea = SystemParameters.WorkArea
				};
			}

			Matrix fromDevice = source.CompositionTarget.TransformFromDevice;

			return new MonitorBounds
			{
				Bounds = RectFromDevice(fromDevice, info.rcMonitor),
				WorkArea = RectFromDevice(fromDevice, info.rcWork)
			};
		}

		private static Rect RectFromDevice(Matrix fromDevice, RECT rect)
		{
			Point topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
			Point bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));

			return new Rect(topLeft, bottomRight);
		}
	}
}
