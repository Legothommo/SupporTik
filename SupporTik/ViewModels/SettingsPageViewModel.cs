using System.Windows.Input;
using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace SupporTik.ViewModels
{
	public class SettingsPageViewModel : ViewModelBase
	{
		private const string NoHotkeyText = "Нажмите для назначения...";

		private readonly IAppSettingsService _settings;

		private Key _ndaKey;
		private ModifierKeys _ndaModifiers;
		private Key _marketingKey;
		private ModifierKeys _marketingModifiers;

		private bool _autoStartEnabled;
		public bool AutoStartEnabled
		{
			get => _autoStartEnabled;
			set
			{
				if (SetProperty(ref _autoStartEnabled, value))
				{
					// В оригинале тумблер применял автозапуск сразу, не дожидаясь "Сохранить"
					_settings.SetAutoStart(value);
				}
			}
		}

		private bool _minimizeToTray;
		public bool MinimizeToTray
		{
			get => _minimizeToTray;
			set => SetProperty(ref _minimizeToTray, value);
		}

		private bool _startMinimized;
		public bool StartMinimized
		{
			get => _startMinimized;
			set => SetProperty(ref _startMinimized, value);
		}

		private bool _marketingMenuFromLeft;
		public bool MarketingMenuFromLeft
		{
			get => _marketingMenuFromLeft;
			set => SetProperty(ref _marketingMenuFromLeft, value);
		}

		private string _ndaHotkeyDisplay;
		public string NdaHotkeyDisplay
		{
			get => _ndaHotkeyDisplay;
			set => SetProperty(ref _ndaHotkeyDisplay, value);
		}

		private string _marketingHotkeyDisplay;
		public string MarketingHotkeyDisplay
		{
			get => _marketingHotkeyDisplay;
			set => SetProperty(ref _marketingHotkeyDisplay, value);
		}

		public ICommand SaveCommand { get; }
		public ICommand ExportCommand { get; }
		public ICommand ImportCommand { get; }

		public SettingsPageViewModel(IAppSettingsService settings)
		{
			_settings = settings;

			SaveCommand = new RelayCommand(Save);
			ExportCommand = new RelayCommand(() => _settings.ExportData());
			ImportCommand = new RelayCommand(() =>
			{
				_settings.ImportData();
				// Импорт мог изменить настройки/хоткеи — обновляем то, что показано на экране
				LoadAll();
			});

			LoadAll();
		}

		private void LoadAll()
		{
			// Через backing-поля напрямую (не через сеттеры) — иначе AutoStartEnabled
			// заново применил бы автозапуск при простой перезагрузке отображения
			_autoStartEnabled = _settings.AutoStartEnabled;
			OnPropertyChanged(nameof(AutoStartEnabled));
			_minimizeToTray = _settings.MinimizeToTray;
			OnPropertyChanged(nameof(MinimizeToTray));
			_startMinimized = _settings.StartMinimized;
			OnPropertyChanged(nameof(StartMinimized));
			_marketingMenuFromLeft = _settings.MarketingMenuFromLeft;
			OnPropertyChanged(nameof(MarketingMenuFromLeft));

			_ndaKey = _settings.NdaKey;
			_ndaModifiers = _settings.NdaModifiers;
			NdaHotkeyDisplay = ComputeDisplay(_ndaKey, _ndaModifiers);

			_marketingKey = _settings.MarketingKey;
			_marketingModifiers = _settings.MarketingModifiers;
			MarketingHotkeyDisplay = ComputeDisplay(_marketingKey, _marketingModifiers);
		}

		private static string ComputeDisplay(Key key, ModifierKeys modifiers)
		{
			return key != Key.None && modifiers != ModifierKeys.None
				? KeyExtensions.ToFriendlyShortcut(modifiers, key)
				: NoHotkeyText;
		}

		/// <summary>Вызывается View сразу по факту захвата (см. SettingsPage.xaml.cs) — там же сбрасывается подсветка.</summary>
		public void OnNdaHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_ndaKey = key;
			_ndaModifiers = modifiers;
			NdaHotkeyDisplay = KeyExtensions.ToFriendlyShortcut(modifiers, key);
		}

		public void OnMarketingHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_marketingKey = key;
			_marketingModifiers = modifiers;
			MarketingHotkeyDisplay = KeyExtensions.ToFriendlyShortcut(modifiers, key);
		}

		/// <summary>Вызывается кодом View при потере фокуса зоной захвата, если ничего не выбрано.</summary>
		public void ResetNdaDisplayIfEmpty()
		{
			if (_ndaKey == Key.None)
			{
				NdaHotkeyDisplay = NoHotkeyText;
			}
			else
			{
				NdaHotkeyDisplay = KeyExtensions.ToFriendlyShortcut(_ndaModifiers, _ndaKey);
			}
		}

		public void ResetMarketingDisplayIfEmpty()
		{
			if (_marketingKey == Key.None)
			{
				MarketingHotkeyDisplay = NoHotkeyText;
			}
			else
			{
				MarketingHotkeyDisplay = KeyExtensions.ToFriendlyShortcut(_marketingModifiers, _marketingKey);
			}
		}

		private void Save()
		{
			_settings.Save(_ndaKey, _ndaModifiers, _marketingKey, _marketingModifiers,
				AutoStartEnabled, MinimizeToTray, StartMinimized, MarketingMenuFromLeft);
		}
	}
}
