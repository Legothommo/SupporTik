using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Services;
using SupporTik.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SupporTik.Styles
{
	public partial class ContextMenu : ResourceDictionary
	{
		public static TaskbarIcon _notifyIcon;

		private readonly TrayMenuViewModel _viewModel;

		public ContextMenu()
		{
			InitializeComponent();

			IMainWindowProvider mainWindowProvider = new MainWindowProvider();
			IBindsService bindsService = new BindsServiceAdapter();
			_viewModel = new TrayMenuViewModel(mainWindowProvider, bindsService);
			_viewModel.PasteToggled += ViewModel_PasteToggled;

			_notifyIcon = (TaskbarIcon)this["MyNotifyIcon"];
			_notifyIcon.TrayMouseDoubleClick += (s, args) => _viewModel.ShowMainWindowCommand.Execute(null);

			var trayMenu = (System.Windows.Controls.ContextMenu)this["TrayContextMenu"];
			trayMenu.DataContext = _viewModel;
		}

		private void ViewModel_PasteToggled(object sender, bool isPaused)
		{
			var trayMenu = (System.Windows.Controls.ContextMenu)this["TrayContextMenu"];
			MenuItem item = trayMenu.Items.OfType<MenuItem>().FirstOrDefault(mi => (string)mi.Tag == "EnableToggle");

			if (item == null)
			{
				return;
			}

			if (!isPaused)
			{
				item.Header = "Включено";
				item.Foreground = (Brush)Application.Current.FindResource("StatusActiveBrush");
			}
			else
			{
				item.Header = "Выключено";
				item.Foreground = (Brush)Application.Current.FindResource("StatusPausedBrush");
			}
		}

		private void ExitApplication(object sender, RoutedEventArgs e)
		{
			// Без этого MainWindow.OnClosing воспринял бы закрытие как обычное — и просто
			// свернул бы окно в трей вместо настоящего выхода (см. MainWindow.IsExiting)
			MainWindow.IsExiting = true;

			_notifyIcon?.Dispose();

			Application.Current.Shutdown();
		}
	}
}
