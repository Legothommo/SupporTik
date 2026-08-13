using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public class UpdateInfo
	{
		public string Version { get; }
		public string ReleaseUrl { get; }

		public UpdateInfo(string version, string releaseUrl)
		{
			Version = version;
			ReleaseUrl = releaseUrl;
		}
	}

	/// <summary>
	/// Сверяет версию сборки с последним релизом на GitHub. Не критично для работы
	/// приложения — недоступность GitHub, отсутствие сети или превышенный анонимный
	/// rate limit просто означают "обновлений нет", без падений и без ретраев.
	/// </summary>
	public class UpdateCheckService
	{
		private const string LatestReleaseApiUrl = "https://api.github.com/repos/Legothommo/SupporTik/releases/latest";

		private static readonly HttpClient _httpClient = CreateHttpClient();

		private static HttpClient CreateHttpClient()
		{
			var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

			// GitHub API отклоняет запросы без User-Agent
			client.DefaultRequestHeaders.UserAgent.ParseAdd("SupporTik-UpdateCheck");

			return client;
		}

		public async Task<UpdateInfo> CheckAsync()
		{
			try
			{
				string json = await _httpClient.GetStringAsync(LatestReleaseApiUrl);
				var release = JObject.Parse(json);

				string tagName = release.Value<string>("tag_name") ?? string.Empty;
				string releaseUrl = release.Value<string>("html_url") ?? string.Empty;

				// Теги в этом репозитории без префикса "v" ("2.5.0"), но на случай,
				// если он когда-нибудь появится — срезаем его перед парсингом
				string versionText = tagName.TrimStart('v', 'V');

				if (!Version.TryParse(versionText, out Version remoteVersion))
				{
					return null;
				}

				Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

				if (remoteVersion > currentVersion)
				{
					return new UpdateInfo(versionText, releaseUrl);
				}
			}
			catch (Exception)
			{
				// Тихо игнорируем — см. комментарий к классу
			}

			return null;
		}
	}
}
