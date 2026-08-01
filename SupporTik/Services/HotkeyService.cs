using Hardcodet.Wpf.TaskbarNotification;
using NHotkey;
using NHotkey.Wpf;
using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Windows.Input;

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
                App._notifyIcon?.ShowBalloonTip(
                    "Ошибка",
                    $"Сочетание клавиш {KeyExtensions.ToFriendlyShortcut(modifiers, key)} уже используется сторонней программой!",
                    BalloonIcon.Warning);
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