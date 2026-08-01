using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Регистрирует горячие клавиши через низкоуровневый хук клавиатуры (WH_KEYBOARD_LL),
	/// а не через Win32 RegisterHotKey. RegisterHotKey работает по принципу "кто первый
	/// зарегистрировал — тот и получает событие", поэтому если сочетание уже занято другим
	/// приложением, наше приложение просто не смогло бы среагировать. Хук же перехватывает
	/// нажатие раньше, чем оно уходит дальше по системе, и "глотает" его (не передаёт другим
	/// приложениям и в очередь сообщений), поэтому SupporTik всегда получает приоритет.
	/// </summary>
	public class HotkeyService : IHotkeyService, IDisposable
	{
		#region WinAPI

		private const int WH_KEYBOARD_LL = 13;
		private const int WM_KEYDOWN = 0x0100;
		private const int WM_SYSKEYDOWN = 0x0104;
		private const int WM_KEYUP = 0x0101;
		private const int WM_SYSKEYUP = 0x0105;

		private const int VK_LSHIFT = 0xA0;
		private const int VK_RSHIFT = 0xA1;
		private const int VK_LCONTROL = 0xA2;
		private const int VK_RCONTROL = 0xA3;
		private const int VK_LMENU = 0xA4;
		private const int VK_RMENU = 0xA5;
		private const int VK_LWIN = 0x5B;
		private const int VK_RWIN = 0x5C;

		private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential)]
		private struct KBDLLHOOKSTRUCT
		{
			public uint vkCode;
			public uint scanCode;
			public uint flags;
			public uint time;
			public IntPtr dwExtraInfo;
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("user32.dll")]
		private static extern short GetKeyState(int nVirtKey);

		#endregion

		private class HotkeyEntry
		{
			public Key Key;
			public ModifierKeys Modifiers;
			public Action Action;
		}

		private readonly object _lock = new object();
		private readonly Dictionary<string, HotkeyEntry> _hotkeysByName = new Dictionary<string, HotkeyEntry>();

		// Комбинации, которые сейчас "зажаты" — чтобы автоповтор WM_KEYDOWN не запускал
		// действие повторно, пока клавиша просто удерживается (как ведёт себя RegisterHotKey)
		private readonly HashSet<Key> _activeTriggerKeys = new HashSet<Key>();

		// Держим ссылку на делегат, чтобы GC его не собрал, пока хук установлен
		private readonly LowLevelKeyboardProc _hookProc;
		private IntPtr _hookHandle = IntPtr.Zero;

		public bool IsSuspended { get; set; }

		public HotkeyService()
		{
			_hookProc = HookCallback;
			_hookHandle = SetHook(_hookProc);
		}

		private static IntPtr SetHook(LowLevelKeyboardProc proc)
		{
			using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
			using (var curModule = curProcess.MainModule)
			{
				return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
			}
		}

		private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0 && !IsSuspended)
			{
				int msg = wParam.ToInt32();
				var hookStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
				Key key = KeyInterop.KeyFromVirtualKey((int)hookStruct.vkCode);

				bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
				bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

				if ((isDown || isUp) && !IsModifierKey(key))
				{
					if (isUp)
					{
						lock (_lock)
						{
							_activeTriggerKeys.Remove(key);
						}
					}
					else
					{
						ModifierKeys mods = GetCurrentModifiers();
						HotkeyEntry match = FindMatch(key, mods);

						if (match != null)
						{
							bool alreadyHeld;
							lock (_lock)
							{
								alreadyHeld = !_activeTriggerKeys.Add(key);
							}

							if (!alreadyHeld)
							{
								match.Action?.Invoke();
							}

							// Не пропускаем нажатие дальше — ни в текущее окно, ни в другие
							// приложения. Так наши бинды побеждают при совпадении хоткеев.
							return (IntPtr)1;
						}
					}
				}
			}

			return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
		}

		private HotkeyEntry FindMatch(Key key, ModifierKeys mods)
		{
			lock (_lock)
			{
				foreach (var entry in _hotkeysByName.Values)
				{
					if (entry.Key == key && entry.Modifiers == mods)
					{
						return entry;
					}
				}
			}

			return null;
		}

		private static bool IsModifierKey(Key key)
		{
			switch (key)
			{
				case Key.LeftCtrl:
				case Key.RightCtrl:
				case Key.LeftAlt:
				case Key.RightAlt:
				case Key.LeftShift:
				case Key.RightShift:
				case Key.LWin:
				case Key.RWin:
					return true;
				default:
					return false;
			}
		}

		private static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

		private static ModifierKeys GetCurrentModifiers()
		{
			ModifierKeys mods = ModifierKeys.None;

			if (IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL)) mods |= ModifierKeys.Control;
			if (IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU)) mods |= ModifierKeys.Alt;
			if (IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT)) mods |= ModifierKeys.Shift;
			if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) mods |= ModifierKeys.Windows;

			return mods;
		}

		public void RegisterHotkey(string name, Key key, ModifierKeys modifiers, Action action)
		{
			lock (_lock)
			{
				_hotkeysByName[name] = new HotkeyEntry { Key = key, Modifiers = modifiers, Action = action };
			}
		}

		public void UnregisterHotkey(string name)
		{
			lock (_lock)
			{
				_hotkeysByName.Remove(name);
			}
		}

		public void UnregisterAll()
		{
			lock (_lock)
			{
				_hotkeysByName.Clear();
				_activeTriggerKeys.Clear();
			}
		}

		public void Dispose()
		{
			if (_hookHandle != IntPtr.Zero)
			{
				UnhookWindowsHookEx(_hookHandle);
				_hookHandle = IntPtr.Zero;
			}
		}
	}
}
