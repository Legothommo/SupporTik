using Hardcodet.Wpf.TaskbarNotification;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using SupporTik.Classes;
using SupporTik.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SupporTik.Views
{
	/// <summary>
	/// Окно парсинга рекламных кампаний Яндекс.Бизнес. Выезжает справа экрана.
	/// Если пользователь ещё не авторизован в Яндексе — сначала показывает окно
	/// входа (WebView2), иначе сразу переходит к поиску по UID.
	/// </summary>
	public partial class MarketingWindow : Window
	{
		private bool _isClosingAnimated = false;

		private const string BaseUrl = "https://yandex.ru/business/priority?from=sprav__external__sprav_header&userUid=";
		private const string LoginCheckUrl = "https://yandex.ru/business/";

		// Полный список последнего поиска — фильтр по статусу просто перерисовывает
		// карточки из этого списка, заново парсить страницу не нужно
		private readonly List<MarketingItem> _allItems = new List<MarketingItem>();

		// Фиксированная ширина окна — используется и для позиционирования, и для
		// анимации, чтобы не зависеть от Width, который во время показа окна может
		// на мгновение отличаться (пересчёт DPI/монитора и т.п.)
		private const double WindowWidth = 420;

		public MarketingWindow()
		{
			InitializeComponent();

			Width = WindowWidth;

			// Занимаем правый край экрана по всей высоте рабочей области, изначально
			// за пределами экрана — оттуда стартует анимация выезда
			var workArea = SystemParameters.WorkArea;
			Height = workArea.Height;
			Top = workArea.Top;
			Left = workArea.Right;

			Loaded += MarketingWindow_Loaded;
		}

		private async void MarketingWindow_Loaded(object sender, RoutedEventArgs e)
		{
			// Даём WPF закончить пересчёт под фактический монитор/DPI после показа —
			// иначе анимация стартует от значения Left, которое система уже успела
			// подправить, и получается прыжок/дёрганье вместо плавного выезда
			await Dispatcher.Yield(DispatcherPriority.Loaded);

			AnimateSlideIn();
			await InitializeAsync();
		}

		private void AnimateSlideIn()
		{
			var screenWidth = SystemParameters.PrimaryScreenWidth;
			var screenHeight = SystemParameters.PrimaryScreenHeight;

			// Целевая позиция (например, окно "прилипает" к правому краю)
			double targetLeft = screenWidth - Width - 20; // 20px отступ от края
			double targetTop = (screenHeight - Height) / 2; // по центру вертикально

			Top = targetTop;

			// Стартовая позиция — за пределами экрана справа
			Left = screenWidth;

			var animation = new DoubleAnimation
			{
				From = screenWidth,
				To = targetLeft,
				Duration = TimeSpan.FromMilliseconds(400),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};

			BeginAnimation(Window.LeftProperty, animation);
		}

		#region Авторизация

		private async Task InitializeAsync()
		{
			await webView.EnsureCoreWebView2Async();

			if (!Properties.Settings.Default.IsLogged)
			{
				await ShowLoginAsync();

				Properties.Settings.Default.IsLogged = true;
				Properties.Settings.Default.Save();
			}

			sp_search.Visibility = Visibility.Visible;
			sp_filter.Visibility = Visibility.Visible;
			sp_actions.Visibility = Visibility.Visible;
		}

		private async Task ShowLoginAsync()
		{
			TbLoginStatus.Visibility = Visibility.Visible;
			webView.Visibility = Visibility.Visible;

			var tcs = new TaskCompletionSource<bool>();

			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				string currentUrl = webView.CoreWebView2.Source ?? string.Empty;
				bool isLoginPage = currentUrl.ToLowerInvariant().Contains("passport.yandex");

				if (args.IsSuccess && !isLoginPage)
				{
					webView.CoreWebView2.NavigationCompleted -= Handler;
					tcs.TrySetResult(true);
				}
			}

			webView.CoreWebView2.NavigationCompleted += Handler;
			webView.CoreWebView2.Navigate(LoginCheckUrl);

			await tcs.Task;

			webView.Visibility = Visibility.Collapsed;
			TbLoginStatus.Visibility = Visibility.Collapsed;
		}

		#endregion

		#region Поиск и парсинг

		private async void SearchButton_Click(object sender, RoutedEventArgs e)
		{
			var uid = UidTextBox.Text.Trim();

			if (string.IsNullOrEmpty(uid))
			{
				App._notifyIcon?.ShowBalloonTip(
					"Предупреждение",
					"Введите UID пользователя.",
					BalloonIcon.Warning);
				return;
			}

			SearchButton.IsEnabled = false;
			string originalContent = SearchButton.Content?.ToString();

			try
			{
				await NavigateAndParseAsync(uid);
			}
			catch (Exception ex)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Ошибка",
					ex.Message,
					BalloonIcon.Warning);
			}
			finally
			{
				SearchButton.Content = originalContent;
				SearchButton.IsEnabled = true;
			}
		}

		private void BtnCopySelected_Click(object sender, RoutedEventArgs e)
		{
			var permalinks = sp_results.Children
				.OfType<MarketingItemPanel>()
				.Where(p => p.IsSelected)
				.Select(p => p.Item.Permalink)
				.Where(p => !string.IsNullOrEmpty(p))
				.ToList();

			if (permalinks.Count == 0)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Предупреждение",
					"Отметьте хотя бы одну карточку.",
					BalloonIcon.Warning);
				return;
			}

			Clipboard.SetText(string.Join(", ", permalinks));

			App._notifyIcon?.ShowBalloonTip(
				"Скопировано",
				$"Пермалинков в буфере: {permalinks.Count}",
				BalloonIcon.None);
		}

		private async Task NavigateAndParseAsync(string uid)
		{
			string url = BaseUrl + Uri.EscapeDataString(uid);
			var doc = await NavigateAndGetDocumentAsync(url);
			ParseData(doc);

			// Состояние текущей страницы списка хранится только в DOM (не в URL) —
			// обновление страницы или прямой переход по адресу сбрасывает на первую.
			// Поэтому листаем пейджер кликами прямо в этом же WebView2, не уходя со
			// страницы, и на каждой странице довытаскиваем карточки.
			const int maxPages = 200; // страховка от зацикливания, если разметка пейджера не совпадёт с ожидаемой
			int page = 1;
			var previousPermalinks = GetPermalinks(doc);

			while (page < maxPages && await ClickNextPageAsync())
			{
				await Task.Delay(1000); // даём списку перерисоваться под новую страницу
				var pageDoc = await GetCurrentDocumentAsync();

				var currentPermalinks = GetPermalinks(pageDoc);

				// Кнопка была кликабельна, но список не изменился (или стал пустым) —
				// значит, это уже последняя страница, дальше продолжать бессмысленно
				if (currentPermalinks.Count == 0 || currentPermalinks.SequenceEqual(previousPermalinks))
				{
					break;
				}

				ParseData(pageDoc, append: true);
				previousPermalinks = currentPermalinks;
				page++;
			}

			// Роли ищем и показываем только если отмечен чекбокс — это отдельный проход
			// по страницам настроек каждой кампании, дорогой по времени, поэтому
			// пользователь должен сам решить, нужен ли он в этот раз
			if (ChkSearchRoles.IsChecked == true)
			{
				// Для каждой найденной кампании отдельно заходим на её страницу настроек
				// и определяем роль — WebView2 один, поэтому строго по очереди
				for (int i = 0; i < _allItems.Count; i++)
				{
					var item = _allItems[i];
					SearchButton.Content = $"Роли {i + 1}/{_allItems.Count}...";

					string settingsUrl = BuildSettingsUrl(item.Permalink, true);
					var settingsDoc = await NavigateAndGetDocumentAsync(settingsUrl);
					if (ParseCampaignSettings(settingsDoc, item) == false)
					{
						settingsUrl = BuildSettingsUrl(item.Permalink, false);
						settingsDoc = await NavigateAndGetDocumentAsync(settingsUrl);
						ParseCampaignSettings(settingsDoc, item);
					}
				}
			}

			ApplyFilter();
		}

		/// <summary>
		/// Переходит по адресу в общем WebView2 (том же, где живёт авторизация) и
		/// возвращает распарсенный HTML. WebView2 у нас один на окно, поэтому все
		/// переходы — включая массовую обработку нескольких кампаний — идут строго
		/// по очереди, никогда не параллельно.
		/// </summary>
		private async Task<HtmlDocument> NavigateAndGetDocumentAsync(string url)
		{
			var tcs = new TaskCompletionSource<bool>();

			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				webView.CoreWebView2.NavigationCompleted -= Handler;
				tcs.TrySetResult(args.IsSuccess);
			}

			webView.CoreWebView2.NavigationCompleted += Handler;
			webView.CoreWebView2.Navigate(url);

			bool success = await tcs.Task;
			if (!success)
			{
				throw new Exception("Не удалось загрузить страницу.");
			}

			// На SPA данные могут дорисовываться уже после NavigationCompleted
			await Task.Delay(1000);

			return await GetCurrentDocumentAsync();
		}

		/// <summary>
		/// Снимает HTML текущей уже загруженной страницы без перехода куда-либо —
		/// нужно после клика по пейджеру, когда список меняется внутри той же SPA,
		/// а не навигацией на новый адрес.
		/// </summary>
		private async Task<HtmlDocument> GetCurrentDocumentAsync()
		{
			string html = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
			html = JsonConvert.DeserializeObject<string>(html);

			var doc = new HtmlDocument();
			doc.LoadHtml(html);
			return doc;
		}

		/// <summary>
		/// Кликает по кнопке следующей страницы. Если кнопки нет или она задизейблена
		/// (обычный признак последней страницы) — сразу возвращает false, не кликая.
		/// </summary>
		private async Task<bool> ClickNextPageAsync()
		{
			// Возвращаем настоящий JS boolean, а не строку 'true'/'false' — ExecuteScriptAsync
			// сериализует результат в JSON, и для строки это была бы "true" (с кавычками
			// внутри самого C#-результата), из-за чего result == "true" никогда не совпадёт
			const string script = @"
					(function() {
						var item = document.querySelector('button[name=""page-next""]');
						if (!item) return false;

						var isDisabled = item.disabled
							|| item.hasAttribute('disabled')
							|| item.getAttribute('aria-disabled') === 'true';
						if (isDisabled) return false;

						['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function(eventType) {
							var event = new MouseEvent(eventType, { bubbles: true, cancelable: true, view: window });
							item.dispatchEvent(event);
						});

						return true;
					})()
				";

			string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
			return result == "true";
		}

		/// <summary>Список пермалинков на текущей странице — используется, чтобы понять, изменился ли список после клика по пейджеру.</summary>
		private static List<string> GetPermalinks(HtmlDocument doc)
		{
			var nodes = doc.DocumentNode.SelectNodes("//tr[@class='campaign-list__list-row']//span[@data-name='campaign-id']");
			return nodes?.Select(n => n.InnerText?.Trim()).ToList() ?? new List<string>();
		}

		private void ParseData(HtmlDocument doc, bool append = false)
		{
			if (!append)
			{
				_allItems.Clear();
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

				_allItems.Add(new MarketingItem
				{
					Permalink = node.SelectSingleNode(".//span[@data-name='campaign-id']")?.InnerText.Replace("№", ""),
					Status = node.SelectSingleNode(".//span[@data-name='campaign-status']")?.InnerText,
					Remain = remain.Replace("&nbsp;", " "),
					Href = ResolveUrl(href),
					Login = login
				});
			}
		}

		private void Filter_Changed(object sender, RoutedEventArgs e)
		{
			ApplyFilter();
		}

		private void ApplyFilter()
		{
			// IsChecked="True" в XAML вызывает Checked ещё во время разбора разметки,
			// до того как InitializeComponent() успевает проинициализировать элементы,
			// объявленные ниже в дереве (в частности sp_results) — в этот момент просто
			// ничего не делаем, актуальная отрисовка всё равно случится после парсинга
			if (sp_results == null)
			{
				return;
			}

			sp_results.Children.Clear();

			foreach (var item in _allItems)
			{
				if (IsStatusVisible(item.Status))
				{
					sp_results.Children.Add(new MarketingItemPanel(item));
				}
			}
		}

		private bool IsStatusVisible(string status)
		{
			switch (status)
			{
				case "Ожидает оплаты": return ChkFilterWaiting.IsChecked == true;
				case "Активна": return ChkFilterActive.IsChecked == true;
				case "Завершена": return ChkFilterFinished.IsChecked == true;
				default: return true; // неизвестный статус — показываем, чтобы не терять данные
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

		// Ссылки на карточках бывают и .../settings, и .../tab-settings — это одна и
		// та же страница, просто разные варианты урла. Поэтому здесь строим свою
		// каноническую ссылку по permalink и не используем item.Href вообще.
		private const string SettingsUrlTemplate = "https://yandex.ru/business/priority/campaign/{0}/settings";
		private const string TabSettingsUrlTemplate = "https://yandex.ru/business/priority/campaign/{0}/tab-settings";

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
		private bool ParseCampaignSettings(HtmlDocument doc, MarketingItem item)
		{
			var ownerNodes = doc.DocumentNode.SelectSingleNode("//div[contains(text(), 'Владелец')]");

			if (ownerNodes == null) return false;

			bool isOwner = ownerNodes != null
				&& !string.IsNullOrEmpty(item.Login)
				&& ownerNodes.ParentNode.InnerText.Contains(item.Login);

			item.Role = isOwner ? "Владелец" : "Наблюдатель";
			return true;
		}

		#endregion

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}
		protected override void OnClosing(CancelEventArgs e)
		{
			if (!_isClosingAnimated)
			{
				e.Cancel = true;
				_isClosingAnimated = true;

				var screenWidth = SystemParameters.PrimaryScreenWidth;
				var animation = new DoubleAnimation
				{
					To = screenWidth,
					Duration = TimeSpan.FromMilliseconds(300),
					EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
				};
				animation.Completed += (s, args) => Close();

				BeginAnimation(Window.LeftProperty, animation);
			}
			else
			{
				base.OnClosing(e);
			}
		}
	}
}
