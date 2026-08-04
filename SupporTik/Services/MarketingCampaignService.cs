using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SupporTik.Classes;

namespace SupporTik.Services
{
	public class MarketingCampaignService : IMarketingCampaignService
	{
		private const string BaseUrl = "https://yandex.ru/business/priority?from=sprav__external__sprav_header&userUid=";

		// Ссылки на карточках бывают и .../settings, и .../tab-settings — это одна и
		// та же страница, просто разные варианты урла. Поэтому здесь строим свою
		// каноническую ссылку по permalink и не используем item.Href вообще.
		private const string SettingsUrlTemplate = "https://yandex.ru/business/priority/campaign/{0}/settings";
		private const string TabSettingsUrlTemplate = "https://yandex.ru/business/priority/campaign/{0}/tab-settings";

		private readonly IWebViewNavigator _navigator;

		public MarketingCampaignService(IWebViewNavigator navigator)
		{
			_navigator = navigator;
		}

		public async Task<List<MarketingItem>> SearchAsync(string uid, bool searchRoles, IProgress<string> progress)
		{
			var allItems = new List<MarketingItem>();

			string url = BaseUrl + Uri.EscapeDataString(uid);
			var doc = await _navigator.NavigateAndGetDocumentAsync(url);

			// NavigateAndGetDocumentAsync ждёт фиксированную секунду после навигации —
			// для небольшого списка (одна страница) SPA обычно успевает дорисоваться,
			// а для крупного (несколько страниц) может не успеть, и первый снимок
			// окажется пустым. Тогда первая страница молча терялась бы целиком —
			// вместо этого дожидаемся, пока в DOM реально появятся строки таблицы
			doc = await WaitForRenderAsync(doc, d => GetPermalinks(d).Count > 0, maxRetries: 8, delayMs: 2000);
			var previousPermalinks = GetPermalinks(doc);

			ParseData(doc, allItems, append: false);

			// Состояние текущей страницы списка хранится только в DOM (не в URL) —
			// обновление страницы или прямой переход по адресу сбрасывает на первую.
			// Поэтому листаем пейджер кликами прямо в этом же WebView2, не уходя со
			// страницы, и на каждой странице довытаскиваем карточки.
			const int maxPages = 200; // страховка от зацикливания, если разметка пейджера не совпадёт с ожидаемой
			int page = 1;

			while (page < maxPages && await _navigator.ClickNextPageAsync())
			{
				await Task.Delay(500); // даём списку перерисоваться под новую страницу
				var pageDoc = await _navigator.GetCurrentDocumentAsync();

				var currentPermalinks = GetPermalinks(pageDoc);

				// Кнопка была кликабельна, но список не изменился (или стал пустым) —
				// значит, это уже последняя страница, дальше продолжать бессмысленно
				if (currentPermalinks.Count == 0 || currentPermalinks.SequenceEqual(previousPermalinks))
				{
					break;
				}

				ParseData(pageDoc, allItems, append: true);
				previousPermalinks = currentPermalinks;
				page++;
			}

			// Роли ищем и показываем только если отмечен чекбокс — это отдельный проход
			// по страницам настроек каждой кампании, дорогой по времени, поэтому
			// пользователь должен сам решить, нужен ли он в этот раз
			if (searchRoles)
			{
				// Для каждой найденной кампании отдельно заходим на её страницу настроек
				// и определяем роль — WebView2 один, поэтому строго по очереди
				for (int i = 0; i < allItems.Count; i++)
				{
					var item = allItems[i];
					progress?.Report($"Роли {i + 1}/{allItems.Count}...");

					string settingsUrl = BuildSettingsUrl(item.Permalink, true);
					var settingsDoc = await _navigator.NavigateAndGetDocumentAsync(settingsUrl);
					settingsDoc = await WaitForRenderAsync(settingsDoc, HasOwnerNode, maxRetries: 4, delayMs: 1000);

					if (ParseCampaignSettings(settingsDoc, item) == false)
					{
						settingsUrl = BuildSettingsUrl(item.Permalink, false);
						settingsDoc = await _navigator.NavigateAndGetDocumentAsync(settingsUrl);
						settingsDoc = await WaitForRenderAsync(settingsDoc, HasOwnerNode, maxRetries: 4, delayMs: 1000);
						ParseCampaignSettings(settingsDoc, item);
					}
				}
			}

			return allItems;
		}

		/// <summary>
		/// Пересъёмка HTML, пока isRendered не увидит нужные данные (до maxRetries раз с
		/// паузой delayMs) — вместо того чтобы один раз проверить страницу по фиксированной
		/// задержке и молча считать её пустой, если SPA ещё не успела дорисоваться.
		/// </summary>
		private async Task<HtmlDocument> WaitForRenderAsync(HtmlDocument doc, Func<HtmlDocument, bool> isRendered, int maxRetries, int delayMs)
		{
			for (int retry = 0; retry < maxRetries && !isRendered(doc); retry++)
			{
				await Task.Delay(delayMs);
				doc = await _navigator.GetCurrentDocumentAsync();
			}

			return doc;
		}

		private static bool HasOwnerNode(HtmlDocument doc) => doc.DocumentNode.SelectSingleNode("//div[contains(text(), 'Владелец')]") != null;

		/// <summary>Список пермалинков на текущей странице — используется, чтобы понять, изменился ли список после клика по пейджеру.</summary>
		private static List<string> GetPermalinks(HtmlDocument doc)
		{
			var nodes = doc.DocumentNode.SelectNodes("//tr[@class='campaign-list__list-row']//span[@data-name='campaign-id']");
			return nodes?.Select(n => n.InnerText?.Trim()).ToList() ?? new List<string>();
		}

		private static void ParseData(HtmlDocument doc, List<MarketingItem> items, bool append)
		{
			if (!append)
			{
				items.Clear();
			}

			var nodes = doc.DocumentNode.SelectNodes("//tr[@class='campaign-list__list-row']");

			if (nodes == null)
			{
				Debug.WriteLine("Элементы не найдены — возможно, страница ещё не прогрузилась или разметка изменилась.");
				return;
			}

			// Одно поле на всю страницу — логин пользователя, для которого сейчас
			// показан список кампаний; проставляем его каждому элементу
			string login = doc.DocumentNode
				.SelectSingleNode("//input[@name='user-suggest']")
				?.GetAttributeValue("value", null);

			foreach (var node in nodes)
			{
				var remainLabels = node
					.SelectSingleNode(".//td[@class='campaign-list__list-column campaign-list__list-column_no-wrap']")
					?.SelectNodes(".//div[contains(@class, 'label')]");
				string remain = remainLabels != null
					? string.Join(Environment.NewLine, remainLabels.Select(n => n.InnerText.Trim()))
					: string.Empty;

				string href = node
					.SelectSingleNode(".//a[contains(@class, 'campaign-list__campaign-card')]")
					?.GetAttributeValue("href", null);

				items.Add(new MarketingItem
				{
					Permalink = node.SelectSingleNode(".//span[@data-name='campaign-id']")?.InnerText.Replace("№", ""),
					Status = node.SelectSingleNode(".//span[@data-name='campaign-status']")?.InnerText,
					Remain = remain.Replace("&nbsp;", " "),
					Href = ResolveUrl(href),
					Login = login
				});
			}
		}

		private static string ResolveUrl(string href)
		{
			if (string.IsNullOrEmpty(href))
			{
				return null;
			}

			return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
				? href
				: "https://yandex.ru" + href;
		}

		private static string BuildSettingsUrl(string permalink, bool tab)
		{
			if (tab)
				return string.Format(TabSettingsUrlTemplate, permalink);
			else
				return string.Format(SettingsUrlTemplate, permalink);
		}

		/// <summary>
		/// Определяет роль пользователя в кампании: ищем элемент с текстом "Владелец"
		/// и проверяем, встречается ли в нём же логин, полученный ранее со страницы
		/// списка кампаний. Если да — пользователь владелец, иначе — наблюдатель.
		/// </summary>
		private static bool ParseCampaignSettings(HtmlDocument doc, MarketingItem item)
		{
			var ownerNodes = doc.DocumentNode.SelectSingleNode("//div[contains(text(), 'Владелец')]");

			if (ownerNodes == null) return false;
			Console.WriteLine(ownerNodes.ParentNode.InnerText + " | " + item.Login);

			bool isOwner = ownerNodes != null
				&& !string.IsNullOrEmpty(item.Login)
				&& ownerNodes.ParentNode.InnerText.Contains(item.Login);

			item.Role = isOwner ? "Владелец" : "Наблюдатель";
			return true;
		}
	}
}
