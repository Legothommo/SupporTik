using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SupporTik.ViewModels
{
	public class MarketingWindowViewModel : ViewModelBase
	{
		private readonly IMarketingCampaignService _campaignService;
		private readonly INotificationService _notificationService;

		// Полный список последнего поиска — фильтр по статусу просто перерисовывает
		// карточки из этого списка, заново парсить страницу не нужно
		private readonly List<MarketingItem> _allItems = new List<MarketingItem>();

		private CancellationTokenSource _filterCts;

		public ObservableCollection<MarketingItemViewModel> Items { get; } = new ObservableCollection<MarketingItemViewModel>();

		private string _uid = string.Empty;
		public string Uid
		{
			get => _uid;
			set => SetProperty(ref _uid, value);
		}

		private bool _searchRoles;
		public bool SearchRoles
		{
			get => _searchRoles;
			set => SetProperty(ref _searchRoles, value);
		}

		private bool _isSearchUiVisible;
		public bool IsSearchUiVisible
		{
			get => _isSearchUiVisible;
			set => SetProperty(ref _isSearchUiVisible, value);
		}

		private bool _filterWaiting = true;
		public bool FilterWaiting
		{
			get => _filterWaiting;
			set { if (SetProperty(ref _filterWaiting, value)) _ = ApplyFilterAsync(); }
		}

		private bool _filterActive = true;
		public bool FilterActive
		{
			get => _filterActive;
			set { if (SetProperty(ref _filterActive, value)) _ = ApplyFilterAsync(); }
		}

		private bool _filterFinished = true;
		public bool FilterFinished
		{
			get => _filterFinished;
			set { if (SetProperty(ref _filterFinished, value)) _ = ApplyFilterAsync(); }
		}

		private string _searchButtonLabel = "Поиск";
		public string SearchButtonLabel
		{
			get => _searchButtonLabel;
			private set => SetProperty(ref _searchButtonLabel, value);
		}

		private bool _isSearching;
		public bool IsSearching
		{
			get => _isSearching;
			private set => SetProperty(ref _isSearching, value);
		}

		private const string ResultsCountLabel = "Количество рекламных кампаний: ";

		private string _resultsCountText = ResultsCountLabel + "0";
		public string ResultsCountText
		{
			get => _resultsCountText;
			private set => SetProperty(ref _resultsCountText, value);
		}

		public ICommand SearchCommand { get; }
		public ICommand CopySelectedCommand { get; }

		public MarketingWindowViewModel(IMarketingCampaignService campaignService, INotificationService notificationService)
		{
			_campaignService = campaignService;
			_notificationService = notificationService;

			SearchCommand = new AsyncRelayCommand(SearchAsync);
			CopySelectedCommand = new RelayCommand(CopySelected);
		}

		private async Task SearchAsync()
		{
			string uid = (Uid ?? string.Empty).Trim();

			if (string.IsNullOrEmpty(uid))
			{
				_notificationService.ShowBalloon("Предупреждение", "Введите UID пользователя.", isWarning: true);
				return;
			}

			IsSearching = true;
			SearchButtonLabel = "Поиск...";

			try
			{
				var progress = new Progress<string>(text => SearchButtonLabel = text);
				List<MarketingItem> items = await _campaignService.SearchAsync(uid, SearchRoles, progress);

				_allItems.Clear();
				_allItems.AddRange(items);
				await ApplyFilterAsync();
			}

			catch (Exception ex)
			{
				_notificationService.ShowBalloon("Ошибка", ex.Message, isWarning: true);
			}
			finally
			{
				SearchButtonLabel = "Поиск";
				IsSearching = false;
			}
		}

		private async Task ApplyFilterAsync()
		{
			// Быстрое переключение чекбоксов фильтра запускает новый проход раньше, чем
			// предыдущий (он "тикает" по Task.Delay между партиями) успел закончиться —
			// без отмены оба одновременно дёргали бы Items.Clear()/Add() вперемешку
			_filterCts?.Cancel();
			var cts = new CancellationTokenSource();
			_filterCts = cts;
			CancellationToken token = cts.Token;

			Items.Clear();
			int count = 0;
			ResultsCountText = ResultsCountLabel + count;

			try
			{
				int i = 0;

				foreach (var item in _allItems)
				{
					if (IsStatusVisible(item.Status))
					{
						Items.Add(new MarketingItemViewModel(item));
						count++;
						ResultsCountText = ResultsCountLabel + count;
					}

					if (++i % 5 == 0)
					{
						await Task.Delay(1, token);
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Более свежий вызов ApplyFilterAsync уже отменил этот проход — ничего страшного
			}
		}

		private bool IsStatusVisible(string status)
		{
			switch (status)
			{
				case "Ожидает оплаты": return FilterWaiting;
				case "Активна": return FilterActive;
				case "Завершена": return FilterFinished;
				default: return true; // неизвестный статус — показываем, чтобы не терять данные
			}
		}

		private void CopySelected()
		{
			var permalinks = Items
				.Where(i => i.IsSelected)
				.Select(i => i.RawPermalink)
				.Where(p => !string.IsNullOrEmpty(p))
				.ToList();

			if (permalinks.Count == 0)
			{
				_notificationService.ShowBalloon("Предупреждение", "Отметьте хотя бы одну карточку.", isWarning: true);
				return;
			}

			Clipboard.SetText(string.Join(", ", permalinks));

			_notificationService.ShowBalloon("Скопировано", $"Пермалинков в буфере: {permalinks.Count}", isWarning: false);
		}
	}
}
