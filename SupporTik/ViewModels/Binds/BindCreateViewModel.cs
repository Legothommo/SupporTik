using SupporTik.Classes;
using SupporTik.Mvvm;
using System;
using System.Windows;
using System.Windows.Input;

namespace SupporTik.ViewModels.Binds
{
	/// <summary>Окно только для создания нового бинда — редактирование существующих происходит инлайн в карточках.</summary>
	public class BindCreateViewModel : ViewModelBase
	{
		/// <summary>true — сохранение прошло успешно (диалог должен закрыться с DialogResult = true).</summary>
		public event EventHandler<bool> CloseRequested;

		private string _name = string.Empty;
		public string Name
		{
			get => _name;
			set => SetProperty(ref _name, value);
		}

		private string _text = string.Empty;
		public string Text
		{
			get => _text;
			set => SetProperty(ref _text, value);
		}

		public string HeaderText => "Добавление бинда";

		private string _hotkeyDisplayText = "Нажмите, чтобы задать хоткей...";
		public string HotkeyDisplayText
		{
			get => _hotkeyDisplayText;
			set => SetProperty(ref _hotkeyDisplayText, value);
		}

		public Key SelectedKey { get; private set; } = Key.None;
		public ModifierKeys SelectedModifiers { get; private set; } = ModifierKeys.None;

		public BindKeys ResultBind { get; private set; }

		public ICommand SaveCommand { get; }
		public ICommand CancelCommand { get; }

		/// <param name="presetKey">
		/// Готовое сочетание клавиш — например, "+ Добавить шаблон" внутри группы биндов
		/// с общим хоткеем: пользователь его не выбирает, оно уже задано группой.
		/// </param>
		public BindCreateViewModel(Key? presetKey = null, ModifierKeys presetModifiers = ModifierKeys.None)
		{
			if (presetKey.HasValue)
			{
				SelectedKey = presetKey.Value;
				SelectedModifiers = presetModifiers;
				_hotkeyDisplayText = KeyExtensions.ToFriendlyShortcut(presetModifiers, presetKey.Value);
			}

			SaveCommand = new RelayCommand(Save);
			CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
		}

		public void OnHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			SelectedKey = key;
			SelectedModifiers = modifiers;
			HotkeyDisplayText = KeyExtensions.ToFriendlyShortcut(modifiers, key);
		}

		private void Save()
		{
			string name = (Name ?? string.Empty).Trim();
			string text = Text ?? string.Empty;

			if (string.IsNullOrWhiteSpace(name))
			{
				MessageBox.Show("Введите название бинда!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (SelectedKey == Key.None)
			{
				MessageBox.Show("Задайте сочетание клавиш!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (string.IsNullOrEmpty(text))
			{
				MessageBox.Show("Введите текст для автовставки!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			ResultBind = new BindKeys
			{
				Name = name,
				Key = SelectedKey,
				Modifiers = SelectedModifiers,
				Text = text
			};

			CloseRequested?.Invoke(this, true);
		}
	}
}
