using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public class BudgetService : IBudgetService
	{
		private const string CampaignPageUrlTemplate = "https://yandex.ru/business/subscription/campaign/{0}";
		private const string CalculateBudgetUrl = "https://yandex.ru/business/subscription/api/billing/calculate-web-renewal-budget";
		private const string GetCampaignUrl = "https://yandex.ru/business/subscription/api/campaign/get-campaign-v3";
		private const string GetUpsaleConditionsUrl = "https://yandex.ru/business/subscription/api/campaign/get-upsale-conditions";

		// csrfToken/sessionId свои у каждой кампании (подтверждено — не совпадают между
		// кампаниями), но лежат в HTML страницы кампании открытым текстом — обычный GET
		// с куками общей сессии находит их без выполнения JS (подтверждено вручную:
		// get-campaign-v3 с таким токеном отвечает 200 с реальными данными кампании).
		// Раньше для этого заходили на страницу кампании через WebView2 строго по
		// очереди (единственный экземпляр) — теперь это обычный HTTP-запрос, поэтому
		// проверку продления можно делать параллельно по нескольким кампаниям сразу
		// (см. MarketingWindowViewModel.ResolveCampaignDetailsAsync)
		private static readonly Regex CsrfTokenRegex = new Regex("\"csrfToken\"\\s*:\\s*\"([^\"]+)\"");
		private static readonly Regex SessionIdRegex = new Regex("\"sessionId\"\\s*:\\s*\"([^\"]+)\"");

		public async Task<Dictionary<int, string>> CalculateRenewalAmountAsync(string companyPermalink, string campaignId, string cookieHeader, int durationDays)
		{
			var result = new Dictionary<int, string>();
			if (!long.TryParse(companyPermalink, out long permalinkNumber) || !long.TryParse(campaignId, out long campaignIdNumber))
			{
				return null;
			}

			// UseCookies = false по той же причине, что и в MarketingCampaignService —
			// иначе встроенный CookieContainer конфликтует с вручную выставленным Cookie
			using (var handler = new HttpClientHandler { UseCookies = false })
			using (var client = new HttpClient(handler))
			{
				client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

				var info = await FetchCampaignInfoAsync(client, campaignId);
				if (info == null)
				{
					return null;
				}

				var payload = new JObject
				{
					["csrfToken"] = info.Value.CsrfToken,
					["sessionId"] = info.Value.SessionId,
					["campaignId"] = campaignIdNumber,
					["permalinks"] = new JArray(permalinkNumber),
					["brandingVersion"] = info.Value.Branding,
					["isMulti"] = true
				};

				using (var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json"))
				{
					var postResponse = await client.PostAsync(CalculateBudgetUrl, content);

					if (!postResponse.IsSuccessStatusCode)
					{
						string errorBody = await postResponse.Content.ReadAsStringAsync();
						throw new HttpRequestException($"Status {(int)postResponse.StatusCode} {postResponse.StatusCode}: {errorBody}");
					}

					string postBody = await postResponse.Content.ReadAsStringAsync();
					var data = JObject.Parse(postBody)["data"];
					var websubscription = data?["websubscription"]?["RENEW_OPTIMAL"];

					if (websubscription == null)
					{
						return null;
					}

					result[0] = websubscription["durations"]?[durationDays.ToString()]?["amount"]?.ToString();
					int? value = websubscription["monthPrediction"]?["to"]?.Value<int?>();
					result[1] = value.HasValue
								? RoundToHundreds(value.Value)
								: null;
					result[2] = info.Value.IsMulti.ToString();
					result[3] = info.Value.HasBudgetIncreaseButton.ToString();

					return result;
				}
			}
		}

		/// <summary>
		/// isMulti и HasBudgetIncreaseButton — для карточек-апсейлов (числовой UpsaleValue),
		/// которые не идут через полный расчёт продления (см. CalculateRenewalAmountAsync),
		/// но эти два флага им всё равно нужны (см.
		/// MarketingWindowViewModel.ResolveCampaignDetailsAsync).
		/// </summary>
		public async Task<(bool IsMulti, bool HasBudgetIncreaseButton)> GetCampaignFlagsAsync(string campaignId, string cookieHeader)
		{
			using (var handler = new HttpClientHandler { UseCookies = false })
			using (var client = new HttpClient(handler))
			{
				client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

				var info = await FetchCampaignInfoAsync(client, campaignId);
				return info == null ? (false, false) : (info.Value.IsMulti, info.Value.HasBudgetIncreaseButton);
			}
		}

		/// <summary>
		/// csrfToken/sessionId свои у каждой кампании (подтверждено — не совпадают между
		/// кампаниями), но лежат в HTML страницы кампании открытым текстом — обычный GET
		/// с куками общей сессии находит их без выполнения JS (подтверждено вручную:
		/// get-campaign-v3 с таким токеном отвечает 200 с реальными данными кампании).
		/// Общая часть для CalculateRenewalAmountAsync и GetCampaignFlagsAsync — оба
		/// начинаются с одного и того же запроса. Возвращает null, если не удалось найти
		/// csrfToken/sessionId на странице кампании или get-campaign-v3 не отдал данные.
		/// </summary>
		private async Task<(string CsrfToken, string SessionId, string Branding, bool IsMulti, bool HasBudgetIncreaseButton)?> FetchCampaignInfoAsync(HttpClient client, string campaignId)
		{
			string campaignPageHtml = await client.GetStringAsync(string.Format(CampaignPageUrlTemplate, campaignId));

			var csrfMatch = CsrfTokenRegex.Match(campaignPageHtml);
			var sessionMatch = SessionIdRegex.Match(campaignPageHtml);

			if (!csrfMatch.Success || !sessionMatch.Success)
			{
				return null;
			}

			string csrfToken = csrfMatch.Groups[1].Value;
			string sessionId = sessionMatch.Groups[1].Value;

			string url = $"{GetCampaignUrl}?csrfToken={csrfToken}" +
					$"&sessionId={sessionId}" +
					$"&campaignId={campaignId}";

			var getResponse = await client.GetAsync(url);

			if (!getResponse.IsSuccessStatusCode)
			{
				string errorBody = await getResponse.Content.ReadAsStringAsync();
				throw new HttpRequestException($"Status {(int)getResponse.StatusCode} {getResponse.StatusCode}: {errorBody}");
			}

			string getBody = await getResponse.Content.ReadAsStringAsync();
			var campaignData = JObject.Parse(getBody)["data"];

			string branding = campaignData?["settings"]?["brandingVersion"]?.Value<string>() ?? "";

			// Есть в ответе get-campaign-v3 — берём его значение, нет — false (не путать
			// с isMulti в payload запроса CalculateRenewalAmountAsync, тот захардкожен отдельно)
			bool isMulti = campaignData?["settings"]?["isMulti"]?.Value<bool?>() ?? false;

			// Подтверждено на реальных данных: businessSnapshotReviewedStatus == "NOT_REVIEWED"
			// совпадает с ОТСУТСТВИЕМ кнопки "Увеличить бюджет" на живой странице; любой
			// другой статус (например "INFO_MISSED") — кнопка есть
			bool isReviewedOk = campaignData?["settings"]?["businessSnapshotReviewedStatus"]?.Value<string>() != "NOT_REVIEWED";

			// upsaleAllowed из get-upsale-conditions (настоящий эндпоинт, стоящий за решением
			// о кнопке — найден в отдельном Selenium-скрипте) — подтверждено, что сам по себе
			// не различает (был true у обеих тестовых кампаний, хотя кнопка только у одной —
			// полная логика на сайте учитывает ещё campaignPayMode/цепочки, недоступные без
			// браузера). Поэтому объединяем через "И" с businessSnapshotReviewedStatus —
			// строго безопаснее: кнопку считаем показанной только если оба сигнала "за"
			bool upsaleAllowed = await GetUpsaleAllowedAsync(client, csrfToken, sessionId, campaignId);

			bool hasBudgetIncreaseButton = isReviewedOk && upsaleAllowed;

			return (csrfToken, sessionId, branding, isMulti, hasBudgetIncreaseButton);
		}

		/// <summary>
		/// upsaleAllowed из get-upsale-conditions — сам по себе не полностью определяет
		/// видимость кнопки (см. FetchCampaignInfoAsync), но используется как
		/// дополнительное условие. Возвращает true (не блокирует), если запрос не удался —
		/// чтобы не потерять уже проверенный сигнал businessSnapshotReviewedStatus из-за
		/// сбоя этого отдельного запроса.
		/// </summary>
		private async Task<bool> GetUpsaleAllowedAsync(HttpClient client, string csrfToken, string sessionId, string campaignId)
		{
			try
			{
				string url = $"{GetUpsaleConditionsUrl}?csrfToken={csrfToken}&sessionId={sessionId}&campaignId={campaignId}";
				var response = await client.GetAsync(url);

				if (!response.IsSuccessStatusCode)
				{
					return true;
				}

				string body = await response.Content.ReadAsStringAsync();
				var data = JObject.Parse(body)["data"];

				return data?["upsaleAllowed"]?.Value<bool?>() ?? true;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static string RoundToHundreds(int value)
		{
			if (value < 100)
				return value.ToString();

			return (Math.Round(value / 100.0, MidpointRounding.AwayFromZero) * 100).ToString();
		}
	}
}
