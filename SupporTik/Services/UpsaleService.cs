using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SupporTik.Services
{
	public class UpsaleService : IUpsaleService
	{
		private sealed class UpsaleCacheEntry
		{
			public string Value { get; set; }
			public DateTime StoredAt { get; set; }
		}

		private const int MaxCachedUpsales = 1000;
		private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(3);
		private readonly object _cacheLock = new object();
		private readonly Dictionary<string, UpsaleCacheEntry> _cache =
			new Dictionary<string, UpsaleCacheEntry>();
		private string _cacheSession;

		// id виджета с таблицей апсейлов на дашборде — если дашборд поменяется, нужно
		// заново найти id через Network → Payload после клика "Применить" в браузере
		private const string ChartId = "fhwtppiuuowk0";
		private const string Url = "https://datalens.yandex-team.ru/api/run";
		private const string DashboardReferer = "https://datalens.yandex-team.ru/qniendwn7xvwg-apseyl-po-nomeram-rk?tab=OW";
		private static readonly HttpClient HttpClient = new HttpClient(
			new HttpClientHandler { UseCookies = false });

		// Раньше кампании проверялись строго по очереди (одна за другой) — на десятках
		// кампаний это была основная задержка "Проверить апсейлы". Ограничение вместо
		// полного распараллеливания — чтобы не долбить DataLens сотнями запросов разом
		private const int MaxConcurrentRequests = 5;

		public async Task<Dictionary<string, string>> CheckUpsalesAsync(
			string cookieHeader,
			string csrfToken,
			IReadOnlyList<string> campaignIds,
			IProgress<string> progress,
			CancellationToken cancellationToken)
		{
			var results = new ConcurrentDictionary<string, string>();

			using (var throttle = new SemaphoreSlim(MaxConcurrentRequests))
			{
				int completed = 0;

				var tasks = campaignIds.Select(async campaignId =>
				{
					await throttle.WaitAsync(cancellationToken);
					try
					{
						string value = TryGetCachedUpsale(cookieHeader, csrfToken, campaignId);
						if (value == null)
						{
							value = await GetUpsaleAsync(cookieHeader, csrfToken, campaignId, cancellationToken);
							if (!string.IsNullOrEmpty(value) && value != "Ошибка")
							{
								StoreUpsale(cookieHeader, csrfToken, campaignId, value);
							}
						}

						results[campaignId] = value;
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex)
					{
						LoggingService.LogError($"UpsaleService campaign_id={campaignId}", ex);
						results[campaignId] = "Ошибка";
					}
					finally
					{
						int done = Interlocked.Increment(ref completed);
						progress?.Report($"Апсейлы {done}/{campaignIds.Count}...");
						throttle.Release();
					}
				});

				await Task.WhenAll(tasks);
			}

			return new Dictionary<string, string>(results);
		}

		private string TryGetCachedUpsale(
			string cookieHeader,
			string csrfToken,
			string campaignId)
		{
			lock (_cacheLock)
			{
				ResetCacheForNewSession(cookieHeader, csrfToken);
				if (_cache.TryGetValue(campaignId, out UpsaleCacheEntry entry))
				{
					if (DateTime.UtcNow - entry.StoredAt <= CacheLifetime)
					{
						return entry.Value;
					}

					_cache.Remove(campaignId);
				}
			}

			return null;
		}

		private void StoreUpsale(
			string cookieHeader,
			string csrfToken,
			string campaignId,
			string value)
		{
			lock (_cacheLock)
			{
				ResetCacheForNewSession(cookieHeader, csrfToken);
				if (_cache.Count >= MaxCachedUpsales)
				{
					_cache.Clear();
				}

				_cache[campaignId] = new UpsaleCacheEntry
				{
					Value = value,
					StoredAt = DateTime.UtcNow
				};
			}
		}

		private void ResetCacheForNewSession(string cookieHeader, string csrfToken)
		{
			string session = cookieHeader + "\n" + csrfToken;
			if (!string.Equals(_cacheSession, session, StringComparison.Ordinal))
			{
				_cache.Clear();
				_cacheSession = session;
			}
		}

		private static async Task<string> GetUpsaleAsync(
			string cookieHeader,
			string csrfToken,
			string campaignId,
			CancellationToken cancellationToken)
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
			using (var response = await HttpRetryPolicy.SendAsync(
				HttpClient,
				() =>
				{
					var request = new HttpRequestMessage(HttpMethod.Post, Url);
					request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
					request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrfToken);
					request.Headers.Referrer = new Uri(DashboardReferer);
					request.Headers.TryAddWithoutValidation("X-Dash-Info", "dashId=qniendwn7xvwg&dashTabId=OW");
					request.Headers.TryAddWithoutValidation("X-DL-Display-Mode", "basic");
					request.Headers.TryAddWithoutValidation("X-DL-TenantId", "common");
					request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
					request.Content = new StringContent(json, Encoding.UTF8, "application/json");
					return request;
				},
				cancellationToken))
			{
				HttpRetryPolicy.EnsureSuccess(
					response,
					"Не удалось проверить предложение для рекламной кампании.",
					"UpsaleService.GetUpsaleAsync");

				string body = await response.Content.ReadAsStringAsync();
				return ParseUpsaleValue(body);
			}
		}

		private static string ParseUpsaleValue(string json)
		{
			try
			{
				var doc = JObject.Parse(json);
				var cell = doc["data"]["rows"][0]["cells"][1]["value"];
				return cell.Type == JTokenType.Null ? null : cell.ToString();
			}
			catch (Exception)
			{
				return "Нет данных";
			}
		}
	}
}
