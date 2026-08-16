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
		void RegisterHotkey(Key key, ModifierKeys modifiers, Action action);
		void UnregisterAll();

		/// <summary>
		/// Начинает захват следующего нажатого сочетания клавиш напрямую через системный
		/// хук — так нажатие достаётся нам раньше, чем его успеет перехватить чужая
		/// программа через RegisterHotKey, и можно назначить даже сочетание, уже занятое
		/// сторонним приложением. Захват одноразовый: после первого подходящего нажатия
		/// (не одиночного модификатора) вызывается onCaptured и захват завершается сам.
		/// </summary>
		void StartCapture(Action<Key, ModifierKeys> onCaptured);

		/// <summary>Отменяет активный захват без результата (например, элемент потерял фокус).</summary>
		void CancelCapture();
	}

	public interface ITextPasteService
	{
		bool IsPaused { get; set; }

		/// <summary>Возобновляет вставку текста и NDA-замену.</summary>
		void Start();

		/// <summary>Временно приостанавливает вставку текста и NDA-замену.</summary>
		void Pause();

		Task PasteText(string text);
		Task ReplaceSelectionInExternalApp();
	}
}
