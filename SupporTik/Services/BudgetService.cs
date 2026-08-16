using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public class BudgetService : IBudgetService
	{
		private sealed class CampaignInfo
		{
			public string CsrfToken { get; set; }
			public string SessionId { get; set; }
			public string Branding { get; set; }
			public bool IsMulti { get; set; }
			public bool HasBudgetIncreaseButton { get; set; }
		}

		private sealed class CampaignInfoCacheEntry
		{
			public CampaignInfo Value { get; set; }
			public DateTime StoredAt { get; set; }
		}

		private const int MaxCachedCampaigns = 500;
		private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
		private readonly object _cacheLock = new object();
		private readonly Dictionary<string, CampaignInfoCacheEntry> _campaignInfoCache =
			new Dictionary<string, CampaignInfoCacheEntry>();
		private string _cacheCookieHeader;

		private static readonly HttpClient HttpClient = new HttpClient(
			new HttpClientHandler { UseCookies = false });

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
		/// </summary>
		public async Task<RenewalCalculationResult> CalculateRenewalAmountAsync(
			string companyPermalink,
			string campaignId,
			string cookieHeader,
			int durationDays,
			CancellationToken cancellationToken)
		{
			if (!long.TryParse(
					companyPermalink,
					out long permalinkNumber) ||
				!long.TryParse(
					campaignId,
					out long campaignIdNumber))
			{
				return null;
			}


				var info =
					await FetchCampaignInfoAsync(
						HttpClient,
						cookieHeader,
						campaignId,
						cancellationToken);

				if (info == null)
					return null;


				var payload = new JObject
				{
					["csrfToken"] =
						info.CsrfToken,

					["sessionId"] =
						info.SessionId,

					["campaignId"] =
						campaignIdNumber,

					["permalinks"] =
						new JArray(permalinkNumber),

					["brandingVersion"] =
						info.Branding,

					["isMulti"] = true
				};


				string payloadJson = payload.ToString(Formatting.None);
				using (var postResponse =
					await SendAsync(
						HttpClient,
						HttpMethod.Post,
						CalculateBudgetUrl,
						cookieHeader,
						() => new StringContent(payloadJson, Encoding.UTF8, "application/json"),
						cancellationToken))
				{
					HttpRetryPolicy.EnsureSuccess(
						postResponse,
						"Не удалось рассчитать стоимость продления.",
						"BudgetService.CalculateRenewalAmountAsync");


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


					string amount =
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

					string roundedPrediction =
						prediction.HasValue
							? RoundToHundreds(
								prediction.Value)
							: null;


					return new RenewalCalculationResult
					{
						Amount = amount,
						Prediction = roundedPrediction,
						IsMulti = info.IsMulti,
						HasBudgetIncreaseButton = info.HasBudgetIncreaseButton
					};
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
				string cookieHeader,
				CancellationToken cancellationToken)
		{
				var info =
					await FetchCampaignInfoAsync(
						HttpClient,
						cookieHeader,
						campaignId,
						cancellationToken);


				if (info == null)
					return (false, false);


				return (
					info.IsMulti,
					info.HasBudgetIncreaseButton
				);
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
		private async Task<CampaignInfo>
			FetchCampaignInfoAsync(
				HttpClient client,
				string cookieHeader,
				string campaignId,
				CancellationToken cancellationToken)
		{
			CampaignInfo cached = TryGetCachedCampaignInfo(cookieHeader, campaignId);
			if (cached != null)
			{
				return cached;
			}

			// ============================================================
			// 1. Получаем HTML страницы кампании
			// ============================================================

			string campaignPageUrl =
				string.Format(
					CampaignPageUrlTemplate,
					campaignId);


			string campaignPageHtml = await GetRequiredStringAsync(
				client,
				campaignPageUrl,
				cookieHeader,
				cancellationToken);


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
					cookieHeader,
					csrfToken,
					sessionId,
					campaignId,
					cancellationToken);


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
					cookieHeader,
					csrfToken,
					sessionId,
					campaignId,
					cancellationToken);


			// Если API прямо сказал, что upsale запрещён,
			// дальше business snapshot и платформы проверять
			// для кнопки уже не требуется.
			if (!upsaleAllowed)
			{
				var noUpsaleInfo = new CampaignInfo
				{
					CsrfToken = csrfToken,
					SessionId = sessionId,
					Branding = branding,
					IsMulti = isMulti,
					HasBudgetIncreaseButton = false
				};
				StoreCampaignInfo(cookieHeader, campaignId, noUpsaleInfo);
				return noUpsaleInfo;
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
					cookieHeader,
					csrfToken,
					sessionId,
					businessSnapshotId.Value,
					cancellationToken);
			}


			// ============================================================
			// 8. Финальный результат
			// ============================================================

			bool hasBudgetIncreaseButton =
				upsaleAllowed &&
				!mapsOnlyByPlatforms &&
				!arbitrageDisabled;


			var info = new CampaignInfo
			{
				CsrfToken = csrfToken,
				SessionId = sessionId,
				Branding = branding,
				IsMulti = isMulti,
				HasBudgetIncreaseButton = hasBudgetIncreaseButton
			};
			StoreCampaignInfo(cookieHeader, campaignId, info);
			return info;
		}

		private CampaignInfo TryGetCachedCampaignInfo(string cookieHeader, string campaignId)
		{
			lock (_cacheLock)
			{
				ResetCacheForNewSession(cookieHeader);
				if (_campaignInfoCache.TryGetValue(campaignId, out CampaignInfoCacheEntry entry))
				{
					if (DateTime.UtcNow - entry.StoredAt <= CacheLifetime)
					{
						return entry.Value;
					}

					_campaignInfoCache.Remove(campaignId);
				}
			}

			return null;
		}

		private void StoreCampaignInfo(string cookieHeader, string campaignId, CampaignInfo info)
		{
			lock (_cacheLock)
			{
				ResetCacheForNewSession(cookieHeader);
				if (_campaignInfoCache.Count >= MaxCachedCampaigns)
				{
					_campaignInfoCache.Clear();
				}

				_campaignInfoCache[campaignId] = new CampaignInfoCacheEntry
				{
					Value = info,
					StoredAt = DateTime.UtcNow
				};
			}
		}

		private void ResetCacheForNewSession(string cookieHeader)
		{
			if (!string.Equals(_cacheCookieHeader, cookieHeader, StringComparison.Ordinal))
			{
				_campaignInfoCache.Clear();
				_cacheCookieHeader = cookieHeader;
			}
		}


		/// <summary>
		/// Получает data из get-campaign-v3.
		/// </summary>
		private async Task<JToken>
			GetCampaignDataAsync(
				HttpClient client,
				string cookieHeader,
				string csrfToken,
				string sessionId,
				string campaignId,
				CancellationToken cancellationToken)
		{
			string url =
				$"{GetCampaignUrl}" +
				$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
				$"&sessionId={Uri.EscapeDataString(sessionId)}" +
				$"&campaignId={Uri.EscapeDataString(campaignId)}";


			using (var response = await SendAsync(
				client, HttpMethod.Get, url, cookieHeader, null, cancellationToken))
			{
				HttpRetryPolicy.EnsureSuccess(
					response,
					"Не удалось получить параметры рекламной кампании.",
					"BudgetService.GetCampaignDataAsync");

				string body = await response.Content.ReadAsStringAsync();
				return JObject.Parse(body)["data"];
			}
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
				string cookieHeader,
				string csrfToken,
				string sessionId,
				string campaignId,
				CancellationToken cancellationToken)
		{
			try
			{
				string url =
					$"{GetUpsaleConditionsUrl}" +
					$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
					$"&sessionId={Uri.EscapeDataString(sessionId)}" +
					$"&campaignId={Uri.EscapeDataString(campaignId)}";


				using (var response = await SendAsync(
					client, HttpMethod.Get, url, cookieHeader, null, cancellationToken))
				{
					if (!response.IsSuccessStatusCode)
					{
						return false;
					}

					string body = await response.Content.ReadAsStringAsync();
					var data = JObject.Parse(body)["data"];
					return data?["upsaleAllowed"]?.Value<bool?>() ?? false;
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				LoggingService.LogError("BudgetService.GetUpsaleAllowedAsync", ex);
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
				string cookieHeader,
				string csrfToken,
				string sessionId,
				long businessSnapshotId,
				CancellationToken cancellationToken)
		{
			try
			{
				string url =
					$"{GetBusinessSnapshotUrl}" +
					$"?csrfToken={Uri.EscapeDataString(csrfToken)}" +
					$"&sessionId={Uri.EscapeDataString(sessionId)}" +
					$"&id={businessSnapshotId}";


				using (var response = await SendAsync(
					client, HttpMethod.Get, url, cookieHeader, null, cancellationToken))
				{
					if (!response.IsSuccessStatusCode)
					{
						return false;
					}

					string body = await response.Content.ReadAsStringAsync();
					var data = JObject.Parse(body)["data"];
					return data?["settings"]?["arbitrageDisabled"]?.Value<bool?>() ?? false;
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				LoggingService.LogError("BudgetService.GetArbitrageDisabledAsync", ex);
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


		private static async Task<HttpResponseMessage> SendAsync(
			HttpClient client,
			HttpMethod method,
			string url,
			string cookieHeader,
			Func<HttpContent> contentFactory,
			CancellationToken cancellationToken)
		{
			return await HttpRetryPolicy.SendAsync(
				client,
				() =>
				{
					var request = new HttpRequestMessage(method, url);
					if (!string.IsNullOrWhiteSpace(cookieHeader))
					{
						request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
					}

					request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
					request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
					request.Content = contentFactory?.Invoke();
					return request;
				},
				cancellationToken);
		}

		private static async Task<string> GetRequiredStringAsync(
			HttpClient client,
			string url,
			string cookieHeader,
			CancellationToken cancellationToken)
		{
			using (var response = await SendAsync(
				client, HttpMethod.Get, url, cookieHeader, null, cancellationToken))
			{
				HttpRetryPolicy.EnsureSuccess(
					response,
					"Не удалось получить данные рекламной кампании.",
					"BudgetService.GetRequiredStringAsync");
				return await response.Content.ReadAsStringAsync();
			}
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
