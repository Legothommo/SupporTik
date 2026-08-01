using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Services
{
	public interface IHotkeyService
	{
		/// <summary>
		/// Пока true, хук не перехватывает и не "глотает" нажатия — используется, пока
		/// пользователь захватывает новое сочетание клавиш в UI (иначе хук перехватит
		/// нажатие раньше, чем оно дойдёт до окна приложения).
		/// </summary>
		bool IsSuspended { get; set; }

		void RegisterHotkey(string name, Key key, ModifierKeys modifiers, Action action);
		void UnregisterHotkey(string name);
		void UnregisterAll();
	}

	public interface ITextPasteService
	{
		bool IsPaused { get; set; }
		Task PasteText(string text);
		Task ReplaceSelectionInExternalApp();
	}
}
