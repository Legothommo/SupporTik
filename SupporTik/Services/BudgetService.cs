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

		// csrfToken/sessionId свои у каждой кампании (подтверждено — не совпадают между
		// кампаниями), но лежат в HTML страницы кампании открытым текстом — обычный GET
		// с куками общей сессии находит их без выполнения JS (подтверждено вручную:
		// get-campaign-v3 с таким токеном отвечает 200 с реальными данными кампании).
		// Раньше для этого заходили на страницу кампании через WebView2 строго по
		// очереди (единственный экземпляр) — теперь это обычный HTTP-запрос, поэтому
		// проверку продления можно делать параллельно по нескольким кампаниям сразу
		// (см. MarketingWindowViewModel.ResolveRenewalAmountsAsync)
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

				var branding = campaignData?["settings"]?["brandingVersion"]?.Value<string>() ?? "";

				var payload = new JObject
				{
					["csrfToken"] = csrfToken,
					["sessionId"] = sessionId,
					["campaignId"] = campaignIdNumber,
					["permalinks"] = new JArray(permalinkNumber),
					["brandingVersion"] = branding,
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

					return result;
				}
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
