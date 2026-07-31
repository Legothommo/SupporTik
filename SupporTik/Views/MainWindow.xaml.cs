using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SupporTik.Pages;

namespace SupporTik
{
	/// <summary>
	/// Логика взаимодействия для MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private bool _isMenuOpen = false;

		public static MainWindow Instance { get; private set; }

		public MainWindow()
		{
			InitializeComponent();
			Instance = this;

			if (Properties.Settings.Default.StartMinimized)
			{
				this.Hide();
			}

			MainFrame.Navigate(new BindsPage());
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
			if (Properties.Settings.Default.MinimizeToTray)
			{
				this.Hide();
			}
			else
			{
				App.Current.Shutdown();
			}
		}

		#endregion

		#region Выдвижное меню

		private void BtnBurger_Click(object sender, RoutedEventArgs e)
		{
			if (!_isMenuOpen)
			{
				OpenMenu();
			}
			else
			{
				CloseMenu();
			}
		}

		private void OpenMenu()
		{
			var sb = (Storyboard)FindResource("OpenMenu");
			Overlay.IsHitTestVisible = true; // Включаем кликабельность фона для закрытия
			sb.Begin();
			_isMenuOpen = true;
		}

		private void CloseMenu()
		{
			var sb = (Storyboard)FindResource("CloseMenu");
			Overlay.IsHitTestVisible = false; // Отключаем тень
			sb.Begin();
			_isMenuOpen = false;
		}

		// Закрываем меню при клике на затемненную область вне меню
		private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
		{
			CloseMenu();
		}

		#endregion

		#region Навигация по страницам

		private void GoToBinds(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new BindsPage());
			CloseMenu();
		}

		private void GoToSettings(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new SettingsPage());
			CloseMenu();
		}

		private void GoToAbout(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new AboutPage());
			CloseMenu();
		}

		#endregion
	}
}