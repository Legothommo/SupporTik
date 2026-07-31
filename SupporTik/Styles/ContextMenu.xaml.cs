using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SupporTik.Styles
{
	public partial class ContextMenu : ResourceDictionary
	{
		public static TaskbarIcon _notifyIcon;
		public ContextMenu()
		{
			InitializeComponent();
			_notifyIcon = (TaskbarIcon)this["MyNotifyIcon"];
			_notifyIcon.TrayMouseDoubleClick += (s, args) => ShowMainWindow(null, null);
		}

		private void ShowMainWindow(object sender, RoutedEventArgs e)
		{
			var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

			// 2. Если окно еще ни разу не создавалось (или было полностью закрыто Close())
			if (mainWindow == null)
			{
				mainWindow = new MainWindow();
			}

			// 3. Показываем его, разворачиваем из свернутого состояния и выводим на передний план
			if (mainWindow.Visibility != Visibility.Visible)
			{
				mainWindow.Show();
			}

			if (mainWindow.WindowState == WindowState.Minimized)
			{
				mainWindow.WindowState = WindowState.Normal;
			}

			mainWindow.Activate();
			mainWindow.Focus();
		}

		private void OpenSettings(object sender, RoutedEventArgs e)
		{
			var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

			// 2. Если окно еще ни разу не создавалось (или было полностью закрыто Close())
			if (mainWindow == null)
			{
				mainWindow = new MainWindow();
			}

			// 3. Показываем его, разворачиваем из свернутого состояния и выводим на передний план
			if (mainWindow.Visibility != Visibility.Visible)
			{
				mainWindow.Show();
			}

			if (mainWindow.WindowState == WindowState.Minimized)
			{
				mainWindow.WindowState = WindowState.Normal;
			}

			mainWindow.Activate();
			mainWindow.Focus();
			mainWindow.MainFrame.Navigate(new SettingsPage());
		}

		private void EnableText(object sender, RoutedEventArgs e)
		{
			App._pasteService.IsPaused = !App._pasteService.IsPaused;

			var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

			// 2. Достаем текущую страницу из Frame (предположим, ваш Frame называется MainFrame)
			if (mainWindow.MainFrame.Content is BindsPage pageBinds)
			{
				// 3. Вызываем метод обновления на странице
				pageBinds.UpdateStatus(App._pasteService.IsPaused);
			}

			MenuItem item = sender as MenuItem;
			if (!App._pasteService.IsPaused)
			{
				item.Header = "Включено";
				item.Foreground = (Brush)Application.Current.FindResource("AccentGreen");
			}
			else
			{
				item.Header = "Выключено";
				item.Foreground = (Brush)Application.Current.FindResource("AccentCoral");
			}
		}

		private void ExitApplication(object sender, RoutedEventArgs e)
		{
			_notifyIcon?.Dispose();

			Application.Current.Shutdown();
		}
	}
}
