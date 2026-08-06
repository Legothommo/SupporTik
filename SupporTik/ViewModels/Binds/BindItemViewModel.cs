using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Windows;
using System.Windows.Input;

namespace SupporTik.ViewModels.Binds
{
	/// <summary>
	/// Одна карточка одиночного бинда (сочетание клавиш без соседей на нём же).
	/// Название и текст редактируются прямо в карточке (инлайн) — окно BindCreateWindow
	/// используется только для создания новых биндов, не для правки существующих.
	/// </summary>
	public class BindItemViewModel : ViewModelBase
	{
		private readonly BindKeys _bind;
		private readonly IBindsService _bindsService;

		/// <summary>Список биндов на диске мог перезагрузиться (после удаления) — просим страницу перестроить всё.</summary>
		public event EventHandler RequestReload;

		private string _name;
		public string Name
		{
			get => _name;
			private set => SetProperty(ref _name, value);
		}

		private string _text;
		public string Text
		{
			get => _text;
			private set => SetProperty(ref _text, value);
		}

		public string KeysDisplay => KeyExtensions.ToFriendlyShortcut(_bind.Modifiers, _bind.Key);

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

		private bool _isEditingName;
		public bool IsEditingName
		{
			get => _isEditingName;
			set => SetProperty(ref _isEditingName, value);
		}

		private string _nameInput;
		public string NameInput
		{
			get => _nameInput;
			set => SetProperty(ref _nameInput, value);
		}

		private bool _isEditingText;
		public bool IsEditingText
		{
			get => _isEditingText;
			set => SetProperty(ref _isEditingText, value);
		}

		private string _textInput;
		public string TextInput
		{
			get => _textInput;
			set => SetProperty(ref _textInput, value);
		}

		public ICommand StartEditNameCommand { get; }
		public ICommand SaveNameCommand { get; }
		public ICommand CancelEditNameCommand { get; }

		public ICommand StartEditTextCommand { get; }
		public ICommand SaveTextCommand { get; }
		public ICommand CancelEditTextCommand { get; }

		public ICommand StartEditKeysCommand { get; }
		public ICommand CancelEditKeysCommand { get; }

		public ICommand DeleteCommand { get; }

		public BindItemViewModel(BindKeys bind, IBindsService bindsService)
		{
			_bind = bind;
			_bindsService = bindsService;
			_name = bind.Name;
			_text = bind.Text;

			StartEditNameCommand = new RelayCommand(StartEditName);
			SaveNameCommand = new RelayCommand(SaveName);
			CancelEditNameCommand = new RelayCommand(() => IsEditingName = false);

			StartEditTextCommand = new RelayCommand(StartEditText);
			SaveTextCommand = new RelayCommand(SaveText);
			CancelEditTextCommand = new RelayCommand(() => IsEditingText = false);

			StartEditKeysCommand = new RelayCommand(StartEditKeys);
			CancelEditKeysCommand = new RelayCommand(CancelEditKeys);

			DeleteCommand = new RelayCommand(Delete);
		}

		public bool MatchesSearch(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				return true;
			}

			return KeysDisplay.ToLower().Contains(query)
				|| (Name ?? string.Empty).ToLower().Contains(query)
				|| (Text ?? string.Empty).ToLower().Contains(query);
		}

		private void StartEditName()
		{
			NameInput = _bind.Name;
			IsEditingName = true;
		}

		private void SaveName()
		{
			// Защита от двойного срабатывания: Enter уже сохранил и закрыл редактор —
			// последующий LostFocus (TextBox скрылся и потерял фокус) не должен сохранять повторно
			if (!IsEditingName)
			{
				return;
			}

			string trimmed = (NameInput ?? string.Empty).Trim();
			if (!string.IsNullOrEmpty(trimmed) && trimmed != _bind.Name)
			{
				_bind.Name = trimmed;
				Name = trimmed;
				_bindsService.SaveBindsOnly();
			}

			IsEditingName = false;
		}

		private void StartEditText()
		{
			TextInput = _bind.Text;
			IsEditingText = true;
		}

		private void SaveText()
		{
			if (!IsEditingText)
			{
				return;
			}

			if (!string.IsNullOrEmpty(TextInput) && TextInput != _bind.Text)
			{
				_bind.Text = TextInput;
				Text = TextInput;
				_bindsService.SaveBindsOnly();
			}

			IsEditingText = false;
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
		/// Коллбэк хука клавиатуры (см. HotkeyService.HookCallback) — вызывается СИНХРОННО
		/// изнутри низкоуровневого хука, поэтому тяжёлую часть (диск, перерегистрация всех
		/// хоткеев, пересборка списка карточек через RequestReload) откладываем через
		/// Dispatcher, а не делаем прямо тут — иначе можно надолго заблокировать хук, и
		/// Windows принудительно снимет его по таймауту.
		/// </summary>
		private void OnKeysCaptured(Key key, ModifierKeys modifiers)
		{
			Application.Current.Dispatcher.InvokeAsync(() =>
			{
				_bind.Key = key;
				_bind.Modifiers = modifiers;
				_bindsService.SaveAndReRegister();

				IsEditingKeys = false;
				RequestReload?.Invoke(this, EventArgs.Empty);
			});
		}

		private void Delete()
		{
			_bindsService.DeleteBind(_bind);
			RequestReload?.Invoke(this, EventArgs.Empty);
		}
	}
}
