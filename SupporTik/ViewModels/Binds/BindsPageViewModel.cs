using SupporTik.Mvvm;
using SupporTik.Pages;
using SupporTik.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;

namespace SupporTik.ViewModels.Binds
{
	public class BindsPageViewModel : ViewModelBase
	{
		private readonly IBindsService _bindsService;
		private readonly IMainWindowProvider _mainWindowProvider;

		public ObservableCollection<object> Rows { get; } = new ObservableCollection<object>();
		public ICollectionView RowsView { get; }

		private string _searchQuery = string.Empty;
		public string SearchQuery
		{
			get => _searchQuery;
			set
			{
				if (SetProperty(ref _searchQuery, value))
				{
					RowsView.Refresh();
				}
			}
		}

		private bool _isPasteEnabled;
		public bool IsPasteEnabled
		{
			get => _isPasteEnabled;
			private set
			{
				if (SetProperty(ref _isPasteEnabled, value))
				{
					OnPropertyChanged(nameof(StatusText));
				}
			}
		}

		public string StatusText => IsPasteEnabled ? "Перехват клавиш активен" : "Перехват клавиш выключен";

		public ICommand AddBindCommand { get; }

		public BindsPageViewModel(IBindsService bindsService, IMainWindowProvider mainWindowProvider)
		{
			_bindsService = bindsService;
			_mainWindowProvider = mainWindowProvider;

			RowsView = CollectionViewSource.GetDefaultView(Rows);
			RowsView.Filter = FilterRow;

			AddBindCommand = new RelayCommand(AddBind);

			IsPasteEnabled = _bindsService.IsPasteEnabled;
			ReloadRows();
		}

		/// <summary>Вызывается извне (трей-меню) при переключении паузы вставки — как раньше UpdateStatus.</summary>
		public void UpdateStatus(bool isPaused)
		{
			IsPasteEnabled = !isPaused;
		}

		private bool FilterRow(object rowObj)
		{
			string query = (SearchQuery ?? string.Empty).Trim().ToLower();

			if (rowObj is BindItemViewModel item)
			{
				return item.MatchesSearch(query);
			}

			if (rowObj is BindGroupViewModel group)
			{
				return group.MatchesSearch(query);
			}

			return true;
		}

		private void AddBind()
		{
			// Запоминаем состояние на случай, если пользователь уже поставил перехват на паузу
			// вручную (через трей) — диалог не должен снимать эту паузу за него
			bool wasPaused = !_bindsService.IsPasteEnabled;
			_bindsService.PausePaste();

			var addWindow = new BindCreateWindow(_bindsService) { Owner = _mainWindowProvider.Current };

			if (addWindow.ShowDialog() == true)
			{
				_bindsService.AddBind(addWindow.ResultBind);

				// Перестраиваем список ПОСЛЕ AddBind (внутри он уже сохранил и
				// перерегистрировал хоткеи) — иначе новая карточка ссылалась бы на
				// объект, "осиротевший" после перезагрузки списка биндов с диска
				ReloadRows();
			}

			if (!wasPaused)
			{
				_bindsService.ResumePaste();
			}
		}

		private void ReloadRows()
		{
			foreach (var row in Rows)
			{
				UnsubscribeReload(row);
			}
			Rows.Clear();

			var groups = _bindsService.GetBinds()
				.GroupBy(b => new { b.Key, b.Modifiers })
				.OrderBy(g => g.Key.Modifiers)
				.ThenBy(g => g.Key.Key);

			foreach (var group in groups)
			{
				var binds = group.ToList();

				if (binds.Count == 1)
				{
					var itemVm = new BindItemViewModel(binds[0], _bindsService);
					itemVm.RequestReload += OnChildRequestReload;
					Rows.Add(itemVm);
				}
				else
				{
					var groupVm = new BindGroupViewModel(binds, _bindsService, _mainWindowProvider);
					groupVm.RequestReload += OnChildRequestReload;
					Rows.Add(groupVm);
				}
			}
		}

		private void UnsubscribeReload(object row)
		{
			if (row is BindItemViewModel item)
			{
				item.RequestReload -= OnChildRequestReload;
			}
			else if (row is BindGroupViewModel group)
			{
				group.RequestReload -= OnChildRequestReload;
			}
		}

		private void OnChildRequestReload(object sender, EventArgs e) => ReloadRows();
	}
}
