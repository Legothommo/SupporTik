using System.Windows.Input;
using SupporTik.Mvvm;
using SupporTik.Services;

namespace SupporTik.ViewModels
{
	public class MainWindowViewModel : ViewModelBase
	{
		private readonly IThemeService _themeService;

		private bool _isMenuOpen;
		public bool IsMenuOpen
		{
			get => _isMenuOpen;
			set => SetProperty(ref _isMenuOpen, value);
		}

		private bool _isLightTheme;
		public bool IsLightTheme
		{
			get => _isLightTheme;
			set
			{
				if (SetProperty(ref _isLightTheme, value))
				{
					_themeService.SetTheme(value);
				}
			}
		}

		public ICommand ToggleMenuCommand { get; }
		public ICommand CloseMenuCommand { get; }

		public MainWindowViewModel(IThemeService themeService)
		{
			_themeService = themeService;
			_isLightTheme = themeService.IsLightTheme;

			ToggleMenuCommand = new RelayCommand(() => IsMenuOpen = !IsMenuOpen);
			CloseMenuCommand = new RelayCommand(() => IsMenuOpen = false);
		}
	}
}
