using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	public class AboutPageViewModel : ViewModelBase
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

		private BitmapImage _catImage;
		public BitmapImage CatImage
		{
			get => _catImage;
			set => SetProperty(ref _catImage, value);
		}

		private string _statusText = "Ищем котика...";
		public string StatusText
		{
			get => _statusText;
			set => SetProperty(ref _statusText, value);
		}

		private bool _isStatusVisible = true;
		public bool IsStatusVisible
		{
			get => _isStatusVisible;
			set => SetProperty(ref _isStatusVisible, value);
		}

		public AsyncRelayCommand RefreshCatCommand { get; }

		public AboutPageViewModel()
		{
			// AsyncRelayCommand сам блокирует повторный запуск, пока предыдущий не
			// завершится (успехом или ошибкой) — кнопка неактивна всё это время
			RefreshCatCommand = new AsyncRelayCommand(LoadRandomCatAsync);

			RefreshCatCommand.Execute(null);
		}

		private async Task LoadRandomCatAsync()
		{
			int myVersion = ++_requestVersion;

			StatusText = "Загрузка котика...";
			IsStatusVisible = true;

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

					IsStatusVisible = false;
					CatImage = bitmap;
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
						StatusText = "Не удалось загрузить котика 😿";
						IsStatusVisible = true;
					}
				}
			}
		}
	}
}
