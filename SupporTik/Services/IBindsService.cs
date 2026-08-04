using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Точка входа для ViewModel'ей фичи "Бинды" — скрывает механику хранения,
	/// перерегистрации хоткеев и паузы вставки за одним интерфейсом.
	/// </summary>
	public interface IBindsService
	{
		IReadOnlyList<BindKeys> GetBinds();
		void AddBind(BindKeys bind);
		void DeleteBind(BindKeys bind);

		/// <summary>
		/// Сохраняет текущий список и перерегистрирует хоткеи. Вызывать при изменениях,
		/// которые могут повлиять на структуру регистрации (добавление/удаление бинда).
		/// </summary>
		void SaveAndReRegister();

		/// <summary>
		/// Только сохраняет список на диск, без перерегистрации хоткеев. Для инлайн-правки
		/// названия/текста существующего бинда — сочетание клавиш не меняется, поэтому
		/// перестраивать хоткеи не нужно, а сам ReRegister пересоздал бы объекты в списке
		/// и "осиротил" бы ViewModel, который их сейчас редактирует.
		/// </summary>
		void SaveBindsOnly();

		string GetGroupName(Key key, ModifierKeys modifiers);
		void SetGroupName(Key key, ModifierKeys modifiers, string name);

		bool IsPasteEnabled { get; }
		void PausePaste();
		void ResumePaste();

		void StartHotkeyCapture(Action<Key, ModifierKeys> onCaptured);
		void CancelHotkeyCapture();
	}
}
