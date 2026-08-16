using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	public class AboutPageViewModel : ViewModelBase
	{
		public string VersionText { get; } =
			"v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

		public string RuntimeText { get; } =
			$".NET Framework 4.8 · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";

		// Один HttpClient на всё приложение — создавать новый на каждый запрос не стоит
		// (истощает пул сокетов при частых обновлениях). Таймаут — то, чего не хватало
		// в старой реализации на BitmapImage.UriSource: без него зависший запрос вешал
		// "Загрузка котика..." навсегда, без ошибки и без возможности понять, что что-то
		// пошло не так.
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(8)
		};

		// TheCatAPI вместо cataas.com — сначала JSON с URL картинки, потом сама картинка
		// (два запроса вместо одного, зато без анти-кэш трюков — сервис сам отдаёт каждый
		// раз новую случайную кошку)
		private const string CatApiUrl = "https://api.thecatapi.com/v1/images/search";

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
					// Сначала JSON со ссылкой на случайную картинку, потом сама картинка
					string searchJson = await _httpClient.GetStringAsync($"{CatApiUrl}?t={DateTime.Now.Ticks}");
					string imageUrl = JArray.Parse(searchJson).FirstOrDefault()?["url"]?.Value<string>();

					if (string.IsNullOrEmpty(imageUrl))
					{
						throw new InvalidOperationException("TheCatAPI не вернул ссылку на картинку");
					}

					byte[] bytes = await _httpClient.GetByteArrayAsync(imageUrl);

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
