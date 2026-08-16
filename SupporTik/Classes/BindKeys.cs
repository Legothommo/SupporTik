using System;
using System.Windows.Input;

namespace SupporTik.Classes
{
	public class BindKeys
	{
		public string Name { get; set; }
		public Key Key { get; set; }
		public ModifierKeys Modifiers { get; set; }
		public string Text { get; set; }
	}
}
