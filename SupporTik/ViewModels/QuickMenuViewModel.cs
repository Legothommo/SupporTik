using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SupporTik.Classes;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	public class QuickMenuViewModel : ViewModelBase
	{
		public ObservableCollection<QuickMenuEntryViewModel> Entries { get; } = new ObservableCollection<QuickMenuEntryViewModel>();

		private string _groupTitle = string.Empty;
		public string GroupTitle
		{
			get => _groupTitle;
			private set => SetProperty(ref _groupTitle, value);
		}

		private bool _isGroupTitleVisible;
		public bool IsGroupTitleVisible
		{
			get => _isGroupTitleVisible;
			private set => SetProperty(ref _isGroupTitleVisible, value);
		}

		/// <summary>Пункт меню отработал — окно должно скрыться (см. QuickTextWindow).</summary>
		public event EventHandler EntryInvoked;

		/// <summary>
		/// Окно ничего не знает, что стоит за каждым пунктом — это может быть вставка
		/// шаблона, NDA-замена или что угодно ещё; вся эта логика собирается в
		/// App.BuildQuickMenuEntries и приходит сюда уже готовым списком.
		/// </summary>
		public void SetEntries(string groupTitle, List<QuickMenuEntry> entries)
		{
			if (string.IsNullOrEmpty(groupTitle))
			{
				IsGroupTitleVisible = false;
				GroupTitle = string.Empty;
			}
			else
			{
				GroupTitle = "📁 " + groupTitle;
				IsGroupTitleVisible = true;
			}

			Entries.Clear();

			bool separatorAdded = false;

			foreach (QuickMenuEntry entry in entries)
			{
				bool showSeparator = entry.IsSpecial && !separatorAdded;
				if (showSeparator)
				{
					separatorAdded = true;
				}

				Entries.Add(new QuickMenuEntryViewModel(entry, showSeparator, () => EntryInvoked?.Invoke(this, EventArgs.Empty)));
			}
		}
	}
}
