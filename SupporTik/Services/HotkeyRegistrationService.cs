using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Newtonsoft.Json;
using SupporTik.Classes;
using SupporTik.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

				bool collidesWithSpecial = specials.Any(s =>
					s.Key != Key.None && s.Key == firstBind.Key && s.Modifiers == firstBind.Modifiers);

				if (binds.Count == 1 && !collidesWithSpecial)
				{
					var bind = binds[0];
					_hotkeyService.RegisterHotkey(
						bind.Name,
						bind.Key,
						bind.Modifiers,
						() => _pasteService.PasteText(bind.Text));
				}
				else
				{
					// Либо несколько шаблонов на одном сочетании, либо оно совпадает со
					// специальным хоткеем (или и то, и другое) — в обоих случаях нужен выбор
					_hotkeyService.RegisterHotkey(
						"OpenQuickMenu" + firstBind.Name,
						firstBind.Key,
						firstBind.Modifiers,
						() => OnQuickMenuHotkeyPressed(binds));
				}
			}

			// Специальные хоткеи регистрируем отдельно, только если их сочетание не занято
			// ни одним биндом — если занято, оно уже вошло в QuickTextWindow выше
			foreach (var special in specials)
			{
				if (special.Key == Key.None)
				{
					continue;
				}

				bool usedByBind = groups.Any(g => g.Key.Key == special.Key && g.Key.Modifiers == special.Modifiers);
				if (usedByBind)
				{
					continue;
				}

				_hotkeyService.RegisterHotkey(special.Name, special.Key, special.Modifiers, special.Action);
			}
		}

		private void OnQuickMenuHotkeyPressed(List<BindKeys> keys)
		{
			// Вызываем показ окна возле мыши
			if (!_pasteService.IsPaused)
			{
				var firstBind = keys[0];
				string groupTitle = GetGroupName(firstBind.Key, firstBind.Modifiers);

				_quickMenu.SetEntries(groupTitle, BuildQuickMenuEntries(keys));
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

		/// <summary>
		/// Хоткей, который может совпасть с обычным биндом. В этом случае прямая
		/// регистрация невозможна (см. RegisterDefaultHotkeys) — вместо неё нажатие
		/// открывает QuickTextWindow, где это действие показывается пунктом меню.
		/// </summary>
		private class SpecialHotkey
		{
			public string Name;
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
					Name = "NDAReplace",
					Key = (Key)Properties.Settings.Default.SelectedKey,
					Modifiers = (ModifierKeys)Properties.Settings.Default.SelectedModifiers,
					MenuLabel = "NDA Замена",
					Action = () => _pasteService.ReplaceSelectionInExternalApp()
				},
				new SpecialHotkey
				{
					Name = "MarketingMenu",
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
		private List<QuickMenuEntry> BuildQuickMenuEntries(List<BindKeys> binds)
		{
			var entries = binds
				.Select(bind => new QuickMenuEntry
				{
					Name = bind.Name,
					Action = () => _pasteService.PasteText(bind.Text)
				})
				.ToList();

			// Если это сочетание совпадает со специальным хоткеем (NDA-замена, меню рекламы) —
			// прямая регистрация для него невозможна (см. RegisterDefaultHotkeys), поэтому
			// даём доступ к нему отсюда
			var firstBind = binds[0];

			foreach (var special in GetSpecialHotkeys())
			{
				if (special.Key != Key.None && special.Key == firstBind.Key && special.Modifiers == firstBind.Modifiers)
				{
					entries.Add(new QuickMenuEntry
					{
						Name = special.MenuLabel,
						Action = special.Action,
						IsSpecial = true
					});
				}
			}

			return entries;
		}

		#endregion

		#region Экспорт / Импорт

		public void ExportData()
		{
			try
			{
				var saveFileDialog = new SaveFileDialog
				{
					Filter = "JSON Files (*.json)|*.json",
					DefaultExt = "json",
					FileName = "SupporTik_Export.json",
					Title = "Экспорт биндов, групп и настроек"
				};

				if (saveFileDialog.ShowDialog() == true)
				{
					var package = new ExportPackage
					{
						Binds = _bindKeys,
						Groups = _groupInfos,
						Settings = new ExportSettings
						{
							StartMinimized = Properties.Settings.Default.StartMinimized,
							MinimizeToTray = Properties.Settings.Default.MinimizeToTray,
							SelectedKey = Properties.Settings.Default.SelectedKey,
							SelectedModifiers = Properties.Settings.Default.SelectedModifiers
						}
					};

					string json = JsonConvert.SerializeObject(package, Formatting.Indented);
					File.WriteAllText(saveFileDialog.FileName, json);

					_notifyIcon?.ShowBalloonTip(
						"Экспорт",
						"Бинды, группы и настройки успешно сохранены!",
						BalloonIcon.None);
				}
			}
			catch (Exception)
			{
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
				ExportPackage package = null;

				try
				{
					package = JsonConvert.DeserializeObject<ExportPackage>(json);
				}
				catch (JsonException)
				{
					// Старый формат экспорта — просто список биндов, без групп и настроек.
					// package остаётся null, ниже сработает запасной путь.
				}

				List<BindKeys> importedBinds = package?.Binds
					?? JsonConvert.DeserializeObject<List<BindKeys>>(json);

				if (importedBinds == null)
				{
					throw new InvalidOperationException("Файл не похож на экспорт SupporTik");
				}

				_bindKeys = importedBinds;
				_storage.SaveData(_bindKeys);

				if (package?.Groups != null)
				{
					_groupInfos = package.Groups;
					_storage.SaveData(_groupInfos, "groups.json");
				}

				if (package?.Settings != null)
				{
					Properties.Settings.Default.StartMinimized = package.Settings.StartMinimized;
					Properties.Settings.Default.MinimizeToTray = package.Settings.MinimizeToTray;
					Properties.Settings.Default.SelectedKey = package.Settings.SelectedKey;
					Properties.Settings.Default.SelectedModifiers = package.Settings.SelectedModifiers;
					Properties.Settings.Default.Save();
				}

				// Перерегистрируем хоткеи под импортированные бинды/настройки
				RegisterDefaultHotkeys();

				_notifyIcon?.ShowBalloonTip(
					"Импорт",
					"Данные успешно импортированы!",
					BalloonIcon.None);
			}
			catch (Exception)
			{
				_notifyIcon?.ShowBalloonTip(
					"Импорт",
					"Произошла ошибка при импорте!",
					BalloonIcon.None);
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
