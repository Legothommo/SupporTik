using Hardcodet.Wpf.TaskbarNotification;
using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace SupporTik.ViewModels
{
	public class SettingsPageViewModel : ViewModelBase
	{
		private const string NoHotkeyText = "Нажмите для назначения...";
		private const string CheckUpdatesDefaultLabel = "Проверить обновления";
		private const string CheckUpdatesRunningLabel = "Проверяем...";

		private readonly IAppSettingsService _settings;
		private readonly UpdateCheckService _updateCheckService;

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

		private string _checkUpdatesLabel = CheckUpdatesDefaultLabel;
		public string CheckUpdatesLabel
		{
			get => _checkUpdatesLabel;
			set => SetProperty(ref _checkUpdatesLabel, value);
		}

		public ICommand SaveCommand { get; }
		public ICommand ExportCommand { get; }
		public ICommand ImportCommand { get; }
		public ICommand CheckUpdatesCommand { get; }
		public ICommand ResetCommand { get; }
		public ICommand ClearAuthorizationCommand { get; }

		public SettingsPageViewModel(IAppSettingsService settings)
		{
			_settings = settings;
			_updateCheckService = new UpdateCheckService();

			SaveCommand = new RelayCommand(Save);
			ExportCommand = new RelayCommand(() => _settings.ExportData());
			ImportCommand = new RelayCommand(() =>
			{
				_settings.ImportData();
				// Импорт мог изменить настройки/хоткеи — обновляем то, что показано на экране
				LoadAll();
			});
			CheckUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
			ClearAuthorizationCommand = new AsyncRelayCommand(ClearAuthorizationAsync);
			ResetCommand = new RelayCommand(ResetToDefaults);

			LoadAll();
		}

		private void ResetToDefaults()
		{
			MessageBoxResult result = MessageBox.Show(
				"Все настройки (хоткеи, автозапуск, тема, история UID в меню рекламы и т.п.) вернутся к значениям по умолчанию. Бинды это не затронет. Продолжить?",
				"Сбросить настройки",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);

			if (result != MessageBoxResult.Yes)
			{
				return;
			}

			_settings.ResetToDefaults();
			// Настройки в реестре/на диске изменились "снаружи" от обычных сеттеров —
			// перечитываем всё то же, что и после импорта
			LoadAll();
		}

		private async Task ClearAuthorizationAsync()
		{
			MessageBoxResult result = MessageBox.Show(
				"Будет удалена авторизация Яндекс Бизнеса и DataLens.\n\n" +
				"При следующем поиске потребуется войти в аккаунты заново.\n\n" +
				"Продолжить?",
				"Удалить авторизацию",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);

			if (result != MessageBoxResult.Yes)
				return;

			try
			{
				await _settings.ClearAuthorizationAsync();

				CompositionRoot.Current?.NotifyIcon?.ShowBalloonTip(
					"Авторизация удалена",
					"Сессии Яндекс Бизнеса и DataLens удалены.",
					BalloonIcon.Info);
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					$"Не удалось удалить авторизацию:\n{ex.Message}",
					"Ошибка",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		/// <summary>
		/// Тот же UpdateCheckService, что и автопроверка на старте (см. App.OnStartup) —
		/// только по явному клику, а не молча. Баллун обновления показывается тем же
		/// App.ShowUpdateBalloon, чтобы клик по нему одинаково открывал ссылку в обоих
		/// случаях (см. App.NotifyIcon_TrayBalloonTipClicked).
		/// </summary>
		private async Task CheckForUpdatesAsync()
		{
			CheckUpdatesLabel = CheckUpdatesRunningLabel;

			try
			{
				UpdateInfo update = await _updateCheckService.CheckAsync();
				TaskbarIcon notifyIcon = CompositionRoot.Current?.NotifyIcon;

				if (notifyIcon == null)
				{
					return;
				}

				if (update != null)
				{
					App.ShowUpdateBalloon(notifyIcon, update);
				}
				else
				{
					notifyIcon.ShowBalloonTip("Обновлений нет", "У вас установлена последняя версия SupporTik.", BalloonIcon.Info);
				}
			}
			finally
			{
				CheckUpdatesLabel = CheckUpdatesDefaultLabel;
			}
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
