using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SupporTik.Classes;

namespace SupporTik.Services
{
	public class MarketingCampaignService : IMarketingCampaignService
	{
		private const string CampaignListApiUrl = "https://yandex.ru/business/priority/api/campaign-list/get";
		private const int PageSize = 20;

		// Ссылка "Открыть" на карточке
		private const string CampaignOpenUrlTemplate = "https://yandex.ru/business/subscription/campaign/{0}";

		public async Task<List<MarketingItem>> SearchAsync(string uid, YandexBusinessAuth auth, IProgress<string> progress)
		{
			// UseCookies = false обязателен: с включённым (по умолчанию) CookieContainer
			// HttpClient сам управляет куками и путается с вручную выставленным заголовком
			// Cookie — сервер в ответ отвечал "Invalid csrf token", хотя сам токен был верным
			using (var handler = new HttpClientHandler { UseCookies = false })
			using (var client = new HttpClient(handler))
			{
				client.DefaultRequestHeaders.Add("Cookie", auth.CookieHeader);
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

				progress?.Report("Страница 1...");
				var (firstItems, total) = await FetchPageAsync(client, uid, auth, offset: 0);

				var allItems = new List<MarketingItem>(firstItems);

				if (firstItems.Count == 0 || total <= PageSize)
				{
					return allItems;
				}

				// Первую страницу пришлось дождаться, чтобы узнать total — остальные не
				// зависят друг от друга, поэтому запрашиваем их все параллельно, а не по
				// очереди (раньше поиск при большом числе кампаний ждал по одной странице)
				var remainingOffsets = new List<int>();
				for (int offset = PageSize; offset < total; offset += PageSize)
				{
					remainingOffsets.Add(offset);
				}

				int totalPages = remainingOffsets.Count + 1;
				int completedPages = 1;

				var pageTasks = remainingOffsets.Select(async offset =>
				{
					var page = await FetchPageAsync(client, uid, auth, offset);
					int done = Interlocked.Increment(ref completedPages);
					progress?.Report($"Страница {done}/{totalPages}...");
					return page;
				});

				var pages = await Task.WhenAll(pageTasks);

				foreach (var page in pages)
				{
					allItems.AddRange(page.Items);
				}

				return allItems;
			}
		}

		private async Task<(List<MarketingItem> Items, int Total)> FetchPageAsync(
			HttpClient client, string uid, YandexBusinessAuth auth, int offset)
		{
			string url = $"{CampaignListApiUrl}?csrfToken={Uri.EscapeDataString(auth.CsrfToken)}" +
				$"&sessionId={Uri.EscapeDataString(auth.SessionId)}" +
				$"&limit={PageSize}&offset={offset}" +
				$"&userUid={Uri.EscapeDataString(uid)}" +
				$"&managerUid={Uri.EscapeDataString(auth.ManagerUid)}";

			var response = await client.GetAsync(url);

			if (!response.IsSuccessStatusCode)
			{
				string errorBody = await response.Content.ReadAsStringAsync();
				throw new HttpRequestException($"Status {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
			}

			string body = await response.Content.ReadAsStringAsync();
			var data = JObject.Parse(body)["data"];

			int total = data?["total"]?.Value<int>() ?? 0;
			var results = data?["result"] as JArray;

			var items = new List<MarketingItem>();
			if (results != null)
			{
				foreach (var campaign in results)
				{
					items.Add(ParseCampaign(campaign, uid));
				}
			}

			return (items, total);
		}

		private static MarketingItem ParseCampaign(JToken campaign, string uid)
		{
			// "id" — собственный числовой ID кампании (используется для ссылки "Открыть"),
			// пермалинк компании — отдельное число, лежит в companyDescription
			string permalink = campaign["id"]?.ToString() ?? string.Empty;
			string companyPermalink = campaign["companyDescription"]?["permalink"]?.ToString() ?? string.Empty;

			int? remainingSum = campaign["remainingSum"]?.Value<int?>();
			int? remainingDays = campaign["remainingDays"]?.Value<int?>();
			string remain = remainingSum.HasValue && remainingDays.HasValue
				? $"{remainingSum.Value} ₽ · {remainingDays.Value} дней"
				: string.Empty;

			return new MarketingItem
			{
				Permalink = permalink,
				CompanyPermalink = companyPermalink,
				Status = MapStatus(campaign["status"]?.Value<string>()),
				Remain = remain,
				Href = string.IsNullOrEmpty(permalink) ? null : string.Format(CampaignOpenUrlTemplate, permalink),
				Role = ParseRole(campaign["users"] as JArray, uid)
			};
		}

		/// <summary>
		/// Роль пользователя приходит прямо в ответе campaign-list/get (массив users с
		/// id и balanceDelegate) — отдельный заход на страницу настроек кампании
		/// (как раньше, через HTML) больше не нужен.
		/// </summary>
		private static string ParseRole(JArray users, string uid)
		{
			const string notFound = "Не найден в списке пользователей";

			if (users == null)
			{
				return notFound;
			}

			foreach (var user in users)
			{
				if (user["id"]?.ToString() == uid)
				{
					bool isOwner = user["balanceDelegate"]?.Value<bool>() ?? false;
					return isOwner ? "Владелец" : "Наблюдатель";
				}
			}

			return notFound;
		}

		private static string MapStatus(string statusKey)
		{
			switch (statusKey)
			{
				case "RUNNING":
				case "OK": return "Активна";
				case "STOPPED_BY_USER":
				case "STOPPED_BY_SYSTEM": return "Остановлено";
				case "ON_PAUSE":
				case "PAUSED": return "На паузе";
				case "WAITING": return "Ожидает оплаты";
				case "FINISHED": return "Завершена";
				case "MODERATION": return "Модерация";
				case "PREPARATION": return "Подготовка";
				case "PRESALE": return "Создается";
				case "LICENSE_NEEDED": return "Нужна лицензия";
				case "SITE_ISSUE": return "Сайт недоступен";
				case "DELAYED_START": return "Подготовка";
				default: return statusKey; // неизвестный статус — показываем как есть, а не теряем
			}
		}
	}
}
