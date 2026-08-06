using System;
using System.Windows;
using System.Windows.Input;
using SupporTik.Mvvm;
using SupporTik.Pages;
using SupporTik.Services;

namespace SupporTik.ViewModels
{
	public class TrayMenuViewModel : ViewModelBase
	{
		private readonly IMainWindowProvider _mainWindowProvider;
		private readonly IBindsService _bindsService;

		/// <summary>Пауза вставки переключена — передаёт новое состояние IsPaused (View обновит вид пункта меню и BindsPage).</summary>
		public event EventHandler<bool> PasteToggled;

		public ICommand ShowMainWindowCommand { get; }
		public ICommand OpenSettingsCommand { get; }
		public ICommand ToggleEnabledCommand { get; }
		public ICommand ExitCommand { get; }
		public ICommand ShowMarketingMenuCommand { get; }

		public TrayMenuViewModel(IMainWindowProvider mainWindowProvider, IBindsService bindsService)
		{
			_mainWindowProvider = mainWindowProvider;
			_bindsService = bindsService;

			ShowMainWindowCommand = new RelayCommand(() => EnsureMainWindow());
			OpenSettingsCommand = new RelayCommand(OpenSettings);
			ToggleEnabledCommand = new RelayCommand(ToggleEnabled);
			ExitCommand = new RelayCommand(Exit);
			ShowMarketingMenuCommand = new RelayCommand(_bindsService.ShowMarketingMenu);
		}

		private MainWindow EnsureMainWindow()
		{
			var mainWindow = _mainWindowProvider.Current as MainWindow ?? new MainWindow();

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

			return mainWindow;
		}

		private void OpenSettings()
		{
			MainWindow mainWindow = EnsureMainWindow();
			mainWindow.MainFrame.Navigate(new SettingsPage());
		}

		private void ToggleEnabled()
		{
			if (_bindsService.IsPasteEnabled)
			{
				_bindsService.PausePaste();
			}
			else
			{
				_bindsService.ResumePaste();
			}

			bool isPaused = !_bindsService.IsPasteEnabled;

			if (_mainWindowProvider.Current is MainWindow mainWindow && mainWindow.MainFrame.Content is BindsPage pageBinds)
			{
				pageBinds.UpdateStatus(isPaused);
			}

			PasteToggled?.Invoke(this, isPaused);
		}

		private void Exit()
		{
			// Без этого MainWindow.OnClosing воспринял бы закрытие как обычное — и просто
			// свернул бы окно в трей вместо настоящего выхода (см. MainWindow.IsExiting)
			MainWindow.IsExiting = true;
			Application.Current.Shutdown();
		}
	}
}
