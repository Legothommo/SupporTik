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

					// Ручной выбор темы отключает слежение за системной (см. ThemeService.SetTheme) —
					// отражаем это и в переключателе "Как в Windows"
					if (_followSystemTheme)
					{
						_followSystemTheme = false;
						OnPropertyChanged(nameof(FollowSystemTheme));
					}
				}
			}
		}

		private bool _followSystemTheme;
		public bool FollowSystemTheme
		{
			get => _followSystemTheme;
			set
			{
				if (SetProperty(ref _followSystemTheme, value))
				{
					_themeService.SetFollowSystem(value);

					// При включении сразу подтягиваем то, что реально применилось (текущую
					// системную тему), чтобы переключатель "Светлая тема" не показывал старое
					// значение до следующей смены темы в Windows
					if (value)
					{
						_isLightTheme = _themeService.IsLightTheme;
						OnPropertyChanged(nameof(IsLightTheme));
					}
				}
			}
		}

		public ICommand ToggleMenuCommand { get; }
		public ICommand CloseMenuCommand { get; }

		public MainWindowViewModel(IThemeService themeService)
		{
			_themeService = themeService;
			_isLightTheme = themeService.IsLightTheme;
			_followSystemTheme = themeService.IsFollowingSystem;

			ToggleMenuCommand = new RelayCommand(() => IsMenuOpen = !IsMenuOpen);
			CloseMenuCommand = new RelayCommand(() => IsMenuOpen = false);
		}
	}
}
