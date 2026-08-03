using SupporTik.Classes;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;

namespace SupporTik.Controls
{
	/// <summary>
	/// Карточка одной рекламной кампании (MarketingWindow). Данные читаются один раз
	/// при создании — карточки создаются заново при каждом новом поиске.
	/// </summary>
	public partial class MarketingItemPanel : UserControl
	{
		private readonly MarketingItem _item;

		public MarketingItem Item => _item;

		public bool IsSelected => ChkSelect.IsChecked == true;

		public MarketingItemPanel(MarketingItem item)
		{
			InitializeComponent();
			_item = item;

			TbPermalink.Text = string.IsNullOrEmpty(item.Permalink) ? "—" : item.Permalink;
			TbStatus.Text = string.IsNullOrEmpty(item.Status) ? "—" : item.Status;
			switch (TbStatus.Text)
			{
				case "Ожидает оплаты":
					BrStatus.Background = (Brush)Application.Current.FindResource("AccentYellow");
					break;
				case "Активна":
					BrStatus.Background = (Brush)Application.Current.FindResource("AccentGreen");
					break;
				case "Завершена":
					BrStatus.Background = (Brush)Application.Current.FindResource("AccentCoral");
					break;
				default:
					TbStatus.Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
					break;
			}
			TbRemain.Text = string.IsNullOrEmpty(item.Remain) ? "—" : item.Remain;

			// Роль показываем только если она реально искалась (чекбокс "Роли" при
			// поиске был отмечен) — иначе блок просто не нужен на карточке
			if (string.IsNullOrEmpty(item.Role))
			{
				sp_role.Visibility = Visibility.Collapsed;
			}
			else
			{
				TbRole.Text = item.Role;
			}

			BtnOpen.IsEnabled = !string.IsNullOrEmpty(item.Href);
		}

		private void BtnOpen_Click(object sender, RoutedEventArgs e)
		{
			if (!string.IsNullOrEmpty(_item.Href))
			{
				Process.Start(new ProcessStartInfo(_item.Href) { UseShellExecute = true });
			}
		}

		private void ChkSelect_Click(object sender, RoutedEventArgs e)
		{
			var checkBox = (CheckBox)sender;
			if (checkBox.IsChecked == true) Card.BorderBrush = (Brush)App.Current.FindResource("AccentGreen");
			else Card.BorderBrush = null;
		}
	}
}
