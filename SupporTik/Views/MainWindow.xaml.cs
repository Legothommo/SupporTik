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

		private readonly MainWindowViewModel _viewModel;

		public MainWindow()
		{
			InitializeComponent();
			Instance = this;

			_viewModel = new MainWindowViewModel(new ThemeService());
			_viewModel.PropertyChanged += ViewModel_PropertyChanged;
			DataContext = _viewModel;

			MainFrame.Navigate(new BindsPage());
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

		private void GoToAbout(object sender, RoutedEventArgs e)
		{
			MainFrame.Navigate(new AboutPage());
			_viewModel.CloseMenuCommand.Execute(null);
		}

		#endregion
	}
}
