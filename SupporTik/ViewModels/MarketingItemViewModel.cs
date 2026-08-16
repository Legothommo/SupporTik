using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;

namespace SupporTik.ViewModels
{
	/// <summary>Одна карточка кампании. StatusKey — сырое значение статуса, View сама решит цвет через конвертер.</summary>
	public class MarketingItemViewModel : ViewModelBase
	{
		private readonly MarketingItem _item;
		private readonly INotificationService _notificationService;
		private readonly MarketingOfferTextBuilder _textBuilder;
		internal MarketingItem Model => _item;

		/// <summary>Сырой пермалинк (без плейсхолдера "—") — используется при копировании, не для отображения.</summary>
		public string RawPermalink => _item.Permalink;

		/// <summary>Пермалинк компании (не кампании) — нужен для billing/calculate-budget.</summary>
		public string CompanyPermalink => _item.CompanyPermalink;

		public string DisplayPermalink => string.IsNullOrEmpty(_item.Permalink) ? "—" : _item.Permalink;
		public string StatusKey => _item.Status;
		public string DisplayStatus => string.IsNullOrEmpty(_item.Status) ? "—" : _item.Status;
		public string DisplayRemain => string.IsNullOrEmpty(_item.Remain) ? "—" : _item.Remain;

		public string Role => _item.Role;

		public bool HasRole => !string.IsNullOrEmpty(_item.Role);

		/// <summary>
		/// Результат проверки апсейла хранится на самом MarketingItem (не в поле ViewModel) —
		/// карточки теперь и не пересоздаются при смене фильтра (см.
		/// MarketingWindowViewModel.ItemsView/RefreshItemsView), но так значение переживёт
		/// даже полное пересоздание, если оно когда-нибудь понадобится.
		/// </summary>
		public string UpsaleValue => _item.UpsaleValue;

		/// <summary>
		/// businessSnapshotReviewedStatus == "NOT_REVIEWED" (см. BudgetService) —
		/// подтверждено на реальных данных как признак наличия кнопки "Увеличить бюджет"
		/// на живой странице. Влияет только на отображение (см. UpsaleDisplayValue), сам
		/// UpsaleValue не трогает — иначе сломался бы int.TryParse в BuildUpsaleText/тексте
		/// ссылок на upsale_budget.
		/// </summary>
		public bool HasBudgetIncreaseButton => _item.HasBudgetIncreaseButton;

		/// <summary>
		/// То, что реально показывается в поле апсейла на карточке (см. TbUpsale в
		/// MarketingItemPanel.xaml) — если UpsaleValue числовой (апсейл-бюджет) и кнопки
		/// "Увеличить бюджет" на странице кампании НЕТ, вместо самого числа показываем
		/// короткую подпись; если кнопка есть — показываем число как есть. UpsaleValue при
		/// этом остаётся числовым для логики копирования.
		/// </summary>
		public string UpsaleDisplayValue =>
			!HasBudgetIncreaseButton && int.TryParse(UpsaleValue, out _)
				? "Увел./умен. бюджет РК"
				: UpsaleValue;

		/// <summary>Для фильтра по апсейлу (см. MarketingWindowViewModel.FilterItem) — передаёт MarketingItem.UpsaleCategory.</summary>
		public string UpsaleCategory => _item.UpsaleCategory;

		public bool HasUpsale => !string.IsNullOrEmpty(_item.UpsaleValue);

		/// <summary>
		/// Кнопка "Копировать" на карточке нужна только когда есть реальное предложение —
		/// скрываем её для служебных значений UpsaleValue, под которые нет шаблона текста
		/// (см. BuildUpsaleText), и для карточек без кнопки "Увеличить бюджет" на сайте
		/// (UpsaleDisplayValue показывает подпись вместо числа — сравниваем с ней, а не
		/// с сырым UpsaleValue, который в этом случае всё ещё числовой).
		/// </summary>
		public bool CanCopyUpsale =>
			!string.IsNullOrEmpty(_item.UpsaleValue) &&
			_item.UpsaleValue != "Не проверено" &&
			_item.UpsaleValue != "Нет предложения" &&
			_item.UpsaleValue != "Не продавать" &&
			_item.UpsaleValue != "Нет данных" &&
			_item.UpsaleValue != "Проверь в ЛК" &&
			UpsaleDisplayValue != "Увел./умен. бюджет РК";

		/// <summary>
		/// Точная сумма продления из billing/calculate-web-renewal-budget — не привязана
		/// ни к одному элементу карточки, в панели не показывается.
		/// </summary>
		public string AmountUpsale => _item.AmountUpsale;
		public string Prediction => _item.Prediction;

		/// <summary>
		/// isMulti из ответа get-campaign-v3 (см. BudgetService.CalculateRenewalAmountAsync) —
		/// для использования в тексте при копировании (см. BuildUpsaleText/BuildUpsalesText).
		/// </summary>
		public bool IsMulti => _item.IsMulti;

		public bool CanOpen => !string.IsNullOrEmpty(_item.Href);

		private bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public ICommand OpenCommand { get; }
		public ICommand CopyPermalinkCommand { get; }
		public ICommand CopyWithTemplateCommand { get; }

		public MarketingItemViewModel(
			MarketingItem item,
			INotificationService notificationService,
			MarketingOfferTextBuilder textBuilder)
		{
			_item = item;
			_notificationService = notificationService;
			_textBuilder = textBuilder;

			OpenCommand = new RelayCommand(Open, () => CanOpen);
			CopyPermalinkCommand = new RelayCommand(CopyPermalink, () => !string.IsNullOrEmpty(RawPermalink));
			CopyWithTemplateCommand = new RelayCommand(CopyWithTemplate);
		}

		private void CopyWithTemplate(object parameter)
		{
			if (!(parameter is MarketingTextTemplate template)) return;
			Clipboard.SetText(BuildUpsaleText(template));

			_notificationService.ShowBalloon(
				"Скопировано",
				"Текст скопирован в буфер",
				false);
		}

		public string BuildUpsaleText(MarketingTextTemplate template)
		{
			return _textBuilder.BuildSingle(_item, template);
		}

		public IReadOnlyList<MarketingTextTemplate> GetCompatibleTemplates()
		{
			IReadOnlyList<MarketingTextTemplate> templates = _textBuilder.GetTemplatesForSingle(_item);
			if (templates.Count == 0)
			{
				_notificationService.ShowBalloon(
					"Шаблоны",
					"Для этой кампании нет подходящих шаблонов.",
					isWarning: true);
			}
			return templates;
		}

		private void Open()
		{
			if (CanOpen)
			{
				Process.Start(new ProcessStartInfo(_item.Href) { UseShellExecute = true });
			}
		}

		private void CopyPermalink()
		{
			Clipboard.SetText(RawPermalink);
			_notificationService.ShowBalloon("Скопировано", $"Пермалинк {RawPermalink} в буфере", isWarning: false);
		}

		/// <summary>Вызывается ViewModel окна после проверки апсейла (см. MarketingWindowViewModel.CheckUpsalesAsync).</summary>
		public void SetUpsale(string value)
		{
			_item.UpsaleValue = string.IsNullOrEmpty(value) ? "Нет данных" : value;
			OnPropertyChanged(nameof(UpsaleValue));
			OnPropertyChanged(nameof(UpsaleDisplayValue));
			OnPropertyChanged(nameof(HasUpsale));
			OnPropertyChanged(nameof(CanCopyUpsale));
			OnPropertyChanged(nameof(UpsaleCategory));
		}

		/// <summary>
		/// Записывает точную сумму продления (см. MarketingWindowViewModel.ResolveCampaignDetailsAsync) —
		/// в отличие от SetUpsale, не трогает UpsaleValue/HasUpsale, значение в панели не отображается.
		/// </summary>
		public void SetAmountUpsale(string value)
		{
			_item.AmountUpsale = value;
			OnPropertyChanged(nameof(AmountUpsale));
		}
		public void SetPrediction(string value)
		{
			_item.Prediction = value;
			OnPropertyChanged(nameof(Prediction));
		}

		public void SetIsMulti(bool value)
		{
			_item.IsMulti = value;
			OnPropertyChanged(nameof(IsMulti));
		}

		public void SetHasBudgetIncreaseButton(bool value)
		{
			_item.HasBudgetIncreaseButton = value;
			OnPropertyChanged(nameof(HasBudgetIncreaseButton));
			OnPropertyChanged(nameof(UpsaleDisplayValue));
			OnPropertyChanged(nameof(CanCopyUpsale));
			OnPropertyChanged(nameof(UpsaleCategory));
		}
	}
}
