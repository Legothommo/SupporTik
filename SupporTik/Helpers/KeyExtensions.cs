using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Classes
{
	public class KeyExtensions
	{
		public static string ToFriendlyShortcut(ModifierKeys modifiers, Key key)
		{
			var parts = new List<string>();

			if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
			if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
			if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
			if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

			parts.Add(ToFriendlyString(key));

			return string.Join(" + ", parts);
		}
		public static string ToFriendlyString(Key key)
		{
			switch (key)
			{
				// Верхний цифровой ряд (D0 - D9)
				case Key.D0: return "0";
				case Key.D1: return "1";
				case Key.D2: return "2";
				case Key.D3: return "3";
				case Key.D4: return "4";
				case Key.D5: return "5";
				case Key.D6: return "6";
				case Key.D7: return "7";
				case Key.D8: return "8";
				case Key.D9: return "9";

				// Numpad цифры
				case Key.NumPad0: return "Num 0";
				case Key.NumPad1: return "Num 1";
				case Key.NumPad2: return "Num 2";
				case Key.NumPad3: return "Num 3";
				case Key.NumPad4: return "Num 4";
				case Key.NumPad5: return "Num 5";
				case Key.NumPad6: return "Num 6";
				case Key.NumPad7: return "Num 7";
				case Key.NumPad8: return "Num 8";
				case Key.NumPad9: return "Num 9";

				// Частые спецсимволы и модификаторы
				case Key.OemQuestion: return "/";
				case Key.OemPlus: return "+";
				case Key.OemMinus: return "-";
				case Key.OemPeriod: return ".";
				case Key.OemComma: return ",";
				case Key.Oem1: return ";";
				case Key.Oem3: return "~";
				case Key.Capital: return "Caps Lock";
				case Key.Back: return "Backspace";
				case Key.Escape: return "Esc";

				// Все остальные клавиши (A-Z, F1-F12 и т.д.) выводятся как есть
				default:
					return key.ToString();
			}
		}
	}
}
