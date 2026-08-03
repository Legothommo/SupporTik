using System;

namespace SupporTik.Classes
{
	/// <summary>
	/// Один пункт всплывающего меню QuickTextWindow. Окно ничего не знает о биндах,
	/// сервисах или настройках — оно просто рисует список таких пунктов и вызывает
	/// Action по клику. Вся логика "что нажать и какой сервис вызвать" остаётся в App.
	/// </summary>
	public class QuickMenuEntry
	{
		public string Name { get; set; }
		public Action Action { get; set; }

		/// <summary>Отделяется от остальных пунктов разделителем (например, NDA-замена).</summary>
		public bool IsSpecial { get; set; }
	}
}
