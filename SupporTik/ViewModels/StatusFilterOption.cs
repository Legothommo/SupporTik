using System;
using SupporTik.Mvvm;

namespace SupporTik.ViewModels
{
	/// <summary>Один статус в выпадающей панели фильтра — независимая галка, а не радио-выбор.</summary>
	public class StatusFilterOption : ViewModelBase
	{
		private readonly Action _onChanged;

		public string Status { get; }

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

		public StatusFilterOption(string status, bool isSelected, Action onChanged)
		{
			Status = status;
			_isSelected = isSelected;
			_onChanged = onChanged;
		}
	}
}
