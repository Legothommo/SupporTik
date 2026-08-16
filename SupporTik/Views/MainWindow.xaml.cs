using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SupporTik.Pages;
using SupporTik.Services;
using SupporTik.ViewModels;

namespace SupporTik
{
	/// <summary>
	/// Логика взаимодействия для MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public static MainWindow Instance { get; private set; }

		/// <summary>
		/// true — окну разрешено реально закрыться (выставляется перед настоящим выходом
		/// из приложения, см. Close_Click/Styles.ContextMenu.ExitApplication). Иначе ЛЮБАЯ
		/// попытка закрыть окно — не только кастомная кнопка (Close_Click), но и панель
		/// задач, Alt+F4 — перехватывается в OnClosing и сворачивает окно в трей вместо
		/// реального закрытия. Без этого закрытие через панель задач оставляло
		/// MainWindow.Instance висеть ссылкой на уже Closed окно: приложение не завершалось
		/// целиком (другие скрытые окна вроде MarketingWindow всё ещё "открыты" для
		/// ShutdownMode=OnLastWindowClose), трей-иконка оставалась, а "Открыть" в её меню
		/// падало с исключением при попытке Show()/Activate() на закрытом окне.
		/// </summary>
		public static bool IsExiting { get; set; }

		private readonly MainWindowViewModel _viewModel;

		// "Идеальный" размер на большом экране — на маленьких (см. ApplySizeForScreen)
		// пропорционально уменьшается, но не ниже MinWidth/MinHeight из XAML
		private const double PreferredWidth = 1100;
		private const double PreferredHeight = 800;

		public MainWindow()
		{
			InitializeComponent();
			Instance = this;

			ApplySizeForScreen();

			_viewModel = new MainWindowViewModel(new ThemeService());
			_viewModel.PropertyChanged += ViewModel_PropertyChanged;
			DataContext = _viewModel;

			MainFrame.Navigate(new BindsPage());
		}

		/// <summary>
		/// XAML задаёт фиксированные Height/Width — на экранах меньше "идеального" размера
		/// (например, ноутбук 1366×768) окно рисковало не влезать или перекрывать панель
		/// задач. Берём долю рабочей области экрана, но не больше PreferredWidth/Height и
		/// не меньше MinWidth/MinHeight (уже заданы в XAML) — так на больших мониторах
		/// размер остаётся прежним, а на маленьких окно уменьшается само.
		/// </summary>
		private void ApplySizeForScreen()
		{
			var workArea = SystemParameters.WorkArea;

			Width = Math.Max(MinWidth, Math.Min(PreferredWidth, workArea.Width * 0.85));
			Height = Math.Max(MinHeight, Math.Min(PreferredHeight, workArea.Height * 0.85));
		}

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MainWindowViewModel.IsMenuOpen))
			{
				AnimateMenu(_viewModel.IsMenuOpen);
			}
		}

		private void AnimateMenu(bool isOpen)
		{
			var sb = (Storyboard)FindResource(isOpen ? "OpenMenu" : "CloseMenu");
			Overlay.IsHitTestVisible = isOpen; // Включаем кликабельность фона для закрытия
			sb.Begin();
		}

		#region Управление окном

		private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
		{
			// Позволяет перетаскивать окно мышкой за верхнюю панель
			if (e.ChangedButton == MouseButton.Left)
			{
				this.DragMove();
			}
		}

		private void Minimize_Click(object sender, RoutedEventArgs e)
		{
			this.WindowState = WindowState.Minimized;
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			// Вся логика теперь в OnClosing — тот же путь обрабатывает и закрытие через
			// панель задач/Alt+F4, а не только эту кнопку
			Close();
		}

		/// <summary>
		/// Перехватывает ЛЮБУЮ попытку закрыть окно, откуда бы она ни пришла (кастомная
		/// кнопка, панель задач, Alt+F4). Если включён MinimizeToTray и мы не в процессе
		/// настоящего выхода — сворачиваем в трей вместо закрытия. Иначе гарантируем полный
		/// выход через Application.Shutdown() (mutex, отмена регистрации хоткеев и т.п.), а
		/// не просто закрытие этого окна — иначе трей-иконка/остальные окна остались бы висеть.
		/// </summary>
		protected override void OnClosing(CancelEventArgs e)
		{
			if (Properties.Settings.Default.MinimizeToTray && !IsExiting)
			{
				e.Cancel = true;
				Hide();
				return;
			}

			if (!IsExiting)
			{
				IsExiting = true;
				e.Cancel = true;
				App.Current.Shutdown();
				return;
			}

			base.OnClosing(e);
		}

		#endregion

		#region Выдвижное меню

		// Закрываем меню при клике на затемненную область вне меню
		private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
		{
			_viewModel.CloseMenuCommand.Execute(null);
		}

		#endregion

		#region Навигация по страницам

		private void GoToBinds(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new BindsPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		private void GoToSettings(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new SettingsPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		private void GoToMarketingTemplates(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new MarketingTemplatesPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		private void GoToDiagnostics(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new DiagnosticsPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		private void GoToAbout(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new AboutPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		#endregion
	}
}
