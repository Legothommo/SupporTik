using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для AboutPage.xaml
	/// </summary>
	public partial class AboutPage : Page
	{
		// Один HttpClient на всё приложение — создавать новый на каждый запрос не стоит
		// (истощает пул сокетов при частых обновлениях). Таймаут — то, чего не хватало
		// в старой реализации на BitmapImage.UriSource: без него зависший запрос к
		// cataas.com вешал "Загрузка котика..." навсегда, без ошибки и без возможности понять,
		// что что-то пошло не так.
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(8)
		};

		// Растёт с каждым новым запросом — если пользователь быстро нажмёт "Обновить"
		// несколько раз подряд, устаревший (более медленный) ответ не перезатрёт свежий
		private int _requestVersion;

		public AboutPage()
		{
			InitializeComponent();
			_ = LoadRandomCatAsync();
		}

		#region Обработчики событий

		private void BtnRefreshCat_Click(object sender, RoutedEventArgs e)
		{
			_ = LoadRandomCatAsync();
		}

		#endregion

		#region Загрузка котика (CATAAS API)

		private async Task LoadRandomCatAsync()
		{
			int myVersion = ++_requestVersion;

			TbCatStatus.Text = "Загрузка котика...";
			TbCatStatus.Visibility = Visibility.Visible;

			const int maxAttempts = 2;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				try
				{
					// Добавляем timestamp к URL, чтобы избежать кэширования ответов
					string url = $"https://cataas.com/cat?t={DateTime.Now.Ticks}";
					byte[] bytes = await _httpClient.GetByteArrayAsync(url);

					if (myVersion != _requestVersion)
					{
						// Пока качали — успели запросить другого котика, этот ответ уже не нужен
						return;
					}

					var bitmap = new BitmapImage();
					using (var stream = new MemoryStream(bytes))
					{
						bitmap.BeginInit();
						bitmap.CacheOption = BitmapCacheOption.OnLoad;
						bitmap.StreamSource = stream;
						bitmap.EndInit();
					}
					bitmap.Freeze();

					TbCatStatus.Visibility = Visibility.Collapsed;
					ImgCat.Source = bitmap;
					return;
				}
				catch (Exception) when (attempt < maxAttempts)
				{
					// Транзиентная ошибка (таймаут, обрыв соединения и т.п.) — пробуем ещё раз
					await Task.Delay(500);
				}
				catch (Exception)
				{
					if (myVersion == _requestVersion)
					{
						TbCatStatus.Text = "Не удалось загрузить котика 😿";
						TbCatStatus.Visibility = Visibility.Visible;
					}
				}
			}
		}

		#endregion
	}
}
