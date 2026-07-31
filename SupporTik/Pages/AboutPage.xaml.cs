using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для AboutPage.xaml
	/// </summary>
	public partial class AboutPage : Page
	{
		private string _currentCatUrl;

		public AboutPage()
		{
			InitializeComponent();
			LoadRandomCat();
		}

		#region Обработчики событий

		private void BtnRefreshCat_Click(object sender, RoutedEventArgs e)
		{
			LoadRandomCat();
		}

		#endregion

		#region Загрузка котика (CATAAS API)

		private void LoadRandomCat()
		{
			try
			{
				TbCatStatus.Text = "Загрузка котика...";
				TbCatStatus.Visibility = Visibility.Visible;

				// Добавляем timestamp к URL, чтобы избежать кэширования ответов
				_currentCatUrl = $"https://cataas.com/cat?t={DateTime.Now.Ticks}";

				var bitmap = new BitmapImage();
				bitmap.BeginInit();
				bitmap.UriSource = new Uri(_currentCatUrl, UriKind.Absolute);
				bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
				bitmap.CacheOption = BitmapCacheOption.OnLoad;
				bitmap.EndInit();

				// Объявляем обработчик завершения загрузки
				EventHandler onDownloadCompleted = null;
				onDownloadCompleted = (s, e) =>
				{
					bitmap.DownloadCompleted -= onDownloadCompleted;
					TbCatStatus.Visibility = Visibility.Collapsed;
					ImgCat.Source = bitmap;
				};

				// Объявляем обработчик ошибки загрузки
				EventHandler<ExceptionEventArgs> onDownloadFailed = null;
				onDownloadFailed = (s, e) =>
				{
					bitmap.DownloadFailed -= onDownloadFailed;
					TbCatStatus.Text = "Не удалось загрузить котика 😿";
				};

				bitmap.DownloadCompleted += onDownloadCompleted;
				bitmap.DownloadFailed += onDownloadFailed;
			}
			catch
			{
				TbCatStatus.Text = "Ошибка соединения";
			}
		}

		#endregion
	}
}