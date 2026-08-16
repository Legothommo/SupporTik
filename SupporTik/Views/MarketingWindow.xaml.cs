using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SupporTik.Classes;
using SupporTik.Helpers;
using SupporTik.Services;
using SupporTik.ViewModels;
using System;
using System.Linq;
using System.Threading;
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
		private static readonly TimeSpan AuthorizationNavigationTimeout = TimeSpan.FromMinutes(5);

		// Фиксированная ширина окна — используется и для позиционирования, и для
		// анимации, чтобы не зависеть от Width, который во время показа окна может
		// на мгновение отличаться (пересчёт DPI/монитора и т.п.)
		private const double WindowWidth = 500;

		private readonly MarketingWindowViewModel _viewModel;
		private readonly SemaphoreSlim _authorizationGate = new SemaphoreSlim(1, 1);

		// Сторона, с которой выезжает окно — настраивается в SettingsPage
		private bool OpenFromLeft => Properties.Settings.Default.MarketingMenuFromLeft;

		// Монитор, на котором окно показывается в текущем цикле показа — определяется
		// по позиции курсора (см. RefreshActiveMonitor), а не всегда основной, чтобы на
		// многомониторных системах окно выезжало там, где сейчас пользователь, а не
		// потенциально за пределами того монитора, где реально сидит человек
		private MonitorHelper.MonitorBounds _activeMonitor;

		public MarketingWindow()
		{
			InitializeComponent();

			Width = WindowWidth;

			// Занимаем нужный край экрана по всей высоте рабочей области, изначально
			// за пределами экрана — оттуда стартует анимация выезда
			RefreshActiveMonitor();
			Rect workArea = _activeMonitor.WorkArea;
			Height = workArea.Height - 200;
			Top = workArea.Top;
			Left = OpenFromLeft ? _activeMonitor.Bounds.Left - WindowWidth : _activeMonitor.Bounds.Right;

			var campaignService = new MarketingCampaignService();
			var notificationService = new NotificationServiceAdapter();
			var upsaleService = new UpsaleService();
			var budgetService = new BudgetService();
			var textBuilder = new MarketingOfferTextBuilder(CompositionRoot.Current.MarketingTemplates);
			_viewModel = new MarketingWindowViewModel(
				campaignService,
				notificationService,
				upsaleService,
				budgetService,
				textBuilder,
				EnsureDataLensAuthAsync,
				GetYandexBusinessAuthAsync);
			DataContext = _viewModel;

			// Тикает, пока окно открыто, чтобы "N мин назад" само доезжало без нового
			// похода за авторизацией — иначе текст замирал бы на значении с момента
			// последнего успешного запроса до следующего клика "Поиск"
			_sessionStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
			_sessionStatusTimer.Tick += (s, e) => RefreshSessionStatusText();
			_sessionStatusTimer.Start();

			Loaded += MarketingWindow_Loaded;
		}

		/// <summary>
		/// "Устарела" — эвристика (StaleSessionThreshold), не факт: у нас нет способа узнать
		/// реальный срок жизни куки/токена со стороны Яндекса, только предупредить, что
		/// давно не перепроверялось, если поиск вдруг начнёт падать с ошибкой авторизации.
		/// </summary>
		private void RefreshSessionStatusText()
		{
			if (_yandexAuthCheckedAt == null)
			{
				_viewModel.SessionStatusText = string.Empty;
				return;
			}

			TimeSpan age = DateTime.Now - _yandexAuthCheckedAt.Value;
			bool stale = age > StaleSessionThreshold;

			string text = $"Сессия Business подтверждена {FormatAge(age)}";

			if (_dataLensAuthCheckedAt != null)
			{
				text += $" · Сессия DataLens подтверждена {FormatAge(DateTime.Now - _dataLensAuthCheckedAt.Value)}";
			}

			_viewModel.SessionStatusText = stale ? $"⚠ {text} — возможно, устарела" : text;
		}

		private static string FormatAge(TimeSpan age)
		{
			if (age < TimeSpan.FromMinutes(1))
			{
				return "только что";
			}

			if (age < TimeSpan.FromHours(1))
			{
				return $"{(int)age.TotalMinutes} мин. назад";
			}

			return $"{(int)age.TotalHours} ч. назад";
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
			// Пересчитываем на каждый показ — за время, пока окно было скрыто,
			// пользователь мог переключиться на другой монитор
			RefreshActiveMonitor();

			if (!IsLoaded)
			{
				Show();
				return;
			}

			Left = OpenFromLeft ? _activeMonitor.Bounds.Left - WindowWidth : _activeMonitor.Bounds.Right;

			Show();
			await Dispatcher.Yield(DispatcherPriority.Loaded);
			AnimateSlideIn();
		}

		private void RefreshActiveMonitor()
		{
			_activeMonitor = MonitorHelper.GetMonitorBoundsForPoint(MouseHelper.GetCursorPosition());
		}

		private void AnimateSlideIn()
		{
			Rect workArea = _activeMonitor.WorkArea;
			Rect bounds = _activeMonitor.Bounds;

			// Целевая позиция — окно "прилипает" к нужному краю рабочей области монитора,
			// с отступом 20px
			double targetLeft = OpenFromLeft ? workArea.Left + 20 : workArea.Right - Width - 20;
			double targetTop = workArea.Top + (workArea.Height - Height) / 2; // по центру вертикально

			Top = targetTop;

			// Стартовая позиция — полностью за пределами монитора, с соответствующей стороны
			double startLeft = OpenFromLeft ? bounds.Left - Width : bounds.Right;
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
		private async Task<JObject> WaitForInitialStateAsync(
			string script,
			CancellationToken cancellationToken)
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

				await Task.Delay(InitialStateRetryDelayMs, cancellationToken);
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

		// Когда кэш выше последний раз подтверждался реальным успешным заходом — чисто
		// информационно для пользователя (см. RefreshSessionStatusText): токен формально
		// не имеет известного срока жизни на нашей стороне, порог "устарела" — эвристика.
		private DateTime? _yandexAuthCheckedAt;
		private DateTime? _dataLensAuthCheckedAt;
		private static readonly TimeSpan StaleSessionThreshold = TimeSpan.FromMinutes(30);
		private readonly DispatcherTimer _sessionStatusTimer;

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
		private Task<(string CookieHeader, string CsrfToken)> EnsureDataLensAuthAsync(
			bool forceRefresh,
			CancellationToken cancellationToken)
		{
			return RunAuthorizationExclusiveAsync(
				() => EnsureDataLensAuthCoreAsync(forceRefresh, cancellationToken),
				cancellationToken);
		}

		private void BtnCopySelectedUpsales_Click(object sender, RoutedEventArgs e)
		{
			if (!(sender is FrameworkElement button)) return;
			MarketingTemplateMenu.Open(
				button,
				_viewModel.GetCompatibleTemplatesForSelected(),
				_viewModel.CopySelectedUpsalesWithTemplate);
		}

		private async Task<(string CookieHeader, string CsrfToken)> EnsureDataLensAuthCoreAsync(
			bool forceRefresh,
			CancellationToken cancellationToken)
		{
			if (!forceRefresh && _cachedDataLensAuth.HasValue)
			{
				return _cachedDataLensAuth.Value;
			}

			await webView.EnsureCoreWebView2Async();

			TbLoginStatus.Text = "Проверяем авторизацию в DataLens...";
			TbLoginStatus.Visibility = Visibility.Visible;

			try
			{
				await NavigateForAuthorizationAsync(
					DataLensDashboardUrl,
					"Войдите в аккаунт login@yandex-team.ru в открывшемся окне...",
					cancellationToken);
			}
			finally
			{
				webView.Visibility = Visibility.Collapsed;
				TbLoginStatus.Visibility = Visibility.Collapsed;
			}

			var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync(DataLensCookieUrl);
			string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
			string csrfToken = cookies.FirstOrDefault(c => c.Name == "CSRF-TOKEN")?.Value ?? string.Empty;

			var result = (cookieHeader, csrfToken);

			// Кэшируем только реально успешный результат — та же причина, что и в
			// GetYandexBusinessAuthAsync ниже
			if (!string.IsNullOrEmpty(csrfToken))
			{
				_cachedDataLensAuth = result;
				_dataLensAuthCheckedAt = DateTime.Now;
				RefreshSessionStatusText();
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
		private Task<YandexBusinessAuth> GetYandexBusinessAuthAsync(
			bool forceRefresh,
			CancellationToken cancellationToken)
		{
			return RunAuthorizationExclusiveAsync(
				() => GetYandexBusinessAuthCoreAsync(forceRefresh, cancellationToken),
				cancellationToken);
		}

		private async Task<YandexBusinessAuth> GetYandexBusinessAuthCoreAsync(
			bool forceRefresh,
			CancellationToken cancellationToken)
		{
			if (!forceRefresh && _cachedYandexAuth != null)
			{
				return _cachedYandexAuth;
			}

			await webView.EnsureCoreWebView2Async();

			TbLoginStatus.Text = "Проверяем авторизацию...";
			TbLoginStatus.Visibility = Visibility.Visible;

			try
			{
				await NavigateForAuthorizationAsync(
					LoginCheckUrl,
					"Войдите в аккаунт yndx-login@yandex.ru в открывшемся окне...",
					cancellationToken);
			}
			finally
			{
				webView.Visibility = Visibility.Collapsed;
				TbLoginStatus.Visibility = Visibility.Collapsed;
			}

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

			JObject data = await WaitForInitialStateAsync(script, cancellationToken);

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
				_yandexAuthCheckedAt = DateTime.Now;
				RefreshSessionStatusText();
			}

			return auth;
		}

		private async Task<T> RunAuthorizationExclusiveAsync<T>(
			Func<Task<T>> action,
			CancellationToken cancellationToken)
		{
			await _authorizationGate.WaitAsync(cancellationToken);
			try
			{
				return await action();
			}
			finally
			{
				_authorizationGate.Release();
			}
		}

		private async Task NavigateForAuthorizationAsync(
			string targetUrl,
			string loginMessage,
			CancellationToken cancellationToken)
		{
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			void Handler(object sender, CoreWebView2NavigationCompletedEventArgs args)
			{
				string currentUrl = webView.CoreWebView2.Source ?? string.Empty;
				if (currentUrl.IndexOf("passport.yandex", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					webView.Visibility = Visibility.Visible;
					TbLoginStatus.Text = loginMessage;
					return;
				}

				if (args.IsSuccess)
				{
					completion.TrySetResult(true);
				}
				else
				{
					completion.TrySetException(new InvalidOperationException(
						$"Не удалось открыть страницу авторизации: {args.WebErrorStatus}."));
				}
			}

			webView.CoreWebView2.NavigationCompleted += Handler;
			try
			{
				webView.CoreWebView2.Navigate(targetUrl);

				Task timeout = Task.Delay(AuthorizationNavigationTimeout, cancellationToken);
				Task finished = await Task.WhenAny(completion.Task, timeout);

				if (finished != completion.Task)
				{
					cancellationToken.ThrowIfCancellationRequested();
					throw new TimeoutException("Истекло время ожидания авторизации Яндекса.");
				}

				await completion.Task;
			}
			finally
			{
				webView.CoreWebView2.NavigationCompleted -= Handler;
			}
		}

		#endregion

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			HideAnimated();
		}

		private void BtnRecentUids_Click(object sender, RoutedEventArgs e)
		{
			RecentUidsList.ItemsSource = _viewModel.RecentUids;
			RecentUidsPopup.IsOpen = !RecentUidsPopup.IsOpen;
		}

		private void RecentUidItem_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement element && element.DataContext is string uid)
			{
				_viewModel.Uid = uid;
			}

			RecentUidsPopup.IsOpen = false;
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
			double target = OpenFromLeft ? _activeMonitor.Bounds.Left - Width : _activeMonitor.Bounds.Right;
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
		public async Task ClearAuthorizationAsync()
		{
			await _authorizationGate.WaitAsync();
			try
			{
				await webView.EnsureCoreWebView2Async();

				if (webView.CoreWebView2 == null)
				{
					return;
				}

				// Удаляем cookies и данные сайтов из профиля WebView2.
				await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
					CoreWebView2BrowsingDataKinds.Cookies |
					CoreWebView2BrowsingDataKinds.AllDomStorage);

				_cachedYandexAuth = null;
				_cachedDataLensAuth = null;
			}
			finally
			{
				_authorizationGate.Release();
			}
		}
	}
}
