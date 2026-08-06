using System.Diagnostics;
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

		/// <summary>Для фильтра по апсейлу (см. MarketingWindowViewModel.FilterItem) — передаёт MarketingItem.UpsaleCategory.</summary>
		public string UpsaleCategory => _item.UpsaleCategory;

		public bool HasUpsale => !string.IsNullOrEmpty(_item.UpsaleValue);

		/// <summary>
		/// Кнопка "Копировать" на карточке нужна только когда есть реальное предложение —
		/// скрываем её для служебных значений UpsaleValue, под которые нет шаблона текста
		/// (см. BuildUpsaleText).
		/// </summary>
		public bool CanCopyUpsale =>
			!string.IsNullOrEmpty(_item.UpsaleValue) &&
			_item.UpsaleValue != "Не проверено" &&
			_item.UpsaleValue != "Нет предложения" &&
			_item.UpsaleValue != "Не продавать" &&
			_item.UpsaleValue != "Нет данных" &&
			_item.UpsaleValue != "Проверь в ЛК";

		/// <summary>
		/// Точная сумма продления из billing/calculate-web-renewal-budget — не привязана
		/// ни к одному элементу карточки, в панели не показывается.
		/// </summary>
		public string AmountUpsale => _item.AmountUpsale;
		public string Prediction => _item.Prediction;

		public bool CanOpen => !string.IsNullOrEmpty(_item.Href);

		private bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public ICommand OpenCommand { get; }
		public ICommand CopyPermalinkCommand { get; }
		public ICommand CopyUpsaleCommand { get; }

		public MarketingItemViewModel(MarketingItem item, INotificationService notificationService)
		{
			_item = item;
			_notificationService = notificationService;

			OpenCommand = new RelayCommand(Open, () => CanOpen);
			CopyPermalinkCommand = new RelayCommand(CopyPermalink, () => !string.IsNullOrEmpty(RawPermalink));
			CopyUpsaleCommand = new RelayCommand(CopyUpsale);
		}

		private void CopyUpsale()
		{
			Clipboard.SetText(BuildUpsaleText());

			_notificationService.ShowBalloon(
				"Скопировано",
				"Текст скопирован в буфер",
				false);
		}

		/// <summary>
		/// Собирает текст предложения по этой карточке — тот же шаблон, что и кнопка
		/// копирования на карточке (CopyUpsaleCommand), но без побочных эффектов
		/// (буфер/уведомление), чтобы им мог переиспользоваться при массовом копировании
		/// (см. MarketingWindowViewModel.CopySelectedUpsalesCommand).
		/// </summary>
		public string BuildUpsaleText()
		{
			var text = "";
			if (Role == "Владелец")
			{
				if (UpsaleValue.Contains("Продление"))
				{
					string[] parts = UpsaleValue.Split(' ');
					int days = int.Parse(parts[1]);

					text = $"Видим, что подписка № {_item.Permalink} скоро завершится. Предлагаем продлить её, чтобы не прерывать показы, сохранить результаты и привлечь новую аудиторию.\r\n\r\n" +
						$"Продление на {days} дней составит {_item.AmountUpsale} ₽ и принесёт до {_item.Prediction} потенциальных клиентов в месяц.\r\n\r\n";
					if (days == 90) text += $"Если планируете продвижение надолго, сроки на 180 или 360 дней принесут выгоду — экономию до 25%. Отметим, что чем дольше ваш бизнес на виду, тем надёжнее поток клиентов.";
					if (days == 180) text += $"Если планируете продвижение надолго, срок на 360 дней принесёт выгоду — экономию 25%. Отметим, что чем дольше ваш бизнес на виду, тем надёжнее поток клиентов";
				}
				if (int.TryParse(UpsaleValue, out int i))
				{
					text = $"Вижу, что у вашей кампании хорошие показатели. Их можно улучшить с помощью увеличения бюджета.\r\n\r\n" +
						$"Как это работает: алгоритм показа объявлений выбирает площадки в пределах бюджета. Если его увеличить, алгоритм получит новые возможности, чтобы привлекать больше потенциальных клиентов.\r\n\r\n" +
						$"Подробности предложения: https://yandex.ru/business/subscription/campaign/{_item.Permalink}?upsale_budget={_item.UpsaleValue}&show_popup=upsale";
				}
			}
			else
			{
				if (UpsaleValue.Contains("Продление"))
				{
					string[] parts = UpsaleValue.Split(' ');
					int days = int.Parse(parts[1]);

					text = $"Мы заметили, что кампания по продвижению № {_item.Permalink} скоро завершится. Продлите её, чтобы не прерывать показы.\r\n\r\n" +
						$"Подробности отправим на почту владельца кампании";
				}
				if (int.TryParse(UpsaleValue, out int i))
				{
					text = $"Вижу, что у вашей кампании хорошие показатели. Их можно улучшить с помощью увеличения бюджета.\r\n\r\n" +
						$"Как это работает: алгоритм показа объявлений выбирает площадки в пределах бюджета. Если его увеличить, алгоритм получит новые возможности, чтобы привлекать больше потенциальных клиентов.\r\n\r\n" +
						$"Подробности предложения: https://yandex.ru/business/subscription/campaign/{_item.Permalink}?upsale_budget={_item.UpsaleValue}&show_popup=upsale\r\n\r\n" +
						$"Отправим письмо с подробностями на почту владельца кампании. Предложение действует 7 дней";
				}
			}

			return text;
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
			OnPropertyChanged(nameof(HasUpsale));
			OnPropertyChanged(nameof(CanCopyUpsale));
			OnPropertyChanged(nameof(UpsaleCategory));
		}

		/// <summary>
		/// Записывает точную сумму продления (см. MarketingWindowViewModel.ResolveRenewalAmountsAsync) —
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
	}
}
