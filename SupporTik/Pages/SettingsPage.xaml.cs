using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SupporTik.Services;
using SupporTik.ViewModels;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для SettingsPage.xaml
	/// </summary>
	public partial class SettingsPage : Page
	{
		private readonly SettingsPageViewModel _viewModel;
		private readonly IAppSettingsService _settingsService;

		public SettingsPage()
		{
			InitializeComponent();

			_settingsService = new AppSettingsServiceAdapter();
			_viewModel = new SettingsPageViewModel(_settingsService);
			DataContext = _viewModel;
		}

		#region Обработка захвата Хоткея (NDA Замена) — Win32-хук, остаётся во View

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			e.Handled = true;
			HotkeyCaptureArea.Focus();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("StatusActiveBrush");
			_viewModel.NdaHotkeyDisplay = "Нажмите сочетание клавиш...";
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("StatusActiveBrush");

			// Захватываем сочетание напрямую через хук — так нажатие достаётся нам раньше,
			// чем его успела бы перехватить сторонняя программа через RegisterHotKey
			_settingsService.StartHotkeyCapture(OnNdaHotkeyCaptured);
		}

		private void OnNdaHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_viewModel.OnNdaHotkeyCaptured(key, modifiers);
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush");
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");
		}

		private void HotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			_settingsService.CancelHotkeyCapture();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush");
			_viewModel.ResetNdaDisplayIfEmpty();
		}

		#endregion

		#region Обработка захвата Хоткея (Меню рекламы) — Win32-хук, остаётся во View

		private void MarketingHotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			e.Handled = true;
			MarketingHotkeyCaptureArea.Focus();
			MarketingHotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("StatusActiveBrush");
			_viewModel.MarketingHotkeyDisplay = "Нажмите сочетание клавиш...";
			TbMarketingHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("StatusActiveBrush");

			_settingsService.StartHotkeyCapture(OnMarketingHotkeyCaptured);
		}

		private void OnMarketingHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_viewModel.OnMarketingHotkeyCaptured(key, modifiers);
			TbMarketingHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush");
			MarketingHotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");
		}

		private void MarketingHotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			_settingsService.CancelHotkeyCapture();
			MarketingHotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");
			TbMarketingHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush");
			_viewModel.ResetMarketingDisplayIfEmpty();
		}

		#endregion
	}
}
