using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public class BudgetService : IBudgetService
	{
		private const string CampaignPageUrlTemplate =
			"https://yandex.ru/business/subscription/campaign/{0}";

		private const string CalculateBudgetUrl =
			"https://yandex.ru/business/subscription/api/billing/calculate-web-renewal-budget";

		private const string GetCampaignUrl =
			"https://yandex.ru/business/subscription/api/campaign/get-campaign-v3";

		private const string GetUpsaleConditionsUrl =
			"https://yandex.ru/business/subscription/api/campaign/get-upsale-conditions";

		private const string GetBusinessSnapshotUrl =
			"https://yandex.ru/business/subscription/api/business-snapshot/get";


		private static readonly Regex CsrfTokenRegex =
			new Regex(
				"\"csrfToken\"\\s*:\\s*\"([^\"]+)\"",
				RegexOptions.Compiled);

		private static readonly Regex SessionIdRegex =
			new Regex(
				"\"sessionId\"\\s*:\\s*\"([^\"]+)\"",
				RegexOptions.Compiled);


		/// <summary>
		/// Рассчитывает сумму продления и параллельно получает
		/// дополнительные флаги кампании.
		///
		/// result[0] = сумма продления
		/// result[1] = прогноз
		/// result[2] = IsMulti
		/// result[3] = HasBudgetIncreaseButton
		/// </summary>
		public async Task<Dictionary<int, string>> CalculateRenewalAmountAsync(
			string companyPermalink,
			string campaignId,
			string cookieHeader,
			int durationDays)
		{
			var result = new Dictionary<int, string>();

			if (!long.TryParse(
					companyPermalink,
					out long permalinkNumber) ||
				!long.TryParse(
					campaignId,
					out long campaignIdNumber))
			{
				return null;
			}


			using (var handler = new HttpClientHandler
			{
				UseCookies = false
			})
			using (var client = new HttpClient(handler))
			{
				ConfigureClient(
					client,
					cookieHeader);


				var info =
					await FetchCampaignInfoAsync(
						client,
						campaignId);

				if (info == null)
					return null;


				var payload = new JObject
				{
					["csrfToken"] =
						info.Value.CsrfToken,

					["sessionId"] =
						info.Value.SessionId,

					["campaignId"] =
						campaignIdNumber,

					["permalinks"] =
						new JArray(permalinkNumber),

					["brandingVersion"] =
						info.Value.Branding,

					["isMulti"] = true
				};


				using (var content =
					new StringContent(
						payload.ToString(
							Formatting.None),
						Encoding.UTF8,
						"application/json"))
				{
					var postResponse =
						await client.PostAsync(
							CalculateBudgetUrl,
							content);

					if (!postResponse.IsSuccessStatusCode)
					{
						string errorBody =
							await postResponse
								.Content
								.ReadAsStringAsync();

						throw new HttpRequestException(
							$"Status " +
							$"{(int)postResponse.StatusCode} " +
							$"{postResponse.StatusCode}: " +
							errorBody);
					}


					string postBody =
						await postResponse
							.Content
							.ReadAsStringAsync();

					var data =
						JObject.Parse(postBody)["data"];

					var websubscription =
						data?["websubscription"]?
						["RENEW_OPTIMAL"];

					if (websubscription == null)
						return null;


					result[0] =
						websubscription
							["durations"]?
							[durationDays.ToString()]?
							["amount"]?
							.ToString();


					int? prediction =
						websubscription
							["monthPrediction"]?
							["to"]?
							.Value<int?>();

					result[1] =
						prediction.HasValue
							? RoundToHundreds(
								prediction.Value)
							: null;


					result[2] =
						info.Value
							.IsMulti
							.ToString();

					result[3] =
						info.Value
							.HasBudgetIncreaseButton
							.ToString();


					return result;
				}
			}
		}


		/// <summary>
		/// Используется для кампаний, которым полный расчёт
		/// продления не требуется.
		/// </summary>
		public async Task<(
			bool IsMulti,
			bool HasBudgetIncreaseButton)>
			GetCampaignFlagsAsync(
				string campaignId,
				string cookieHeader)
		{
			using (var handler =
				new HttpClientHandler
				{
					UseCookies = false
				})
			using (var client =
				new HttpClient(handler))
			{
				ConfigureClient(
					client,
					cookieHeader);


				var info =
					await FetchCampaignInfoAsync(
						client,
						campaignId);


				if (info == null)
					return (false, false);


				return (
					info.Value.IsMulti,
					info.Value.HasBudgetIncreaseButton
				);
			}
		}


		/// <summary>
		/// Основная функция определения параметров кампании.
		///
		/// Логика HasBudgetIncreaseButton:
		///
		/// 1. get-upsale-conditions
		///    upsaleAllowed == false
		///    => кнопки нет.
		///
		/// 2. Если upsaleAllowed == true:
		///    смотрим get-campaign-v3.
		///
		/// 3. Если активна только MAPS:
		///    campaignPayMode на фронтенде становится maps-only,
		///    и upsale-контрол не рендерится.
		///
		/// 4. Также frontend считает кампанию maps-only,
		///    если businessSnapshot.settings.arbitrageDisabled == true.
		///
		/// Поэтому:
		///
		/// HasBudgetIncreaseButton =
		///     upsaleAllowed
		///     && !mapsOnlyByPlatforms
		///     && !arbitrageDisabled
		/// </summary>
		private async Task<(
			string CsrfToken,
			string SessionId,
			string Branding,
			bool IsMulti,
			bool HasBudgetIncreaseButton)?>
			FetchCampaignInfoAsync(
				HttpClient client,
				string campaignId)
		{
			// ============================================================
			// 1. Получаем HTML страницы кампании
			// ============================================================

			string campaignPageUrl =
				string.Format(
					CampaignPageUrlTemplate,
					campaignId);


			string campaignPageHtml =
				await client.GetStringAsync(
					campaignPageUrl);


			var csrfMatch =
				CsrfTokenRegex.Match(
					campaignPageHtml);

			var sessionMatch =
				SessionIdRegex.Match(
					campaignPageHtml);


			if (!csrfMatch.Success ||
				!sessionMatch.Success)
			{
				return null;
			}


			string csrfToken =
				csrfMatch
					.Groups[1]
					.Value;

			string sessionId =
				sessionMatch
					.Groups[1]
					.Value;


			// ============================================================
			// 2. get-campaign-v3
			// ============================================================

			JToken campaignData =
				await GetCampaignDataAsync(
					client,
					csrfToken,
					sessionId,
					campaignId);


			if (campaignData == null)
				return null;


			// ============================================================
			// 3. Общие данные кампании
			// ============================================================

			string branding =
				campaignData
					["settings"]?
					["brandingVersion"]?
					.Value<string>()
				?? "";


			bool isMulti =
				campaignData
					["settings"]?
					["isMulti"]?
					.Value<bool?>()
				?? false;


			// ============================================================
			// 4. Проверяем upsaleAllowed
			// ============================================================

			bool upsaleAllowed =
				await GetUpsaleAllowedAsync(
					client,
					csrfToken,
					sessionId,
					campaignId);


			// Если API прямо сказал, что upsale запрещён,
			// дальше business snapshot и платформы проверять
			// для кнопки уже не требуется.
			if (!upsaleAllowed)
			{
				return (
					csrfToken,
					sessionId,
					branding,
					isMulti,
					false
				);
			}


			// ============================================================
			// 5. Проверяем platforms -> mapsOnly
			// ============================================================

			bool mapsOnlyByPlatforms =
				IsMapsOnlyByPlatforms(
					campaignData);


			// ============================================================
			// 6. Получаем businessSnapshotId
			// ============================================================

			long? businessSnapshotId =
				GetBusinessSnapshotId(
					campaignData);


			// ============================================================
			// 7. Проверяем arbitrageDisabled
			// ============================================================

			bool arbitrageDisabled = false;


			if (businessSnapshotId.HasValue)
			{
				arbitrageDisabled =
					await GetArbitrageDisabledAsync(
						client,
						csrfToken,
						sessionId,
						businessSnapshotId.Value);
			}


			// ============================================================
			// 8. Финальный результат
			// ============================================================

			bool hasBudgetIncreaseButton =
				upsaleAllowed &&
				!mapsOnlyByPlatforms &&
				!arbitrageDisabled;


			return (
				csrfToken,
				sessionId,
				branding,
				isMulti,
				hasBudgetIncreaseButton
			);
		}


		/// <summary>
		/// Получает data из get-campaign-v3.
		/// </summary>
		private async Task<JToken>
			GetCampaignDataAsync(
				HttpClient client,
				string csrfToken,
				string sessionId,
				string campaignId)
		{
			string url =
				$"{GetCampaignUrl}" +
				$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
				$"&sessionId={Uri.EscapeDataString(sessionId)}" +
				$"&campaignId={Uri.EscapeDataString(campaignId)}";


			var response =
				await client.GetAsync(url);


			if (!response.IsSuccessStatusCode)
			{
				string errorBody =
					await response
						.Content
						.ReadAsStringAsync();

				throw new HttpRequestException(
					$"Status " +
					$"{(int)response.StatusCode} " +
					$"{response.StatusCode}: " +
					errorBody);
			}


			string body =
				await response
					.Content
					.ReadAsStringAsync();


			return JObject
				.Parse(body)
				["data"];
		}


		/// <summary>
		/// Проверяет get-upsale-conditions.
		///
		/// false означает, что кнопки точно нет.
		///
		/// При ошибке возвращаем false, чтобы не получить
		/// ложноположительный HasBudgetIncreaseButton.
		/// </summary>
		private async Task<bool>
			GetUpsaleAllowedAsync(
				HttpClient client,
				string csrfToken,
				string sessionId,
				string campaignId)
		{
			try
			{
				string url =
					$"{GetUpsaleConditionsUrl}" +
					$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
					$"&sessionId={Uri.EscapeDataString(sessionId)}" +
					$"&campaignId={Uri.EscapeDataString(campaignId)}";


				var response =
					await client.GetAsync(url);


				if (!response.IsSuccessStatusCode)
					return false;


				string body =
					await response
						.Content
						.ReadAsStringAsync();


				var data =
					JObject
						.Parse(body)
						["data"];


				return data?
					["upsaleAllowed"]?
					.Value<bool?>()
					?? false;
			}
			catch
			{
				return false;
			}
		}


		/// <summary>
		/// Проверяет businessSnapshot.settings.arbitrageDisabled.
		///
		/// Если arbitrageDisabled == true,
		/// frontend считает рекламу maps-only.
		/// </summary>
		private async Task<bool>
			GetArbitrageDisabledAsync(
				HttpClient client,
				string csrfToken,
				string sessionId,
				long businessSnapshotId)
		{
			try
			{
				string url =
					$"{GetBusinessSnapshotUrl}" +
					$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
					$"&sessionId={Uri.EscapeDataString(sessionId)}" +
					$"&id={businessSnapshotId}";


				var response =
					await client.GetAsync(url);


				if (!response.IsSuccessStatusCode)
				{
					return false;
				}


				string body =
					await response
						.Content
						.ReadAsStringAsync();


				var data =
					JObject
						.Parse(body)
						["data"];


				return data?
					["settings"]?
					["arbitrageDisabled"]?
					.Value<bool?>()
					?? false;
			}
			catch
			{
				return false;
			}
		}


		/// <summary>
		/// Определяет maps-only по campaignPlatforms.
		///
		/// Пример без кнопки:
		///
		/// MAPS          ON
		/// YABS          OFF
		/// DIRECT_SEARCH OFF
		/// SERP_AUCTION  OFF
		///
		/// Тогда единственная активная платформа — MAPS.
		/// </summary>
		private static bool IsMapsOnlyByPlatforms(
			JToken campaignData)
		{
			if (campaignData == null)
				return false;


			// ============================================================
			// Предпочтительно используем campaignPlatforms,
			// потому что там явно указаны статусы ON/OFF.
			// ============================================================

			var campaignPlatforms =
				campaignData
					["campaignPlatforms"]
					as JObject;


			if (campaignPlatforms != null &&
				campaignPlatforms.HasValues)
			{
				var activePlatforms =
					campaignPlatforms
						.Properties()
						.Where(platform =>
						{
							var statuses =
								platform
									.Value
									["statuses"]
									as JArray;


							if (statuses == null)
								return false;


							return statuses.Any(
								status =>
									string.Equals(
										status.Value<string>(),
										"ON",
										StringComparison.OrdinalIgnoreCase));
						})
						.Select(
							platform =>
								platform.Name)
						.ToList();


				return
					activePlatforms.Count == 1 &&
					string.Equals(
						activePlatforms[0],
						"MAPS",
						StringComparison.OrdinalIgnoreCase);
			}


			// ============================================================
			// Fallback на availablePlatforms для старого ответа API.
			//
			// Платформа считается активной, если disableReasons пустой.
			// ============================================================

			var availablePlatforms =
				campaignData
					["availablePlatforms"]
					as JObject;


			if (availablePlatforms == null ||
				!availablePlatforms.HasValues)
			{
				return false;
			}


			var activeAvailablePlatforms =
				availablePlatforms
					.Properties()
					.Where(platform =>
					{
						var disableReasons =
							platform
								.Value
								["disableReasons"]
								as JArray;


						return
							disableReasons == null ||
							disableReasons.Count == 0;
					})
					.Select(
						platform =>
							platform.Name)
					.ToList();


			return
				activeAvailablePlatforms.Count == 1 &&
				string.Equals(
					activeAvailablePlatforms[0],
					"MAPS",
					StringComparison.OrdinalIgnoreCase);
		}


		/// <summary>
		/// Пытается получить id business snapshot из get-campaign-v3.
		///
		/// Оставил несколько вариантов пути, потому что структура
		/// может отличаться у разных типов кампаний.
		/// </summary>
		private static long?
			GetBusinessSnapshotId(
				JToken campaignData)
		{
			if (campaignData == null)
				return null;


			// Самый вероятный вариант.
			long? id =
				campaignData
					["businessSnapshotId"]?
					.Value<long?>();

			if (id.HasValue)
				return id;


			// Возможный вложенный объект.
			id =
				campaignData
					["businessSnapshot"]?
					["id"]?
					.Value<long?>();

			if (id.HasValue)
				return id;


			// Возможный вариант внутри settings.
			id =
				campaignData
					["settings"]?
					["businessSnapshotId"]?
					.Value<long?>();

			return id;
		}


		/// <summary>
		/// Общие настройки HttpClient.
		/// </summary>
		private static void ConfigureClient(
			HttpClient client,
			string cookieHeader)
		{
			if (!string.IsNullOrWhiteSpace(
				cookieHeader))
			{
				client
					.DefaultRequestHeaders
					.Add(
						"Cookie",
						cookieHeader);
			}


			client
				.DefaultRequestHeaders
				.Accept
				.Add(
					new MediaTypeWithQualityHeaderValue(
						"application/json"));


			client
				.DefaultRequestHeaders
				.TryAddWithoutValidation(
					"X-Requested-With",
					"XMLHttpRequest");
		}


		public static string RoundToHundreds(
			int value)
		{
			if (value < 100)
				return value.ToString();


			return (
				Math.Round(
					value / 100.0,
					MidpointRounding.AwayFromZero)
				* 100
			).ToString();
		}
	}
}