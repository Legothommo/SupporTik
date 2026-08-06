using System;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	/// <summary>
	/// Один пункт в выпадающей панели фильтра (статус/роль/апсейл) — независимая
	/// галка, а не радио-выбор. Переиспользуется для всех трёх фильтров карточек.
	/// </summary>
	public class FilterOption : ViewModelBase
	{
		private readonly Action _onChanged;

		public string Value { get; }

		private bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (SetProperty(ref _isSelected, value))
				{
					_onChanged?.Invoke();
				}
			}
		}

		public FilterOption(string value, bool isSelected, Action onChanged)
		{
			Value = value;
			_isSelected = isSelected;
			_onChanged = onChanged;
		}
	}
}
