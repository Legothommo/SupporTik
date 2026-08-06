using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Pages;
using SupporTik.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SupporTik.ViewModels.Binds
{
	/// <summary>"Папка" — несколько шаблонов на одном сочетании клавиш, показанных одним блоком.</summary>
	public class BindGroupViewModel : ViewModelBase
	{
		private readonly List<BindKeys> _binds;
		private readonly Key _groupKey;
		private readonly ModifierKeys _groupModifiers;
		private readonly IBindsService _bindsService;
		private readonly IMainWindowProvider _mainWindowProvider;

		public event EventHandler RequestReload;

		public string KeysDisplay { get; }
		public ObservableCollection<BindItemViewModel> Items { get; } = new ObservableCollection<BindItemViewModel>();

		private const string KeysCapturePlaceholder = "Нажмите сочетание клавиш...";

		private bool _isEditingKeys;
		public bool IsEditingKeys
		{
			get => _isEditingKeys;
			private set
			{
				if (SetProperty(ref _isEditingKeys, value))
				{
					OnPropertyChanged(nameof(KeysDisplayText));
				}
			}
		}

		/// <summary>То, что реально показывается в плашке сочетания — во время записи заменяется на подсказку.</summary>
		public string KeysDisplayText => IsEditingKeys ? KeysCapturePlaceholder : KeysDisplay;

		private string _groupTitle;
		public string GroupTitle
		{
			get => _groupTitle;
			private set => SetProperty(ref _groupTitle, value);
		}

		private string _groupSubtitle;
		public string GroupSubtitle
		{
			get => _groupSubtitle;
			private set => SetProperty(ref _groupSubtitle, value);
		}

		private bool _hasCustomName;
		public bool HasCustomName
		{
			get => _hasCustomName;
			private set => SetProperty(ref _hasCustomName, value);
		}

		private bool _isRenamingGroup;
		public bool IsRenamingGroup
		{
			get => _isRenamingGroup;
			set => SetProperty(ref _isRenamingGroup, value);
		}

		private string _groupNameInput;
		public string GroupNameInput
		{
			get => _groupNameInput;
			set => SetProperty(ref _groupNameInput, value);
		}

		public ICommand StartRenameCommand { get; }
		public ICommand SaveGroupNameCommand { get; }
		public ICommand CancelRenameCommand { get; }
		public ICommand AddToGroupCommand { get; }

		public ICommand StartEditKeysCommand { get; }
		public ICommand CancelEditKeysCommand { get; }

		public BindGroupViewModel(List<BindKeys> binds, IBindsService bindsService, IMainWindowProvider mainWindowProvider)
		{
			_binds = binds;
			_bindsService = bindsService;
			_mainWindowProvider = mainWindowProvider;

			var firstBind = binds.First();
			_groupKey = firstBind.Key;
			_groupModifiers = firstBind.Modifiers;
			KeysDisplay = KeyExtensions.ToFriendlyShortcut(_groupModifiers, _groupKey);

			foreach (var bind in binds)
			{
				var itemVm = new BindItemViewModel(bind, bindsService);
				itemVm.RequestReload += (s, e) => RequestReload?.Invoke(this, EventArgs.Empty);
				Items.Add(itemVm);
			}

			StartRenameCommand = new RelayCommand(StartRename);
			SaveGroupNameCommand = new RelayCommand(() => CloseNameEditor(save: true));
			CancelRenameCommand = new RelayCommand(() => CloseNameEditor(save: false));
			AddToGroupCommand = new RelayCommand(AddToGroup);

			StartEditKeysCommand = new RelayCommand(StartEditKeys);
			CancelEditKeysCommand = new RelayCommand(CancelEditKeys);

			UpdateTitleDisplay();
		}

		private void UpdateTitleDisplay()
		{
			string customName = _bindsService.GetGroupName(_groupKey, _groupModifiers);
			string countText = $"{_binds.Count} {TemplateWord(_binds.Count)}";

			if (!string.IsNullOrEmpty(customName))
			{
				GroupTitle = customName;
				GroupSubtitle = countText;
				HasCustomName = true;
			}
			else
			{
				GroupTitle = countText;
				GroupSubtitle = string.Empty;
				HasCustomName = false;
			}
		}

		private void StartRename()
		{
			GroupNameInput = _bindsService.GetGroupName(_groupKey, _groupModifiers) ?? string.Empty;
			IsRenamingGroup = true;
		}

		private void CloseNameEditor(bool save)
		{
			if (save)
			{
				_bindsService.SetGroupName(_groupKey, _groupModifiers, GroupNameInput);
				UpdateTitleDisplay();
			}

			IsRenamingGroup = false;
		}

		private static string TemplateWord(int count)
		{
			int mod100 = count % 100;
			int mod10 = count % 10;

			if (mod100 >= 11 && mod100 <= 14) return "шаблонов";
			if (mod10 == 1) return "шаблон";
			if (mod10 >= 2 && mod10 <= 4) return "шаблона";
			return "шаблонов";
		}

		/// <summary>Поиск проверяет общий хоткей группы, её название (папку) и каждый входящий в неё шаблон.</summary>
		public bool MatchesSearch(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				return true;
			}

			if (KeysDisplay.ToLower().Contains(query))
			{
				return true;
			}

			if (HasCustomName && GroupTitle.ToLower().Contains(query))
			{
				return true;
			}

			return _binds.Any(b =>
				b.Name.ToLower().Contains(query) ||
				b.Text.ToLower().Contains(query));
		}

		private void StartEditKeys()
		{
			IsEditingKeys = true;
			_bindsService.StartHotkeyCapture(OnKeysCaptured);
		}

		private void CancelEditKeys()
		{
			if (!IsEditingKeys)
			{
				return;
			}

			_bindsService.CancelHotkeyCapture();
			IsEditingKeys = false;
		}

		/// <summary>
		/// Коллбэк хука клавиатуры — вызывается синхронно изнутри низкоуровневого хука (см.
		/// HotkeyService.HookCallback и BindItemViewModel.OnKeysCaptured), поэтому тяжёлую
		/// часть откладываем через Dispatcher. Сочетание общее для всей группы — меняем его
		/// сразу у всех шаблонов группы, а не только у одного.
		/// </summary>
		private void OnKeysCaptured(Key key, ModifierKeys modifiers)
		{
			Application.Current.Dispatcher.InvokeAsync(() =>
			{
				foreach (var bind in _binds)
				{
					bind.Key = key;
					bind.Modifiers = modifiers;
				}

				_bindsService.SaveAndReRegister();

				IsEditingKeys = false;
				RequestReload?.Invoke(this, EventArgs.Empty);
			});
		}

		private void AddToGroup()
		{
			bool wasPaused = !_bindsService.IsPasteEnabled;
			_bindsService.PausePaste();

			var addWindow = new BindCreateWindow(_bindsService, _groupKey, _groupModifiers) { Owner = _mainWindowProvider.Current };

			if (addWindow.ShowDialog() == true)
			{
				_bindsService.AddBind(addWindow.ResultBind);
			}

			if (!wasPaused)
			{
				_bindsService.ResumePaste();
			}

			RequestReload?.Invoke(this, EventArgs.Empty);
		}
	}
}
