using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SupporTik.Classes;
using SupporTik.Services;
using SupporTik.ViewModels;
using System;
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
		private const string LoginCheckUrl = "https://yandex.ru/business/priority";

		// DataLens — отдельный сайт (внутренний Yandex Team) со своей сессией/логином,
		// используется только для проверки апсейлов, не для основного списка кампаний
		private const string DataLensDashboardUrl = "https://datalens.yandex-team.ru/qniendwn7xvwg-apseyl-po-nomeram-rk?tab=OW";
		private const string DataLensCookieUrl = "https://datalens.yandex-team.ru";

		private const string YandexCookieUrl = "https://yandex.ru";

		// Фиксированная ширина окна — используется и для позиционирования, и для
		// анимации, чтобы не зависеть от Width, который во время показа окна может
		// на мгновение отличаться (пересчёт DPI/монитора и т.п.)
		private const double WindowWidth = 500;

		private readonly MarketingWindowViewModel _viewModel;

		// Сторона, с которой выезжает окно — настраивается в SettingsPage
		private bool OpenFromLeft => Properties.Settings.Default.MarketingMenuFromLeft;

		public MarketingWindow()
		{
			InitializeComponent();

			Width = WindowWidth;

			// Занимаем нужный край экрана по всей высоте рабочей области, изначально
			// за пределами экрана — оттуда стартует анимация выезда
			var workArea = SystemParameters.WorkArea;
			Height = workArea.Height - 200;
			Top = workArea.Top;
			Left = OpenFromLeft ? workArea.Left - WindowWidth : workArea.Right;

			var campaignService = new MarketingCampaignService();
			var notificationService = new NotificationServiceAdapter();
			var upsaleService = new UpsaleService();
			var budgetService = new BudgetService();
			_viewModel = new MarketingWindowViewModel(campaignService, notificationService, upsaleService, budgetService, EnsureDataLensAuthAsync, GetYandexBusinessAuthAsync);
			DataContext = _viewModel;

			Loaded += MarketingWindow_Loaded;
		}

		private async void MarketingWindow_Loaded(object sender, RoutedEventArgs e)
		{
			// Срабатывает один раз за всё время жизни окна: Hide()/повторный Show() не
			// пересоздают визуальное дерево, поэтому здесь и первая анимация выезда, и
			// проверка авторизации — после неё логин больше не перепроверяется, пока
			// приложение не перезапущено (см. ShowAnimated ниже для повторных открытий)
			await Dispatcher.Yield(DispatcherPriority.Loaded);

			AnimateSlideIn();
			await InitializeAsync();
		}

		/// <summary>
		/// Показывает уже существующее окно (в том числе ранее скрытое через HideAnimated)
		/// с анимацией выезда. Для самого первого показа достаточно Show() — Loaded сам
		/// запустит анимацию и проверку авторизации; при повторных открытиях Loaded больше
		/// не срабатывает, поэтому готовим стартовую позицию и анимируем вручную.
		/// </summary>
		public async void ShowAnimated()
		{
			if (!IsLoaded)
			{
				Show();
				return;
			}

			var workArea = SystemParameters.WorkArea;
			Left = OpenFromLeft ? workArea.Left - WindowWidth : workArea.Right;

			Show();
			await Dispatcher.Yield(DispatcherPriority.Loaded);
			AnimateSlideIn();
		}

		private void AnimateSlideIn()
		{
			var screenWidth = SystemParameters.PrimaryScreenWidth;
			var screenHeight = SystemParameters.PrimaryScreenHeight;

			// Целевая позиция — окно "прилипает" к нужному краю, с отступом 20px
			double targetLeft = OpenFromLeft ? 20 : screenWidth - Width - 20;
			double targetTop = (screenHeight - Height) / 2; // по центру вертикально

			Top = targetTop;

			// Стартовая позиция — полностью за пределами экрана, с соответствующей стороны
			double startLeft = OpenFromLeft ? -Width : screenWidth;
			Left = startLeft;

			var animation = new DoubleAnimation
			{
				From = startLeft,
				To = targetLeft,
				Duration = TimeSpan.FromMilliseconds(400),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};

			// FillBehavior по умолчанию — HoldEnd, то есть без явного снятия анимация
			// продолжает "держать" Left даже после завершения. Из-за этого нативный
			// ресайз через WindowChrome конфликтует с анимационным клоком — при попытке
			// потянуть один край съезжает противоположный. Снимаем анимацию по
			// завершении и фиксируем позицию обычным присвоением.
			animation.Completed += (s, args) =>
			{
				BeginAnimation(Window.LeftProperty, null);
				Left = targetLeft;
			};

			BeginAnimation(Window.LeftProperty, animation);
		}

		private const int InitialStateMaxAttempts = 10;
		private const int InitialStateRetryDelayMs = 200;

		/// <summary>
		/// Опрашивает window.__INITIAL__ короткими интервалами вместо одной попытки сразу
		/// после NavigationCompleted — SPA дозаполняет его клиентским JS уже после события
		/// загрузки документа, поэтому "не готово" сразу после навигации — не ошибка, а
		/// нормальная гонка. script должен сам возвращать пустые строки, а не бросать
		/// исключение, если __INITIAL__ ещё не готов (см. использование ниже).
		/// </summary>
		private async Task<JObject> WaitForInitialStateAsync(string script)
		{
			for (int attempt = 0; attempt < InitialStateMaxAttempts; attempt++)
			{
				string rawResult = await webView.CoreWebView2.ExecuteScriptAsync(script);

				// ExecuteScriptAsync возвращает JSON-представление строкового результата, то
				// есть саму нашу JSON-строку ещё раз обёрнутую в кавычки/экранирование
				string json = JsonConvert.DeserializeObject<string>(rawResult) ?? string.Empty;

				if (!string.IsNullOrEmpty(json))
				{
					var data = JObject.Parse(json);

					if (!string.IsNullOrEmpty(data.Value<string>("csrfToken")))
					{
						return data;
					}
				}

				await Task.Delay(InitialStateRetryDelayMs);
			}

			return new JObject();
		}

		#region Авторизация (WebView2-проводка — View-специфичная логика)

		// Без кэша каждый поиск/проверка апсейлов заново гоняли WebView2 через полную
		// загрузку страницы, даже если сессия только что была подтверждена — секунды на
		// пустом месте. forceRefresh (см. ниже) даёт ViewModel возможность попросить
		// перезайти заново, если запрос с кэшированным токеном всё же не сработал.
		private YandexBusinessAuth _cachedYandexAuth;
		private (string CookieHeader, string CsrfToken)? _cachedDataLensAuth;

		private async Task InitializeAsync()
		{
			_viewModel.IsSearchUiVisible = true;

			await webView.EnsureCoreWebView2Async();
		}

		/// <summary>
		/// Авторизация в DataLens для проверки апсейлов — тот же WebView2, что и для
		/// списка кампаний (куки разных доменов друг другу не мешают), но своя сессия.
		/// Логика идентична ShowLoginAsync: если уже залогинен — переход сразу попадёт
		/// на дашборд и окно логина не покажется вовсе; если нет — ждём редиректа
		/// на страницу логина и затем перехода обратно после того, как пользователь войдёт.
		/// </summary>
		private async Task<(string CookieHeader, string CsrfToken)> EnsureDataLensAuthAsync(bool forceRefresh)
		{
			if (!forceRefresh && _cachedDataLensAuth.HasValue)
			{
				return _cachedDataLensAuth.Value;
			}

			await webView.EnsureCoreWebView2Async();

			TbLoginStatus.Text = "Проверяем авторизацию в DataLens...";
			TbLoginStatus.Visibility = Visibility.Visible;

			var tcs = new TaskCompletionSource<bool>();

			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				string currentUrl = webView.CoreWebView2.Source ?? string.Empty;
				bool isLoginPage = currentUrl.ToLowerInvariant().Contains("passport.yandex");

				if (isLoginPage)
				{
					webView.Visibility = Visibility.Visible;
					TbLoginStatus.Text = "Войдите в аккаунт login@yandex-team.ru в открывшемся окне...";
					return; // ждём следующей навигации — после того, как пользователь войдёт
				}

				if (args.IsSuccess)
				{
					webView.CoreWebView2.NavigationCompleted -= Handler;
					tcs.TrySetResult(true);
				}
			}

			webView.CoreWebView2.NavigationCompleted += Handler;
			webView.CoreWebView2.Navigate(DataLensDashboardUrl);

			await tcs.Task;

			webView.Visibility = Visibility.Collapsed;
			TbLoginStatus.Visibility = Visibility.Collapsed;

			var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync(DataLensCookieUrl);
			string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
			string csrfToken = cookies.FirstOrDefault(c => c.Name == "CSRF-TOKEN")?.Value ?? string.Empty;

			var result = (cookieHeader, csrfToken);

			// Кэшируем только реально успешный результат — та же причина, что и в
			// GetYandexBusinessAuthAsync ниже
			if (!string.IsNullOrEmpty(csrfToken))
			{
				_cachedDataLensAuth = result;
			}

			return result;
		}

		/// <summary>
		/// csrfToken/sessionId/managerUid НЕ куки — это поля из window.__INITIAL__.state.config,
		/// зашитого в HTML страницы (managerUid — это uid самого залогиненного менеджера,
		/// config.authorization.uid). Поэтому сначала (пере)переходим на LoginCheckUrl тем
		/// же приёмом, что и в EnsureDataLensAuthAsync (с ожиданием логина, если сессия
		/// истекла — WebView2 общий с DataLens и мог успеть уйти на другой домен), а затем
		/// достаём значения через ExecuteScriptAsync из уже отрисованной страницы.
		/// </summary>
		private async Task<YandexBusinessAuth> GetYandexBusinessAuthAsync(bool forceRefresh)
		{
			if (!forceRefresh && _cachedYandexAuth != null)
			{
				return _cachedYandexAuth;
			}

			await webView.EnsureCoreWebView2Async();

			TbLoginStatus.Text = "Проверяем авторизацию...";
			TbLoginStatus.Visibility = Visibility.Visible;

			var tcs = new TaskCompletionSource<bool>();

			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				string currentUrl = webView.CoreWebView2.Source ?? string.Empty;
				bool isLoginPage = currentUrl.ToLowerInvariant().Contains("passport.yandex");

				if (isLoginPage)
				{
					webView.Visibility = Visibility.Visible;
					TbLoginStatus.Text = "Войдите в аккаунт yndx-login@yandex.ru в открывшемся окне...";
					return; // ждём следующей навигации — после того, как пользователь войдёт
				}

				if (args.IsSuccess)
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

			var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync(YandexCookieUrl);
			string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

			// NavigationCompleted срабатывает по загрузке HTML-документа, а сама SPA
			// дозаполняет window.__INITIAL__ клиентским JS уже ПОСЛЕ этого события — сразу
			// после навигации скрипт иногда успевает выполниться раньше, чем состояние
			// готово (первый поиск после входа падал с пустым токеном, повторный — уже
			// успевал). Поэтому не одна попытка, а короткий опрос; сам скрипт защищён от
			// исключений на "ещё не готово" через && вместо прямого обращения к полям
			const string script =
				"JSON.stringify({" +
				"csrfToken: (window.__INITIAL__ && window.__INITIAL__.state && window.__INITIAL__.state.config) ? window.__INITIAL__.state.config.csrfToken : ''," +
				"sessionId: (window.__INITIAL__ && window.__INITIAL__.state && window.__INITIAL__.state.config && window.__INITIAL__.state.config.counters) ? window.__INITIAL__.state.config.counters.analytics.sessionId : ''," +
				"managerUid: (window.__INITIAL__ && window.__INITIAL__.state && window.__INITIAL__.state.config && window.__INITIAL__.state.config.authorization) ? window.__INITIAL__.state.config.authorization.uid : ''" +
				"})";

			JObject data = await WaitForInitialStateAsync(script);

			string csrfToken = data.Value<string>("csrfToken") ?? string.Empty;
			string sessionId = data.Value<string>("sessionId") ?? string.Empty;
			string managerUid = data.Value<string>("managerUid") ?? string.Empty;

			var auth = new YandexBusinessAuth(cookieHeader, csrfToken, sessionId, managerUid);

			// Кэшируем только реально успешный результат — иначе одна неудачная попытка
			// (например, страница не успела отрисовать window.__INITIAL__) навсегда
			// застревала бы в кэше пустым токеном, и все следующие поиски получали бы
			// именно его, даже если пользователь успешно перезашёл
			if (!string.IsNullOrEmpty(csrfToken))
			{
				_cachedYandexAuth = auth;
			}

			return auth;
		}

		#endregion

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			HideAnimated();
		}

		private void BtnStatusFilter_Click(object sender, RoutedEventArgs e)
		{
			StatusFilterPopup.IsOpen = !StatusFilterPopup.IsOpen;
		}

		private void BtnRoleFilter_Click(object sender, RoutedEventArgs e)
		{
			RoleFilterPopup.IsOpen = !RoleFilterPopup.IsOpen;
		}

		private void BtnUpsaleFilter_Click(object sender, RoutedEventArgs e)
		{
			UpsaleFilterPopup.IsOpen = !UpsaleFilterPopup.IsOpen;
		}

		/// <summary>
		/// Прячет окно вместо реального закрытия — WebView2 (и уже пройденный логин)
		/// остаётся жить, чтобы при следующем открытии не проверять авторизацию заново.
		/// Реально окно закрывается только когда приложение целиком завершает работу
		/// (WPF сам вызывает Close() на всех окнах при Application.Shutdown()).
		/// </summary>
		private void HideAnimated()
		{
			double target = OpenFromLeft ? -Width : SystemParameters.PrimaryScreenWidth;
			var animation = new DoubleAnimation
			{
				To = target,
				Duration = TimeSpan.FromMilliseconds(300),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
			};
			animation.Completed += (s, args) =>
			{
				Hide();
				BeginAnimation(Window.LeftProperty, null);
				Left = target;
			};

			BeginAnimation(Window.LeftProperty, animation);
		}
	}
}
