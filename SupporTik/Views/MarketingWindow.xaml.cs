using Microsoft.Web.WebView2.Core;
using SupporTik.Services;
using SupporTik.ViewModels;
using System;
using System.ComponentModel;
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

		private const string LoginCheckUrl = "https://yandex.ru/business/priority";

		// Фиксированная ширина окна — используется и для позиционирования, и для
		// анимации, чтобы не зависеть от Width, который во время показа окна может
		// на мгновение отличаться (пересчёт DPI/монитора и т.п.)
		private const double WindowWidth = 420;

		private readonly MarketingWindowViewModel _viewModel;

		// Сторона, с которой выезжает окно — настраивается в SettingsPage
		private readonly bool _openFromLeft;

		public MarketingWindow()
		{
			InitializeComponent();

			_openFromLeft = Properties.Settings.Default.MarketingMenuFromLeft;

			Width = WindowWidth;

			// Занимаем нужный край экрана по всей высоте рабочей области, изначально
			// за пределами экрана — оттуда стартует анимация выезда
			var workArea = SystemParameters.WorkArea;
			Height = workArea.Height - 200;
			Top = workArea.Top;
			Left = _openFromLeft ? workArea.Left - WindowWidth : workArea.Right;

			var navigator = new WebViewNavigator(webView);
			var campaignService = new MarketingCampaignService(navigator);
			var notificationService = new NotificationServiceAdapter();
			_viewModel = new MarketingWindowViewModel(campaignService, notificationService);
			DataContext = _viewModel;

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

			// Целевая позиция — окно "прилипает" к нужному краю, с отступом 20px
			double targetLeft = _openFromLeft ? 20 : screenWidth - Width - 20;
			double targetTop = (screenHeight - Height) / 2; // по центру вертикально

			Top = targetTop;

			// Стартовая позиция — полностью за пределами экрана, с соответствующей стороны
			double startLeft = _openFromLeft ? -Width : screenWidth;
			Left = startLeft;

			var animation = new DoubleAnimation
			{
				From = startLeft,
				To = targetLeft,
				Duration = TimeSpan.FromMilliseconds(400),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};

			BeginAnimation(Window.LeftProperty, animation);
		}

		#region Авторизация (WebView2-проводка — View-специфичная логика)

		private async Task InitializeAsync()
		{
			await webView.EnsureCoreWebView2Async();

			await ShowLoginAsync();

			_viewModel.IsSearchUiVisible = true;
		}

		private async Task ShowLoginAsync()
		{
			TbLoginStatus.Text = "Проверяем авторизацию...";
			TbLoginStatus.Visibility = Visibility.Visible;

			var tcs = new TaskCompletionSource<bool>();

			// currentUrl нужно перечитывать на каждом NavigationCompleted, а не один раз
			// до перехода: до первой навигации в этом WebView2 Source ещё пуст, а если
			// сессия истекла, нас редиректнет на passport.yandex уже ПОСЛЕ Navigate —
			// это и есть момент, когда окно логина нужно показать пользователю
			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				string currentUrl = webView.CoreWebView2.Source ?? string.Empty;
				bool isLoginPage = currentUrl.ToLowerInvariant().Contains("passport.yandex");

				if (isLoginPage)
				{
					webView.Visibility = Visibility.Visible;
					TbLoginStatus.Text = "Войдите в аккаунт Яндекс в открывшемся окне...";
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

				double target = _openFromLeft ? -Width : SystemParameters.PrimaryScreenWidth;
				var animation = new DoubleAnimation
				{
					To = target,
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
