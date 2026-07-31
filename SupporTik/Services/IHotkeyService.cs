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
