using System;
using System.Windows.Input;
using SupporTik.Classes;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	/// <summary>Один пункт всплывающего меню — обёртка над QuickMenuEntry для биндинга.</summary>
	public class QuickMenuEntryViewModel : ViewModelBase
	{
		public string DisplayName { get; }
		public bool IsSpecial { get; }

		/// <summary>Разделитель рисуется прямо над этим пунктом — первым из "особых" (NDA/маркетинг).</summary>
		public bool ShowSeparatorAbove { get; }

		public ICommand ExecuteCommand { get; }

		public QuickMenuEntryViewModel(QuickMenuEntry entry, bool showSeparatorAbove, Action onInvoked)
		{
			// Обрезаем длинное имя — как и раньше, в SetEntries
			DisplayName = entry.Name.Length > 15 ? entry.Name.Substring(0, 14) + "..." : entry.Name;
			IsSpecial = entry.IsSpecial;
			ShowSeparatorAbove = showSeparatorAbove;

			ExecuteCommand = new RelayCommand(() =>
			{
				// Сначала скрываем меню (и тем самым возвращаем фокус приложению, в которое
				// вставляем) и только потом запускаем само действие — если сделать наоборот,
				// действие может выполниться синхронно быстрее, чем окно успеет скрыться,
				// и Ctrl+V/Ctrl+C улетит ещё в это всплывающее меню
				onInvoked?.Invoke();
				entry.Action?.Invoke();
			});
		}
	}
}
