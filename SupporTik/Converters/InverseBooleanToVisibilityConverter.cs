using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SupporTik.Converters
{
	/// <summary>true → Collapsed, false → Visible. Для пары "текст / поле ввода при редактировании".</summary>
	public class InverseBooleanToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			bool boolValue = value is bool b && b;
			return boolValue ? Visibility.Collapsed : Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
	}
}
