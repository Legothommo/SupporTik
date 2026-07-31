using System;
using System.Collections.Generic;
using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;

namespace SupporTik.Services
{
	public class HotkeyService : IHotkeyService
	{
		private readonly HashSet<string> _registeredKeys = new HashSet<string>();

		public void RegisterHotkey(string name, Key key, ModifierKeys modifiers, Action action)
		{
			try
			{
				HotkeyManager.Current.AddOrReplace(name, key, modifiers, (sender, e) =>
				{
					action?.Invoke();
					e.Handled = true;
				});

				_registeredKeys.Add(name);
			}
			catch (HotkeyAlreadyRegisteredException)
			{
				// Игнорируем, если хоткей уже зарегистрирован в системе
			}
		}

		public void UnregisterHotkey(string name)
		{
			if (_registeredKeys.Contains(name))
			{
				HotkeyManager.Current.Remove(name);
				_registeredKeys.Remove(name);
			}
		}

		public void UnregisterAll()
		{
			foreach (var name in _registeredKeys)
			{
				HotkeyManager.Current.Remove(name);
			}
			_registeredKeys.Clear();
		}
	}
}