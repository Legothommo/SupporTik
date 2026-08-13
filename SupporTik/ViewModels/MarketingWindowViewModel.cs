using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace SupporTik.ViewModels
{
	public class MarketingWindowViewModel : ViewModelBase
	{
		private readonly IMarketingCampaignService _campaignService;
		private readonly INotificationService _notificationService;
		private readonly IUpsaleService _upsaleService;
		private readonly IBudgetService _budgetService;

		/// <summary>
		/// Авторизация в DataLens — отдельный сайт (datalens.yandex-team.ru) со своей
		/// сессией, показ окна логина требует WebView2 (View-специфичная логика),
		/// поэтому передаётся сюда как делегат, а не делается напрямую из VM. View сам
		/// кэширует результат между вызовами — bool здесь просит его обновить кэш заново
		/// (используем, если запрос с закэшированным токеном всё же не сработал).
		/// </summary>
		private readonly Func<bool, Task<(string CookieHeader, string CsrfToken)>> _ensureDataLensAuth;

		/// <summary>
		/// Куки/CSRF-токен уже авторизованной страницы yandex.ru/business — для прямых
		/// API-запросов за списком кампаний. Тем же приёмом, что и для DataLens (делегат
		/// с кэшем на стороне View, а не прямой доступ к WebView2 из VM).
		/// </summary>
		private readonly Func<bool, Task<YandexBusinessAuth>> _getYandexBusinessAuth;

		private static readonly Regex RenewalDaysRegex = new Regex(@"\d+");

		// Проверка продления теперь обычный HTTP-запрос (см. BudgetService —
		// csrfToken/sessionId конкретной кампании достаются прямо из HTML её страницы без
		// WebView2/JS), поэтому идёт с ограниченной параллельностью, а не строго по очереди
		private const int MaxConcurrentRenewalChecks = 5;

		// Полный список последнего поиска (сырые данные) — из него строятся варианты
		// фильтров (RebuildFilters) и создаются карточки-VM ровно один раз за поиск
		private readonly List<MarketingItem> _allItems = new List<MarketingItem>();

		// BulkObservableCollection.ReplaceRange поднимает одно-единственное уведомление
		// (Reset) вместо Clear()+N×Add() — так весь список после поиска выводится одним
		// куском, а не через N отдельных перестроений разметки
		private readonly BulkObservableCollection<MarketingItemViewModel> _items =
			new BulkObservableCollection<MarketingItemViewModel>();

		public ObservableCollection<MarketingItemViewModel> Items => _items;

		/// <summary>
		/// Отфильтрованное представление Items — карточки под текущим фильтром меняются
		/// через ItemsView.Refresh() (пересчёт предиката FilterItem над уже существующими
		/// VM), а не через пересоздание Items. Раньше при каждом изменении фильтра ВСЕ
		/// MarketingItemViewModel пересоздавались заново (дорого — на большом списке
		/// заметно подвисало) и коллекция перестраивалась через Clear()+N×Add() (каждый
		/// Add — отдельная перерисовка). Теперь VM создаются один раз за поиск (см.
		/// SearchAsync), а фильтрация — это просто Refresh() поверх них.
		/// </summary>
		public ICollectionView ItemsView { get; }

		private string _uid = string.Empty;
		public string Uid
		{
			get => _uid;
			set => SetProperty(ref _uid, value);
		}

		private const int MaxRecentUids = 8;

		/// <summary>Последние успешно искавшиеся UID — самый свежий первым (см. AddRecentUid).</summary>
		public ObservableCollection<string> RecentUids { get; } = new ObservableCollection<string>();

		/// <summary>
		/// "Сессия подтверждена N мин назад" — обновляется из View (см.
		/// MarketingWindow.RefreshSessionStatusText), которая одна знает про
		/// WebView2-авторизацию и её кэш.
		/// </summary>
		private string _sessionStatusText = string.Empty;
		public string SessionStatusText
		{
			get => _sessionStatusText;
			set => SetProperty(ref _sessionStatusText, value);
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
		public ObservableCollection<FilterOption> StatusFilters { get; } = new ObservableCollection<FilterOption>();

		/// <summary>Роль пользователя в кампании ("Владелец"/"Наблюдатель"/...) — тот же принцип, что и StatusFilters.</summary>
		public ObservableCollection<FilterOption> RoleFilters { get; } = new ObservableCollection<FilterOption>();

		/// <summary>
		/// "Не проверено" / "Есть апсейл" / "Нет апсейла" (см. MarketingItem.UpsaleCategory) —
		/// перестраивается не только после поиска, но и после каждой проверки апсейлов
		/// (см. CheckUpsalesForItemsAsync), поскольку категории карточек меняются со временем.
		/// </summary>
		public ObservableCollection<FilterOption> UpsaleFilters { get; } = new ObservableCollection<FilterOption>();

		private string _statusFilterSummary;
		public string StatusFilterSummary
		{
			get => _statusFilterSummary;
			private set => SetProperty(ref _statusFilterSummary, value);
		}

		private string _roleFilterSummary;
		public string RoleFilterSummary
		{
			get => _roleFilterSummary;
			private set => SetProperty(ref _roleFilterSummary, value);
		}

		private string _upsaleFilterSummary;
		public string UpsaleFilterSummary
		{
			get => _upsaleFilterSummary;
			private set => SetProperty(ref _upsaleFilterSummary, value);
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
			private set
			{
				if (SetProperty(ref _isSearching, value))
				{
					OnPropertyChanged(nameof(IsNotSearching));
				}
			}
		}

		/// <summary>
		/// Инверсия IsSearching для IsEnabled в XAML — блокирует карточки, фильтры и кнопки
		/// действий, пока идёт поиск рекламных кампаний (SearchAsync) или проверка апсейлов
		/// (CheckUpsalesAsync) — оба метода используют одно и то же IsSearching.
		/// </summary>
		public bool IsNotSearching => !IsSearching;

		private bool _nothingToSearch;
		public bool NothingToSearch
		{
			get => _nothingToSearch;
			private set => SetProperty(ref _nothingToSearch, value);
		}

		private bool _lazyUpsaleCheck;
		/// <summary>
		/// "Ленивая" проверка — если включена, сразу после поиска (SearchAsync) все найденные
		/// карточки проходят проверку апсейлов (как CheckUpsalesCommand, но без выделения —
		/// на всех найденных), после чего фильтр по апсейлу сужается только до "Апсейлы"/"Продления".
		/// </summary>
		public bool LazyUpsaleCheck
		{
			get => _lazyUpsaleCheck;
			set => SetProperty(ref _lazyUpsaleCheck, value);
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

		// Пока true — Item_PropertyChanged не пересчитывает SelectAll на каждое отдельное
		// IsSelected, которое сама же SelectAll и выставляет ниже (иначе на середине цикла
		// пересчёт временно нашёл бы "не все выбраны" и сбросил бы галку раньше времени)
		private bool _suspendSelectAllSync;

		/// <summary>
		/// Двусторонняя синхронизация с выделением карточек: клик по галке отмечает/снимает
		/// все видимые под текущим фильтром карточки (ItemsView, а не Items — иначе скрытые
		/// фильтром карточки тоже незаметно попадали бы в выделение); а если пользователь
		/// сам вручную снял или, наоборот, отметил все карточки по одной — эта галка сама
		/// подстраивается под фактическое состояние (см. Item_PropertyChanged).
		/// </summary>
		public bool SelectAll
		{
			get => _selectAll;
			set
			{
				if (SetProperty(ref _selectAll, value))
				{
					_suspendSelectAllSync = true;
					try
					{
						foreach (MarketingItemViewModel item in ItemsView)
						{
							item.IsSelected = value;
						}
					}
					finally
					{
						_suspendSelectAllSync = false;
					}
				}
			}
		}

		/// <summary>
		/// Подписывается на каждую карточку при её создании (см. SearchAsync) — как только
		/// пользователь вручную меняет IsSelected у любой карточки, пересчитываем SelectAll
		/// по фактическому состоянию видимых карточек.
		/// </summary>
		private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (_suspendSelectAllSync || e.PropertyName != nameof(MarketingItemViewModel.IsSelected))
			{
				return;
			}

			var visibleItems = ItemsView.Cast<MarketingItemViewModel>().ToList();
			bool allSelected = visibleItems.Count > 0 && visibleItems.All(i => i.IsSelected);

			if (_selectAll != allSelected)
			{
				_selectAll = allSelected;
				OnPropertyChanged(nameof(SelectAll));
			}
		}

		public ICommand SearchCommand { get; }
		public ICommand CopySelectedCommand { get; }
		public ICommand CheckUpsalesCommand { get; }
		public ICommand CopySelectedUpsalesCommand { get; }

		// Кэш авторизации живёт во View между вызовами — если запрос всё же упал (сессия
		// протухла), просим View обновить его заново при следующей попытке, а не каждый раз
		private bool _forceRefreshYandexAuth;
		private bool _forceRefreshDataLensAuth;

		// Пока true — OnXFilterChanged не запускает Refresh на каждое отдельное изменение
		// галки. Без этого RebuildFilters/ShowOffersOnly, меняющие сразу несколько галок
		// подряд, гоняли бы фильтрацию по разу на каждую из них
		private bool _suspendFiltering;

		public MarketingWindowViewModel(
			IMarketingCampaignService campaignService,
			INotificationService notificationService,
			IUpsaleService upsaleService,
			IBudgetService budgetService,
			Func<bool, Task<(string CookieHeader, string CsrfToken)>> ensureDataLensAuth,
			Func<bool, Task<YandexBusinessAuth>> getYandexBusinessAuth)
		{
			_campaignService = campaignService;
			_notificationService = notificationService;
			_upsaleService = upsaleService;
			_budgetService = budgetService;
			_ensureDataLensAuth = ensureDataLensAuth;
			_getYandexBusinessAuth = getYandexBusinessAuth;

			ItemsView = CollectionViewSource.GetDefaultView(_items);
			ItemsView.Filter = FilterItem;

			UpdateStatusFilterSummary();
			UpdateRoleFilterSummary();
			UpdateUpsaleFilterSummary();

			SearchCommand = new AsyncRelayCommand(SearchAsync);
			CopySelectedCommand = new RelayCommand(CopySelected);
			CheckUpsalesCommand = new AsyncRelayCommand(CheckUpsalesAsync);
			CopySelectedUpsalesCommand = new RelayCommand(CopySelectedUpsales);

			string saved = Properties.Settings.Default.RecentMarketingUids ?? string.Empty;
			foreach (string uid in saved.Split('|').Where(u => !string.IsNullOrEmpty(u)))
			{
				RecentUids.Add(uid);
			}
		}

		/// <summary>Запоминает успешно искавшийся UID — свежий вперёд, без дублей, не больше MaxRecentUids штук.</summary>
		private void AddRecentUid(string uid)
		{
			RecentUids.Remove(uid);
			RecentUids.Insert(0, uid);

			while (RecentUids.Count > MaxRecentUids)
			{
				RecentUids.RemoveAt(RecentUids.Count - 1);
			}

			Properties.Settings.Default.RecentMarketingUids = string.Join("|", RecentUids);
			Properties.Settings.Default.Save();
		}

		private void OnStatusFilterChanged()
		{
			UpdateStatusFilterSummary();

			if (!_suspendFiltering)
			{
				ClearSelection();
				RefreshItemsView();
			}
		}

		private void OnRoleFilterChanged()
		{
			UpdateRoleFilterSummary();

			if (!_suspendFiltering)
			{
				ClearSelection();
				RefreshItemsView();
			}
		}

		private void OnUpsaleFilterChanged()
		{
			UpdateUpsaleFilterSummary();

			if (!_suspendFiltering)
			{
				ClearSelection();
				RefreshItemsView();
			}
		}

		/// <summary>
		/// Снимает выделение со всех карточек — вызывается, когда пользователь меняет
		/// фильтр (см. OnStatusFilterChanged/OnRoleFilterChanged/OnUpsaleFilterChanged),
		/// чтобы старое выделение не "утекало" в новый набор видимых карточек. Карточки
		/// теперь не пересоздаются при смене фильтра (см. ItemsView), поэтому без явного
		/// сброса IsSelected сам собой не снимался бы, как раньше. Специально НЕ вызывается
		/// из RefreshItemsView напрямую — в CheckUpsalesForItemsAsync выделение, наоборот,
		/// должно пережить обновление списка после проверки апсейлов.
		/// </summary>
		private void ClearSelection()
		{
			foreach (var item in Items)
			{
				item.IsSelected = false;
			}

			if (_selectAll)
			{
				_selectAll = false;
				OnPropertyChanged(nameof(SelectAll));
			}
		}

		private void UpdateStatusFilterSummary() =>
			StatusFilterSummary = BuildFilterSummary(StatusFilters, "Статус", "Все статусы", "Статус не выбран");

		private void UpdateRoleFilterSummary() =>
			RoleFilterSummary = BuildFilterSummary(RoleFilters, "Роль", "Все роли", "Роль не выбрана");

		private void UpdateUpsaleFilterSummary() =>
			UpsaleFilterSummary = BuildFilterSummary(UpsaleFilters, "Апсейл", "Все варианты", "Ничего не выбрано");

		private static string BuildFilterSummary(ObservableCollection<FilterOption> filters, string placeholder, string allLabel, string noneLabel)
		{
			if (filters.Count == 0)
			{
				return placeholder;
			}

			var selected = filters.Where(f => f.IsSelected).ToList();

			if (selected.Count == filters.Count)
			{
				return allLabel;
			}

			if (selected.Count == 0)
			{
				return noneLabel;
			}

			return string.Join(", ", selected.Select(f => f.Value));
		}

		/// <summary>
		/// Перестраивает панель фильтра из реально спарсенных данных — берёт уникальные
		/// значения из _allItems по selector. Старые галки не переносятся, каждое новое
		/// значение по умолчанию видно. _suspendFiltering на время Clear()+N×Add() — иначе
		/// каждая добавленная галка (IsSelected = true в конструкторе FilterOption не
		/// поднимает onChanged, но следующие изменения тех же галок извне — поднимали бы).
		/// </summary>
		private void RebuildFilters(ObservableCollection<FilterOption> filters, Func<MarketingItem, string> selector, Action onChanged)
		{
			_suspendFiltering = true;

			try
			{
				filters.Clear();

				var distinctValues = _allItems
					.Select(selector)
					.Where(v => !string.IsNullOrEmpty(v))
					.Distinct();

				foreach (var value in distinctValues)
				{
					filters.Add(new FilterOption(value, true, onChanged));
				}
			}
			finally
			{
				_suspendFiltering = false;
			}
		}

		private void RebuildStatusFilters()
		{
			RebuildFilters(StatusFilters, i => i.Status, OnStatusFilterChanged);
			UpdateStatusFilterSummary();
		}

		private void RebuildRoleFilters()
		{
			RebuildFilters(RoleFilters, i => i.Role, OnRoleFilterChanged);
			UpdateRoleFilterSummary();
		}

		private void RebuildUpsaleFilters()
		{
			RebuildFilters(UpsaleFilters, i => i.UpsaleCategory, OnUpsaleFilterChanged);
			UpdateUpsaleFilterSummary();
		}

		/// <summary>Предикат ICollectionView — вызывается WPF для каждой карточки при ItemsView.Refresh().</summary>
		private bool FilterItem(object obj)
		{
			var item = (MarketingItemViewModel)obj;

			return IsFilterVisible(StatusFilters, item.StatusKey)
				&& IsFilterVisible(RoleFilters, item.Role)
				&& IsFilterVisible(UpsaleFilters, item.UpsaleCategory);
		}

		/// <summary>
		/// Пересчитывает предикат фильтра поверх уже существующих карточек (без создания
		/// новых MarketingItemViewModel и без Clear()/Add() коллекции) и обновляет счётчик.
		/// </summary>
		private void RefreshItemsView()
		{
			ItemsView.Refresh();
			ResultsCountText = ResultsCountLabel + ItemsView.Cast<object>().Count();
		}

		private static bool IsFilterVisible(ObservableCollection<FilterOption> filters, string value)
		{
			var option = filters.FirstOrDefault(f => f.Value == value);
			return option?.IsSelected ?? true; // неизвестное значение — показываем, чтобы не терять данные
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
				YandexBusinessAuth auth = await _getYandexBusinessAuth(_forceRefreshYandexAuth);

				if (string.IsNullOrEmpty(auth.CsrfToken))
				{
					// Токен пустой — просим View в следующий раз обновить его заново, а не
					// сбрасываем флаг здесь же (иначе при пустом кэше на стороне View
					// следующая попытка снова получила бы forceRefresh=false)
					_forceRefreshYandexAuth = true;
					_notificationService.ShowBalloon("Ошибка", "Не удалось получить данные авторизации yandex.ru/business — проверьте, что вход выполнен.", isWarning: true);
					return;
				}

				_forceRefreshYandexAuth = false;

				var progress = new Progress<string>(text => SearchButtonLabel = text);
				List<MarketingItem> items = await _campaignService.SearchAsync(uid, auth, progress);

				AddRecentUid(uid);

				_allItems.Clear();
				_allItems.AddRange(items);
				NothingToSearch = _allItems.Count == 0;

				if (NothingToSearch)
				{
					_notificationService.ShowBalloon("Ничего не найдено", $"У пользователя {uid} нет рекламных кампаний — проверьте UID.", isWarning: true);
				}

				// Карточки-VM создаются здесь РОВНО ОДИН РАЗ на весь поиск (не при каждом
				// изменении фильтра, как было раньше) — в фоновом потоке, чтобы создание
				// сотен объектов не занимало UI-поток, и разом кладутся в коллекцию через
				// ReplaceRange (одно уведомление вместо N)
				var notificationService = _notificationService;
				List<MarketingItemViewModel> vms = await Task.Run(() =>
					_allItems.Select(item => new MarketingItemViewModel(item, notificationService)).ToList());

				// Подписка нужна для двусторонней синхронизации с SelectAll (см.
				// Item_PropertyChanged) — старые карточки из прошлого поиска отписывать не
				// нужно: они больше никем не удерживаются (не в _items/ItemsView) и уйдут
				// под сборку мусора вместе со своими подписками
				foreach (var vm in vms)
				{
					vm.PropertyChanged += Item_PropertyChanged;
				}

				_items.ReplaceRange(vms);

				// Новые карточки и так создаются с IsSelected = false, но саму галку
				// "Выбрать все" никто не трогал — без явного сброса она осталась бы включённой
				// с прошлого поиска, хотя фактически ни одна карточка не выбрана
				ClearSelection();

				RebuildStatusFilters();
				RebuildRoleFilters();
				RebuildUpsaleFilters();
				RefreshItemsView();

				if (LazyUpsaleCheck && _allItems.Count > 0)
				{
					var lazyCheckItems = Items
						.Where(i => i.StatusKey == "Активна" || i.StatusKey == "Завершена")
						.ToList();

					if (lazyCheckItems.Count > 0)
					{
						await CheckUpsalesForItemsAsync(lazyCheckItems, progress);
						ShowOffersOnly();
					}
				}
			}

			catch (Exception ex)
			{
				// Кэш токена (см. MarketingWindow.EnsureDataLensAuthAsync/GetYandexBusinessAuthAsync)
				// мог протухнуть — при следующей попытке просим View обновить его заново
				_forceRefreshYandexAuth = true;
				_notificationService.ShowBalloon("Ошибка", ex.Message, isWarning: true);
			}
			finally
			{
				SearchButtonLabel = "Поиск";
				IsSearching = false;
			}
		}

		/// <summary>
		/// Сужает фильтр по апсейлу до "Апсейлы"/"Продления"/"" и фильтр по статусу до
		/// "Активна"/"Завершена" (те же статусы, что и в самой ленивой проверке, см.
		/// SearchAsync) — используется после ленивой проверки (LazyUpsaleCheck), чтобы сразу
		/// показать только реально проверенные карточки с предложением. _suspendFiltering
		/// на время обоих foreach — иначе каждое отдельное изменение галки запускало бы
		/// свой Refresh (с 6 статусами и несколькими вариантами апсейла — до десятка раз
		/// подряд); в конце один RefreshItemsView() на всё сразу.
		/// </summary>
		private void ShowOffersOnly()
		{
			_suspendFiltering = true;

			try
			{
				foreach (var option in StatusFilters)
				{
					option.IsSelected = option.Value == "Активна" || option.Value == "Завершена";
				}

				foreach (var option in UpsaleFilters)
				{
					option.IsSelected = option.Value == "Апсейлы" || option.Value == "Продления" || option.Value == "Увел./умен. бюджет РК" || option.Value == "Проверь в ЛК";
				}
			}
			finally
			{
				_suspendFiltering = false;
			}

			UpdateStatusFilterSummary();
			UpdateUpsaleFilterSummary();
			RefreshItemsView();
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

		/// <summary>
		/// Собирает текст предложения (см. MarketingItemViewModel.BuildUpsaleText) по всем
		/// отмеченным карточкам и кладёт в буфер одним куском — тот же принцип отбора, что
		/// и у CheckUpsalesCommand/CopySelectedCommand (Items.Where(i => i.IsSelected)).
		/// </summary>
		private void CopySelectedUpsales()
		{
			var selected = Items.Where(i => i.IsSelected).ToList();

			if (selected.Count <= 1)
			{
				_notificationService.ShowBalloon("Предупреждение", "Отметьте хотя бы одну карточку.", isWarning: true);
				return;
			}

			var text = BuildUpsalesText(selected);
			if (text != null)
			{
				Clipboard.SetText(text);
				_notificationService.ShowBalloon("Скопировано", $"Текстов в буфере: {selected.Count}", isWarning: false);
			}
		}

		public string BuildUpsalesText(List<MarketingItemViewModel> items)
		{
			var hasOneRole = items
				.Select(x => x.Role)
				.Distinct();

			// Проверяем ДО .Contains ниже — у "Не проверено"/"Нет предложения"/"Не продавать"
			// UpsaleValue либо null, либо не содержит "Продление", иначе .Contains упадёт на null
			bool hasUpsale = items.All(x =>
				!string.IsNullOrEmpty(x.UpsaleValue) &&
				x.UpsaleValue != "Нет предложения" &&
				x.UpsaleValue != "Проверь в ЛК" &&
				x.UpsaleValue != "Не продавать" ||
				!x.HasUpsale);

			if (!hasUpsale) { _notificationService.ShowBalloon("Предупреждение", "Выбери кампании с предложениями", isWarning: true); return null; }
			if (hasOneRole.Count() != 1) { _notificationService.ShowBalloon("Предупреждение", "Нельзя выбирать разные роли.", isWarning: true); return null; }

			bool allUpsalesAreInt = items.All(x => int.TryParse(x.UpsaleValue, out _));
			bool allUpsalesAreString = items.All(x => x.UpsaleValue.Contains("Продление"));

			if (!allUpsalesAreInt && !allUpsalesAreString) { _notificationService.ShowBalloon("Предупреждение", "Нельзя выбирать разные предложения.", isWarning: true); return null; }

			var result = string.Empty;
			var firstStroke = string.Empty;
			var secondStroke = string.Empty;

			if (allUpsalesAreInt)
			{
				firstStroke = string.Join("\r\n",
					items.Select(item => item.IsMulti
						? $"- [https://yandex.ru/business/subscription/campaign/{item.DisplayPermalink}?upsale_budget={item.UpsaleValue}&show_popup=upsale](https://yandex.ru/business/subscription/campaign/{item.DisplayPermalink}?upsale_budget={item.UpsaleValue}&show_popup=upsale)"
						: $"- [https://yandex.ru/business/priority/campaign/{item.DisplayPermalink}/main?show_popup=upsale&upsale_budget={item.UpsaleValue}](https://yandex.ru/business/priority/campaign/{item.DisplayPermalink}/main?show_popup=upsale&upsale_budget={item.UpsaleValue})"));

				result = $"Мы заметили, что вам доступны увеличения бюджета для ваших рекламных кампаний. Увеличьте месячный бюджет на продвижения, чтобы повысить их охват:\r\n\r\n" +
					$"{firstStroke}\r\n\r\n" +
					$"Алгоритм подбирает площадки и публикует объявления в пределах бюджета, который вы выбрали. Если его увеличить, объявления будут публиковаться чаще и на новых площадках. Это расширит клиентскую базу — об организации узнает больше пользователей.";

				if (hasOneRole.First() == "Наблюдатель")
				{
					result += "\r\n\r\nПодробности отправим на почту владельца продвижения. Предложение действует 7 дней";
				}
			}
			if (allUpsalesAreString)
			{
				firstStroke = string.Join("\r\n",
						items.Select(item => $"- № {item.DisplayPermalink}")) + ".";
				secondStroke = string.Join("\r\n",
					items.Select(item =>
					{
						int days = int.Parse(item.UpsaleValue.Split(' ')[1]);
						return $"- № {item.DisplayPermalink} на {days} дней - {item.AmountUpsale} ₽";
					})) + ".";

				if (hasOneRole.First() == "Наблюдатель")
				{
					result = $"Мы заметили, что скоро завершатся кампании по продвижению:\r\n\r\n" +
						$"{firstStroke}\r\n\r\n" +
						$"Продлите их, чтобы избежать перерыва в показах.\r\n\r\n" +
						$"Отправим подробности владельцам кампаний на почту";
				}
				else
				{
					result = $"Мы заметили, что скоро завершатся кампании ваши по продвижению:\r\n\r\n" +
						$"{firstStroke}\r\n\r\n" +
						$"Продлите их, чтобы избежать перерыва в показах. Стоимость продления:\r\n\r\n" +
						$"{secondStroke}\r\n\r\n" +
						$"Тарифы на 180 или 360 дней помогут сэкономить до 25% затрат. Отметим, что чем дольше клиенты видят вас, тем надёжнее будет поток заявок";
				}
			}
			return result;
		}

		private async Task CheckUpsalesAsync()
		{
			var selected = Items.Where(i => i.IsSelected).ToList();

			if (selected.Count == 0)
			{
				_notificationService.ShowBalloon("Предупреждение", "Отметьте хотя бы одну карточку.", isWarning: true);
				return;
			}

			IsSearching = true;

			try
			{
				var progress = new Progress<string>(text => UpsaleButtonLabel = text);
				await CheckUpsalesForItemsAsync(selected, progress);
			}
			catch (Exception ex)
			{
				_forceRefreshDataLensAuth = true;
				_notificationService.ShowBalloon("Ошибка", ex.Message, isWarning: true);
			}
			finally
			{
				UpsaleButtonLabel = "Проверить апсейлы";
				IsSearching = false;
			}
		}

		/// <summary>
		/// Общая логика проверки апсейлов (DataLens + calculate-web-renewal-budget) — вызывается
		/// и вручную по кнопке (CheckUpsalesCommand, на выбранных карточках), и автоматически
		/// сразу после поиска (см. LazyUpsaleCheck в SearchAsync, на всех найденных).
		/// </summary>
		private async Task CheckUpsalesForItemsAsync(
				List<MarketingItemViewModel> items,
				IProgress<string> progress)
		{
			// Первая попытка:
			// если ранее был установлен forceRefresh — обновляем сразу,
			// иначе используем обычную/закэшированную авторизацию.
			var (cookieHeader, csrfToken) =
				await _ensureDataLensAuth(_forceRefreshDataLensAuth);

			// ------------------------------------------------------------
			// Если DataLens ещё не был авторизован.
			//
			// Особенно актуально при первом запуске:
			// пользователь только что авторизовался в Yandex Business,
			// после чего LazyUpsaleCheck сразу дошёл до DataLens.
			//
			// Не ждём следующего поиска — прямо сейчас просим View
			// принудительно пройти/обновить DataLens-авторизацию.
			// ------------------------------------------------------------
			if (string.IsNullOrEmpty(csrfToken))
			{
				progress?.Report("Авторизация в DataLens...");

				(cookieHeader, csrfToken) =
					await _ensureDataLensAuth(true);
			}

			// После второй попытки токена всё ещё нет —
			// тогда уже действительно показываем ошибку.
			if (string.IsNullOrEmpty(csrfToken))
			{
				_forceRefreshDataLensAuth = true;

				_notificationService.ShowBalloon(
					"Ошибка",
					"Не удалось получить CSRF-токен DataLens — проверьте авторизацию.",
					isWarning: true);

				return;
			}

			// Успешно получили свежую/валидную авторизацию.
			_forceRefreshDataLensAuth = false;


			var campaignIds = items
				.Select(i => i.RawPermalink)
				.Where(p => !string.IsNullOrEmpty(p))
				.ToList();


			Dictionary<string, string> results =
				await _upsaleService.CheckUpsalesAsync(
					cookieHeader,
					csrfToken,
					campaignIds,
					progress);


			foreach (var item in items)
			{
				if (!string.IsNullOrEmpty(item.RawPermalink) &&
					results.TryGetValue(
						item.RawPermalink,
						out string value))
				{
					item.SetUpsale(value);
				}
			}


			await ResolveCampaignDetailsAsync(
				items,
				progress);


			RebuildUpsaleFilters();
			RefreshItemsView();
		}

		/// <summary>
		/// Для карточек с "Продление N дней" в UpsaleValue считает точную сумму продления
		/// через billing/calculate-web-renewal-budget (см. BudgetService — csrfToken/sessionId
		/// конкретной кампании достаются обычным HTTP GET её страницы, без WebView2/JS) и
		/// заодно получает isMulti/businessSnapshotReviewedStatus. Для остальных проверенных
		/// карточек (апсейлы-числа и т.п.) полный расчёт не нужен, но эти два флага — нужны
		/// всем, поэтому для них делается облегчённый запрос (GetCampaignFlagsAsync, без POST
		/// на calculate-web-renewal-budget). Раньше это делалось строго по очереди (единственный
		/// экземпляр WebView2) — теперь
		/// это обычные HTTP-запросы, поэтому идёт с ограниченной параллельностью
		/// (MaxConcurrentRenewalChecks), как и проверка апсейлов в UpsaleService. Если для
		/// конкретной кампании не получилось — просто пропускает её, не прерывая проверку
		/// остальных.
		/// </summary>
		private async Task ResolveCampaignDetailsAsync(List<MarketingItemViewModel> items, IProgress<string> progress)
		{
			var relevantItems = items
				.Where(i => !string.IsNullOrEmpty(i.RawPermalink))
				.ToList();

			if (relevantItems.Count == 0)
			{
				return;
			}

			// cookieHeader общий для всех кампаний (сессия yandex.ru) — тот же самый, что
			// уже используется для списка кампаний, кэшируется во View между вызовами
			YandexBusinessAuth auth = await _getYandexBusinessAuth(false);

			if (string.IsNullOrEmpty(auth.CookieHeader))
			{
				return; // не авторизованы — пропускаем, апсейлы уже проверены
			}

			int completed = 0;

			using (var throttle = new SemaphoreSlim(MaxConcurrentRenewalChecks))
			{
				var tasks = relevantItems.Select(async item =>
				{
					await throttle.WaitAsync();
					try
					{
						bool isRenewal = !string.IsNullOrEmpty(item.UpsaleValue) && item.UpsaleValue.Contains("Продление");

						if (isRenewal)
						{
							var match = RenewalDaysRegex.Match(item.UpsaleValue);
							if (!match.Success || !int.TryParse(match.Value, out int durationDays))
							{
								return;
							}

							var result = await _budgetService.CalculateRenewalAmountAsync(item.CompanyPermalink, item.RawPermalink, auth.CookieHeader, durationDays);

							if (result != null)
							{
								item.SetAmountUpsale(result[0]);
								item.SetPrediction(result[1]);
								item.SetIsMulti(result.TryGetValue(2, out string isMultiRaw) && bool.TryParse(isMultiRaw, out bool isMulti) && isMulti);
								item.SetHasBudgetIncreaseButton(result.TryGetValue(3, out string hasButtonRaw) && bool.TryParse(hasButtonRaw, out bool hasButton) && hasButton);
							}
						}
						else
						{
							var flags = await _budgetService.GetCampaignFlagsAsync(item.RawPermalink, auth.CookieHeader);
							item.SetIsMulti(flags.IsMulti);
							item.SetHasBudgetIncreaseButton(flags.HasBudgetIncreaseButton);
						}
					}
					catch (Exception)
					{
						// Не удалось получить детали для конкретной кампании — оставляем как
						// есть и переходим к следующей
					}
					finally
					{
						int done = Interlocked.Increment(ref completed);
						progress?.Report($"Проверка кампаний {done}/{relevantItems.Count}...");
						throttle.Release();
					}
				});

				await Task.WhenAll(tasks);
			}
		}
	}
}
