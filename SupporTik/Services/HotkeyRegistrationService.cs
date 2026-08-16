using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Newtonsoft.Json;
using SupporTik.Classes;
using SupporTik.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Владеет списком биндов и групп (раньше — статические поля App._bindKeys/_groupInfos),
	/// регистрацией хоткеев, сборкой всплывающего меню при коллизии и экспортом/импортом.
	/// Единственное место, которое перезагружает бинды с диска (RegisterDefaultHotkeys) —
	/// вызывающий код должен перечитывать BindKeys ПОСЛЕ вызова, а не держать старую ссылку.
	/// </summary>
	public class HotkeyRegistrationService
	{
		private readonly StorageService _storage;
		private readonly IHotkeyService _hotkeyService;
		private readonly ITextPasteService _pasteService;
		private readonly QuickTextWindow _quickMenu;
		private readonly TaskbarIcon _notifyIcon;

		private List<BindKeys> _bindKeys = new List<BindKeys>();
		private List<BindGroupInfo> _groupInfos;

		private MarketingWindow _marketingWindow;

		public IReadOnlyList<BindKeys> BindKeys => _bindKeys;

		public HotkeyRegistrationService(StorageService storage, IHotkeyService hotkeyService,
			ITextPasteService pasteService, QuickTextWindow quickMenu, TaskbarIcon notifyIcon)
		{
			_storage = storage;
			_hotkeyService = hotkeyService;
			_pasteService = pasteService;
			_quickMenu = quickMenu;
			_notifyIcon = notifyIcon;

			_groupInfos = _storage.LoadData<BindGroupInfo>("groups.json");
		}

		#region Бинды

		public void AddBind(BindKeys bind)
		{
			_bindKeys.Add(bind);
			SaveAndReRegister();
		}

		public void DeleteBind(BindKeys bind)
		{
			_bindKeys.Remove(bind);

			// Если это был последний бинд с этим сочетанием — группа как таковая перестаёт
			// существовать. Если не подчистить её кастомное имя здесь, оно "утечёт": создав
			// позже новую группу на том же самом сочетании клавиш, пользователь неожиданно
			// увидел бы старое название вместо чистого состояния
			bool anyRemaining = _bindKeys.Any(b => b.Key == bind.Key && b.Modifiers == bind.Modifiers);
			if (!anyRemaining)
			{
				SetGroupName(bind.Key, bind.Modifiers, null);
			}

			SaveAndReRegister();
		}

		public void SaveAndReRegister()
		{
			_storage.SaveData(_bindKeys);
			RegisterDefaultHotkeys();
		}

		/// <summary>
		/// Только сохраняет на диск, без перерегистрации — сочетание клавиш не меняется,
		/// поэтому не нужно ни трогать хук, ни (что важнее) перезагружать _bindKeys с диска,
		/// иначе ViewModel, из которой вызвана правка, "осиротеет" (её BindKeys перестанет
		/// быть тем же самым объектом, что лежит в списке).
		/// </summary>
		public void SaveBindsOnly()
		{
			_storage.SaveData(_bindKeys);
		}

		#endregion

		#region Названия групп

		public string GetGroupName(Key key, ModifierKeys modifiers)
		{
			return _groupInfos.FirstOrDefault(g => g.Key == key && g.Modifiers == modifiers)?.Name;
		}

		public void SetGroupName(Key key, ModifierKeys modifiers, string name)
		{
			var existing = _groupInfos.FirstOrDefault(g => g.Key == key && g.Modifiers == modifiers);

			if (string.IsNullOrWhiteSpace(name))
			{
				if (existing != null)
				{
					_groupInfos.Remove(existing);
				}
			}
			else if (existing != null)
			{
				existing.Name = name.Trim();
			}
			else
			{
				_groupInfos.Add(new BindGroupInfo { Key = key, Modifiers = modifiers, Name = name.Trim() });
			}

			_storage.SaveData(_groupInfos, "groups.json");
		}

		#endregion

		#region Регистрация хоткеев

		public void RegisterDefaultHotkeys()
		{
			_bindKeys = _storage.LoadData<BindKeys>()
									  .OrderBy(b => b.Modifiers)
									  .ThenBy(b => b.Key)
									  .ToList();

			_hotkeyService.UnregisterAll();

			var specials = GetSpecialHotkeys();
			var groups = _bindKeys.GroupBy(b => new { b.Key, b.Modifiers }).ToList();

			foreach (var group in groups)
			{
				var binds = group.ToList();
				var firstBind = binds[0];
				var matchingSpecials = specials.Where(s =>
					s.Key != Key.None && s.Key == firstBind.Key && s.Modifiers == firstBind.Modifiers).ToList();

				if (binds.Count == 1 && matchingSpecials.Count == 0)
				{
					var bind = binds[0];
					_hotkeyService.RegisterHotkey(
						bind.Key,
						bind.Modifiers,
						() =>
						{
							_pasteService.PasteText(bind.Text);
						});
				}
				else
				{
					// Либо несколько шаблонов на одном сочетании, либо оно совпадает со
					// специальным хоткеем (или и то, и другое) — в обоих случаях нужен выбор
					_hotkeyService.RegisterHotkey(
						firstBind.Key,
						firstBind.Modifiers,
						() => OnQuickMenuHotkeyPressed(binds, matchingSpecials));
				}
			}

			// Оставшиеся специальные хоткеи группируем между собой. Если NDA и меню
			// рекламы назначены на одно сочетание, ни одно действие не теряется — выбор
			// показывается в том же быстром меню, что и при коллизии обычных биндов.
			var standaloneSpecialGroups = specials
				.Where(s => s.Key != Key.None && !groups.Any(g =>
					g.Key.Key == s.Key && g.Key.Modifiers == s.Modifiers))
				.GroupBy(s => new { s.Key, s.Modifiers });

			foreach (var specialGroup in standaloneSpecialGroups)
			{
				var actions = specialGroup.ToList();
				if (actions.Count == 1)
				{
					var special = actions[0];
					_hotkeyService.RegisterHotkey(special.Key, special.Modifiers, special.Action);
				}
				else
				{
					_hotkeyService.RegisterHotkey(
						specialGroup.Key.Key,
						specialGroup.Key.Modifiers,
						() => OnQuickMenuHotkeyPressed(new List<BindKeys>(), actions));
				}
			}
		}

		private void OnQuickMenuHotkeyPressed(List<BindKeys> binds, List<SpecialHotkey> specials)
		{
			if (!_pasteService.IsPaused)
			{
				string groupTitle = binds.Count == 0
					? null
					: GetGroupName(binds[0].Key, binds[0].Modifiers);

				_quickMenu.SetEntries(groupTitle, BuildQuickMenuEntries(binds, specials));
				_quickMenu.ShowAtCursor();
			}
		}

		private void OnMarketingMenuHotkeyPressed()
		{
			if (_pasteService.IsPaused)
			{
				return;
			}

			ShowMarketingMenu();
		}

		/// <summary>
		/// Показывает/активирует окно меню рекламы — вызывается и по хоткею (см.
		/// OnMarketingMenuHotkeyPressed, с проверкой паузы вставки), и напрямую из
		/// пункта трей-меню "Меню рекламы" (см. IBindsService.ShowMarketingMenu), где
		/// пауза вставки ни при чём — это осознанный клик пользователя, а не хоткей.
		/// </summary>
		public void ShowMarketingMenu()
		{
			// Окно создаётся только один раз за всё время работы приложения — закрытие
			// по крестику теперь лишь прячет его (см. MarketingWindow.HideAnimated), а не
			// уничтожает, поэтому WebView2 и уже пройденный логин не сбрасываются между
			// повторными открытиями
			if (_marketingWindow == null)
			{
				_marketingWindow = new MarketingWindow();
			}

			if (!_marketingWindow.IsVisible)
			{
				_marketingWindow.ShowAnimated();
			}
			else
			{
				_marketingWindow.Activate();
			}
		}
		public async Task ClearAuthorizationAsync()
		{
			if (_marketingWindow == null)
			{
				_marketingWindow = new MarketingWindow();
			}

			// Если окно ещё ни разу не было загружено,
			// WebView2 тоже ещё не может нормально инициализироваться.
			if (!_marketingWindow.IsLoaded)
			{
				_marketingWindow.Show();

				// Даём WPF создать HWND и загрузить WebView2.
				await _marketingWindow.Dispatcher.InvokeAsync(
					() => { },
					System.Windows.Threading.DispatcherPriority.Loaded);

				_marketingWindow.Hide();
			}

			await _marketingWindow.ClearAuthorizationAsync();
		}

		/// <summary>
		/// Хоткей, который может совпасть с обычным биндом. В этом случае прямая
		/// регистрация невозможна (см. RegisterDefaultHotkeys) — вместо неё нажатие
		/// открывает QuickTextWindow, где это действие показывается пунктом меню.
		/// </summary>
		private class SpecialHotkey
		{
			public Key Key;
			public ModifierKeys Modifiers;
			public string MenuLabel;
			public Action Action;
		}

		private List<SpecialHotkey> GetSpecialHotkeys()
		{
			return new List<SpecialHotkey>
			{
				new SpecialHotkey
				{
					Key = (Key)Properties.Settings.Default.SelectedKey,
					Modifiers = (ModifierKeys)Properties.Settings.Default.SelectedModifiers,
					MenuLabel = "NDA Замена",
					Action = () => _pasteService.ReplaceSelectionInExternalApp()
				},
				new SpecialHotkey
				{
					Key = (Key)Properties.Settings.Default.MarketingMenuKey,
					Modifiers = (ModifierKeys)Properties.Settings.Default.MarketingMenuModifiers,
					MenuLabel = "Меню рекламы",
					Action = () => OnMarketingMenuHotkeyPressed()
				}
			};
		}

		/// <summary>
		/// Собирает пункты всплывающего меню для группы биндов с общим сочетанием клавиш.
		/// QuickTextWindow сам ничего не знает про BindKeys/настройки — вся эта логика здесь.
		/// </summary>
		private List<QuickMenuEntry> BuildQuickMenuEntries(
			List<BindKeys> binds,
			List<SpecialHotkey> specials)
		{
			var entries = binds
				.Select(bind => new QuickMenuEntry
				{
					Name = bind.Name,
					Action = () =>
					{
						_pasteService.PasteText(bind.Text);
					}
				})
				.ToList();

			foreach (var special in specials)
			{
				entries.Add(new QuickMenuEntry
				{
					Name = special.MenuLabel,
					Action = special.Action,
					IsSpecial = true
				});
			}

			return entries;
		}

		#endregion

		#region Экспорт / Импорт

		public void ExportData(bool includeBinds, bool includeSettings, bool includeMarketingTemplates, bool autoStartEnabled)
		{
			try
			{
				var saveFileDialog = new SaveFileDialog
				{
					Filter = "JSON Files (*.json)|*.json",
					DefaultExt = "json",
					FileName = "SupporTik_Export.json",
					Title = "Экспорт данных SupporTik"
				};

				if (saveFileDialog.ShowDialog() == true)
				{
					var package = new ExportPackage
					{
						Version = 2,
						Binds = includeBinds ? _bindKeys : null,
						Groups = includeBinds ? _groupInfos : null,
						Settings = includeSettings ? new ExportSettings
						{
							StartMinimized = Properties.Settings.Default.StartMinimized,
							MinimizeToTray = Properties.Settings.Default.MinimizeToTray,
							SelectedKey = Properties.Settings.Default.SelectedKey,
							SelectedModifiers = Properties.Settings.Default.SelectedModifiers,
							MarketingMenuKey = Properties.Settings.Default.MarketingMenuKey,
							MarketingMenuModifiers = Properties.Settings.Default.MarketingMenuModifiers,
							MarketingMenuFromLeft = Properties.Settings.Default.MarketingMenuFromLeft,
							IsLightTheme = Properties.Settings.Default.IsLightTheme,
							FollowSystemTheme = Properties.Settings.Default.FollowSystemTheme,
							AutoStartEnabled = autoStartEnabled,
							RecentMarketingUids = Properties.Settings.Default.RecentMarketingUids
						} : null,
						MarketingTemplates = includeMarketingTemplates
							? CompositionRoot.Current.MarketingTemplates.GetAll().ToList()
							: null
					};

					string json = JsonConvert.SerializeObject(package, Formatting.Indented,
						new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
					File.WriteAllText(saveFileDialog.FileName, json);
					var exportedSections = new List<string>();
					if (includeBinds) exportedSections.Add("бинды");
					if (includeSettings) exportedSections.Add("настройки");
					if (includeMarketingTemplates) exportedSections.Add("рекламные шаблоны");

					_notifyIcon?.ShowBalloonTip(
						"Экспорт",
						$"Сохранено: {string.Join(", ", exportedSections)}.",
						BalloonIcon.None);
				}
			}
			catch (Exception ex)
			{
				LoggingService.LogError("HotkeyRegistrationService.ExportData", ex);
				_notifyIcon?.ShowBalloonTip(
					"Экспорт",
					"Произошла ошибка при экспорте!",
					BalloonIcon.None);
			}
		}

		public void ImportData()
		{
			try
			{
				var openFileDialog = new OpenFileDialog
				{
					Filter = "JSON Files (*.json)|*.json",
					DefaultExt = "json",
					Title = "Выберите файл для импорта"
				};

				if (openFileDialog.ShowDialog() != true)
				{
					return;
				}

				string json = File.ReadAllText(openFileDialog.FileName);
				ExportPackage package;
				if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
				{
					// Самый старый формат: в корне лежал только список биндов.
					package = new ExportPackage
					{
						Binds = JsonConvert.DeserializeObject<List<BindKeys>>(json)
					};
				}
				else
				{
					package = JsonConvert.DeserializeObject<ExportPackage>(json);
				}

				if (package == null || (package.Binds == null && package.Settings == null && package.MarketingTemplates == null))
				{
					throw new InvalidOperationException("Файл не похож на экспорт SupporTik.");
				}

				if (package.Binds != null)
					ValidateImportedData(package.Binds, package.Groups);
				if (package.MarketingTemplates != null)
					MarketingTemplateService.ValidateImported(package.MarketingTemplates);

				// Сначала полностью сохраняем проверенный пакет. Живое состояние приложения
				// меняем только после успешной записи, чтобы ошибка диска не оставила UI и
				// зарегистрированные хоткеи в полуприменённом состоянии.
				if (package.Binds != null)
				{
					_storage.SaveData(package.Binds);
					if (package.Groups != null)
						_storage.SaveData(package.Groups, "groups.json");

					_bindKeys = package.Binds;
					if (package.Groups != null)
						_groupInfos = package.Groups;
				}

				if (package.MarketingTemplates != null)
					CompositionRoot.Current.MarketingTemplates.ReplaceAll(package.MarketingTemplates);

				if (package.Settings != null)
				{
					Properties.Settings.Default.StartMinimized = package.Settings.StartMinimized;
					Properties.Settings.Default.MinimizeToTray = package.Settings.MinimizeToTray;
					Properties.Settings.Default.SelectedKey = package.Settings.SelectedKey;
					Properties.Settings.Default.SelectedModifiers = package.Settings.SelectedModifiers;

					// В старом пакетном формате этих полей ещё не было. Не сбрасываем их
					// значениями по умолчанию при импорте такого файла.
					if (package.Version >= 2)
					{
						Properties.Settings.Default.MarketingMenuKey = package.Settings.MarketingMenuKey;
						Properties.Settings.Default.MarketingMenuModifiers = package.Settings.MarketingMenuModifiers;
						Properties.Settings.Default.MarketingMenuFromLeft = package.Settings.MarketingMenuFromLeft;
						Properties.Settings.Default.IsLightTheme = package.Settings.IsLightTheme;
						Properties.Settings.Default.FollowSystemTheme = package.Settings.FollowSystemTheme;
						Properties.Settings.Default.ThemePreferenceInitialized = true;
						Properties.Settings.Default.RecentMarketingUids = package.Settings.RecentMarketingUids ?? string.Empty;
					}
					Properties.Settings.Default.Save();

					if (package.Version >= 2)
					{
						new AppSettingsServiceAdapter().SetAutoStart(package.Settings.AutoStartEnabled);
						var themeService = new ThemeService();
						if (package.Settings.FollowSystemTheme)
							themeService.SetFollowSystem(true);
						else
						{
							themeService.SetFollowSystem(false);
							ThemeService.Apply(package.Settings.IsLightTheme);
						}
					}
				}

				if (package.Binds != null || package.Settings != null)
					RegisterDefaultHotkeys();

				var importedSections = new List<string>();
				if (package.Binds != null) importedSections.Add("бинды");
				if (package.Settings != null) importedSections.Add("настройки");
				if (package.MarketingTemplates != null) importedSections.Add("рекламные шаблоны");

				_notifyIcon?.ShowBalloonTip(
					"Импорт",
					$"Импортировано: {string.Join(", ", importedSections)}.",
					BalloonIcon.None);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("HotkeyRegistrationService.ImportData", ex);
				_notifyIcon?.ShowBalloonTip(
					"Импорт",
					"Произошла ошибка при импорте!",
					BalloonIcon.None);
			}
		}

		private static void ValidateImportedData(List<BindKeys> binds, List<BindGroupInfo> groups)
		{
			if (binds.Any(b => b == null || b.Key == Key.None || string.IsNullOrWhiteSpace(b.Name)))
			{
				throw new InvalidOperationException("Экспорт содержит некорректные бинды.");
			}

			if (groups != null && groups.Any(g => g == null || g.Key == Key.None))
			{
				throw new InvalidOperationException("Экспорт содержит некорректные группы.");
			}
		}

		#endregion

		#region Завершение работы

		public void SaveOnExit()
		{
			_storage.SaveData(_bindKeys);
		}

		public void UnregisterAllOnExit()
		{
			_hotkeyService.UnregisterAll();
			(_hotkeyService as IDisposable)?.Dispose();
		}

		#endregion
	}
}
