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
		private readonly IUpsaleService _upsaleService;

		/// <summary>
		/// Авторизация в DataLens — отдельный сайт (datalens.yandex-team.ru) со своей
		/// сессией, показ окна логина требует WebView2 (View-специфичная логика),
		/// поэтому передаётся сюда как делегат, а не делается напрямую из VM.
		/// </summary>
		private readonly Func<Task<(string CookieHeader, string CsrfToken)>> _ensureDataLensAuth;

		/// <summary>
		/// Куки/CSRF-токен уже авторизованной страницы yandex.ru/business — для прямых
		/// API-запросов за списком кампаний. Тем же приёмом, что и для DataLens (делегат,
		/// а не прямой доступ к WebView2 из VM).
		/// </summary>
		private readonly Func<Task<YandexBusinessAuth>> _getYandexBusinessAuth;

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

		private bool _isSearchUiVisible;
		public bool IsSearchUiVisible
		{
			get => _isSearchUiVisible;
			set => SetProperty(ref _isSearchUiVisible, value);
		}

		/// <summary>
		/// Уникальные статусы из последнего поиска — независимые галки, можно выбрать
		/// сразу несколько. Список строится заново из реально спарсенных данных
		/// (см. RebuildStatusFilters), а не захардкожен заранее.
		/// </summary>
		public ObservableCollection<StatusFilterOption> StatusFilters { get; } = new ObservableCollection<StatusFilterOption>();

		private string _statusFilterSummary;
		public string StatusFilterSummary
		{
			get => _statusFilterSummary;
			private set => SetProperty(ref _statusFilterSummary, value);
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
		private bool _nothingToSearch;
		public bool NothingToSearch
		{
			get => _nothingToSearch;
			private set => SetProperty(ref _nothingToSearch, value);
		}

		private const string ResultsCountLabel = "Количество рекламных кампаний: ";

		private string _resultsCountText = ResultsCountLabel + "0";
		public string ResultsCountText
		{
			get => _resultsCountText;
			private set => SetProperty(ref _resultsCountText, value);
		}

		private string _upsaleButtonLabel = "Проверить апсейлы";
		public string UpsaleButtonLabel
		{
			get => _upsaleButtonLabel;
			private set => SetProperty(ref _upsaleButtonLabel, value);
		}

		private bool _selectAll;
		/// <summary>
		/// Не синхронизируется обратно, если пользователь вручную снял галку с одной
		/// карточки — это просто переключатель "отметить/снять все", а не отражение
		/// текущего состояния выбора.
		/// </summary>
		public bool SelectAll
		{
			get => _selectAll;
			set
			{
				if (SetProperty(ref _selectAll, value))
				{
					foreach (var item in Items)
					{
						item.IsSelected = value;
					}
				}
			}
		}

		public ICommand SearchCommand { get; }
		public ICommand CopySelectedCommand { get; }
		public ICommand CheckUpsalesCommand { get; }

		public MarketingWindowViewModel(
			IMarketingCampaignService campaignService,
			INotificationService notificationService,
			IUpsaleService upsaleService,
			Func<Task<(string CookieHeader, string CsrfToken)>> ensureDataLensAuth,
			Func<Task<YandexBusinessAuth>> getYandexBusinessAuth)
		{
			_campaignService = campaignService;
			_notificationService = notificationService;
			_upsaleService = upsaleService;
			_ensureDataLensAuth = ensureDataLensAuth;
			_getYandexBusinessAuth = getYandexBusinessAuth;

			UpdateStatusFilterSummary();

			SearchCommand = new AsyncRelayCommand(SearchAsync);
			CopySelectedCommand = new RelayCommand(CopySelected);
			CheckUpsalesCommand = new AsyncRelayCommand(CheckUpsalesAsync);
		}

		private void OnStatusFilterChanged()
		{
			UpdateStatusFilterSummary();
			_ = ApplyFilterAsync();
		}

		private void UpdateStatusFilterSummary()
		{
			if (StatusFilters.Count == 0)
			{
				StatusFilterSummary = "Статус";
				return;
			}

			var selected = StatusFilters.Where(f => f.IsSelected).ToList();

			if (selected.Count == StatusFilters.Count)
			{
				StatusFilterSummary = "Все статусы";
			}
			else if (selected.Count == 0)
			{
				StatusFilterSummary = "Статус не выбран";
			}
			else
			{
				StatusFilterSummary = string.Join(", ", selected.Select(f => f.Status));
			}
		}

		/// <summary>
		/// Перестраивает список статусов для фильтра из реально спарсенных данных — берёт
		/// уникальные значения из _allItems. Вызывается после каждого нового поиска, старые
		/// галки (в том числе от предыдущего UID с другим набором статусов) не переносятся —
		/// каждый статус из нового результата по умолчанию виден.
		/// </summary>
		private void RebuildStatusFilters()
		{
			StatusFilters.Clear();

			var distinctStatuses = _allItems
				.Select(i => i.Status)
				.Where(s => !string.IsNullOrEmpty(s))
				.Distinct();

			foreach (var status in distinctStatuses)
			{
				StatusFilters.Add(new StatusFilterOption(status, true, OnStatusFilterChanged));
			}

			UpdateStatusFilterSummary();
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
			NothingToSearch = false;
			SearchButtonLabel = "Поиск...";

			try
			{
				YandexBusinessAuth auth = await _getYandexBusinessAuth();

				if (string.IsNullOrEmpty(auth.CsrfToken))
				{
					_notificationService.ShowBalloon("Ошибка", "Не удалось получить данные авторизации yandex.ru/business — проверьте, что вход выполнен.", isWarning: true);
					return;
				}

				var progress = new Progress<string>(text => SearchButtonLabel = text);
				List<MarketingItem> items = await _campaignService.SearchAsync(uid, auth, progress);

				_allItems.Clear();
				_allItems.AddRange(items);
				NothingToSearch = _allItems.Count == 0 ? true : false;
				RebuildStatusFilters();
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

			// Карточки ниже пересоздаются заново (каждая начинается с IsSelected = false),
			// так что старое выделение всё равно теряется — синхронизируем и галку "Выбрать
			// все" тем же способом, напрямую через поле, чтобы не запускать её собственный
			// сеттер (Items в этот момент как раз пустой/перестраивается)
			if (_selectAll)
			{
				_selectAll = false;
				OnPropertyChanged(nameof(SelectAll));
			}

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
						Items.Add(new MarketingItemViewModel(item, _notificationService));
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
			var option = StatusFilters.FirstOrDefault(f => f.Status == status);
			return option?.IsSelected ?? true; // неизвестный статус — показываем, чтобы не терять данные
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

		private async Task CheckUpsalesAsync()
		{
			var selected = Items.Where(i => i.IsSelected).ToList();

			if (selected.Count == 0)
			{
				_notificationService.ShowBalloon("Предупреждение", "Отметьте хотя бы одну карточку.", isWarning: true);
				return;
			}

			try
			{
				var (cookieHeader, csrfToken) = await _ensureDataLensAuth();

				if (string.IsNullOrEmpty(csrfToken))
				{
					_notificationService.ShowBalloon("Ошибка", "Не удалось получить CSRF-токен DataLens — проверьте авторизацию.", isWarning: true);
					return;
				}

				var campaignIds = selected
					.Select(i => i.RawPermalink)
					.Where(p => !string.IsNullOrEmpty(p))
					.ToList();

				var progress = new Progress<string>(text => UpsaleButtonLabel = text);
				Dictionary<string, string> results = await _upsaleService.CheckUpsalesAsync(cookieHeader, csrfToken, campaignIds, progress);

				foreach (var item in selected)
				{
					if (!string.IsNullOrEmpty(item.RawPermalink) && results.TryGetValue(item.RawPermalink, out string value))
					{
						item.SetUpsale(value);
					}
				}
			}
			catch (Exception ex)
			{
				_notificationService.ShowBalloon("Ошибка", ex.Message, isWarning: true);
			}
			finally
			{
				UpsaleButtonLabel = "Проверить апсейлы";
			}
		}
	}
}
