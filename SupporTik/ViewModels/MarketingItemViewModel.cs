using System.Diagnostics;
using System.Windows;
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

		public string DisplayPermalink => string.IsNullOrEmpty(_item.Permalink) ? "—" : _item.Permalink;
		public string StatusKey => _item.Status;
		public string DisplayStatus => string.IsNullOrEmpty(_item.Status) ? "—" : _item.Status;
		public string DisplayRemain => string.IsNullOrEmpty(_item.Remain) ? "—" : _item.Remain;

		public string Role => _item.Role;

		public bool HasRole => !string.IsNullOrEmpty(_item.Role);

		private string _upsaleValue;
		/// <summary>Результат проверки апсейла — null, пока не проверялся (кнопка "Проверить апсейлы").</summary>
		public string UpsaleValue
		{
			get => _upsaleValue;
			private set
			{
				if (SetProperty(ref _upsaleValue, value))
				{
					OnPropertyChanged(nameof(HasUpsale));
				}
			}
		}

		public bool HasUpsale => !string.IsNullOrEmpty(_upsaleValue);

		public bool CanOpen => !string.IsNullOrEmpty(_item.Href);

		private bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public ICommand OpenCommand { get; }
		public ICommand CopyPermalinkCommand { get; }

		public MarketingItemViewModel(MarketingItem item, INotificationService notificationService)
		{
			_item = item;
			_notificationService = notificationService;

			OpenCommand = new RelayCommand(Open, () => CanOpen);
			CopyPermalinkCommand = new RelayCommand(CopyPermalink, () => !string.IsNullOrEmpty(RawPermalink));
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
			UpsaleValue = string.IsNullOrEmpty(value) ? "Нет данных" : value;
		}
	}
}
