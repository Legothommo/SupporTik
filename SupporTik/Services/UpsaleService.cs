using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SupporTik.Services
{
	public class UpsaleService : IUpsaleService
	{
		// id виджета с таблицей апсейлов на дашборде — если дашборд поменяется, нужно
		// заново найти id через Network → Payload после клика "Применить" в браузере
		private const string ChartId = "fhwtppiuuowk0";
		private const string Url = "https://datalens.yandex-team.ru/api/run";
		private const string DashboardReferer = "https://datalens.yandex-team.ru/qniendwn7xvwg-apseyl-po-nomeram-rk?tab=OW";

		public async Task<Dictionary<string, string>> CheckUpsalesAsync(
			string cookieHeader, string csrfToken, IReadOnlyList<string> campaignIds, IProgress<string> progress)
		{
			var results = new Dictionary<string, string>();

			var handler = new HttpClientHandler
			{
				UseCookies = false // куки передаём вручную через заголовок
			};

			using (var client = new HttpClient(handler))
			{
				client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
				client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);
				client.DefaultRequestHeaders.Add("Referer", DashboardReferer);
				client.DefaultRequestHeaders.Add("X-Dash-Info", "dashId=qniendwn7xvwg&dashTabId=OW");
				client.DefaultRequestHeaders.Add("X-DL-Display-Mode", "basic");
				client.DefaultRequestHeaders.Add("X-DL-TenantId", "common");
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

				for (int i = 0; i < campaignIds.Count; i++)
				{
					string campaignId = campaignIds[i];
					progress?.Report($"Апсейлы {i + 1}/{campaignIds.Count}...");

					try
					{
						results[campaignId] = await GetUpsaleAsync(client, campaignId);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"campaign_id={campaignId}: ошибка проверки апсейла — {ex.Message}");
						results[campaignId] = "Ошибка";
					}
				}
			}

			return results;
		}

		private static async Task<string> GetUpsaleAsync(HttpClient client, string campaignId)
		{
			// ВАЖНО: campaign_ggio, а не campaign_id — так работает на реальном дашборде,
			// хотя по названию поля логичнее выглядело бы наоборот
			var payload = new
			{
				id = ChartId,
				@params = new Dictionary<string, string[]>
				{
					["campaign_id"] = new[] { "" },
					["campaign_ggio"] = new[] { campaignId }
				},
				responseOptions = new
				{
					includeConfig = true,
					includeLogs = false
				}
			};

			string json = JsonConvert.SerializeObject(payload);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			var response = await client.PostAsync(Url, content);

			if (!response.IsSuccessStatusCode)
			{
				string errorBody = await response.Content.ReadAsStringAsync();
				throw new HttpRequestException($"Status {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
			}

			string body = await response.Content.ReadAsStringAsync();
			return ParseUpsaleValue(body);
		}

		private static string ParseUpsaleValue(string json)
		{
			try
			{
				var doc = JObject.Parse(json);
				var cell = doc["data"]["rows"][0]["cells"][1]["value"];
				return cell.Type == JTokenType.Null ? null : cell.ToString();
			}
			catch
			{
				return "Нет данных";
			}
		}
	}
}
