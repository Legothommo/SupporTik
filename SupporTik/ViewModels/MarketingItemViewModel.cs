using System.Diagnostics;
using System.Windows.Input;
using SupporTik.Classes;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	/// <summary>Одна карточка кампании. StatusKey — сырое значение статуса, View сама решит цвет через конвертер.</summary>
	public class MarketingItemViewModel : ViewModelBase
	{
		private readonly MarketingItem _item;

		/// <summary>Сырой пермалинк (без плейсхолдера "—") — используется при копировании, не для отображения.</summary>
		public string RawPermalink => _item.Permalink;

		public string DisplayPermalink => string.IsNullOrEmpty(_item.Permalink) ? "—" : _item.Permalink;
		public string StatusKey => _item.Status;
		public string DisplayStatus => string.IsNullOrEmpty(_item.Status) ? "—" : _item.Status;
		public string DisplayRemain => string.IsNullOrEmpty(_item.Remain) ? "—" : _item.Remain;

		public string Role => _item.Role;

		/// <summary>Роль показываем только если она реально искалась (был отмечен чекбокс "Роли" при поиске).</summary>
		public bool HasRole => !string.IsNullOrEmpty(_item.Role);

		public bool CanOpen => !string.IsNullOrEmpty(_item.Href);

		private bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public ICommand OpenCommand { get; }

		public MarketingItemViewModel(MarketingItem item)
		{
			_item = item;
			OpenCommand = new RelayCommand(Open, () => CanOpen);
		}

		private void Open()
		{
			if (CanOpen)
			{
				Process.Start(new ProcessStartInfo(_item.Href) { UseShellExecute = true });
			}
		}
	}
}
