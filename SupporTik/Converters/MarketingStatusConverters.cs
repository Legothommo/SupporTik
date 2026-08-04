using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SupporTik.Converters
{
	/// <summary>Статус кампании → цвет фона бейджа (те же цвета, что и в исходном MarketingItemPanel).</summary>
	public class StatusToBadgeBrushConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			string resourceKey;
			switch (value as string)
			{
				case "Ожидает оплаты": resourceKey = "AccentYellow"; break;
				case "Активна": resourceKey = "AccentGreen"; break;
				case "Завершена": resourceKey = "AccentCoral"; break;
				default: resourceKey = "SubtleFillBrush"; break;
			}

			return Application.Current.FindResource(resourceKey);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
	}

	/// <summary>Для неизвестного статуса текст бейджа перекрашивается в основной цвет темы вместо чёрного по умолчанию.</summary>
	public class StatusToTextForegroundConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			string status = value as string;
			bool isKnownStatus = status == "Ожидает оплаты" || status == "Активна" || status == "Завершена";

			return isKnownStatus ? Brushes.Black : (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
	}
}
