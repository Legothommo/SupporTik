using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SupporTik.Classes
{
	public class BulkObservableCollection<T> : ObservableCollection<T>
	{
		private bool _suppress;

		public void ReplaceRange(IEnumerable<T> items)
		{
			_suppress = true;
			try
			{
				Items.Clear();

				foreach (var item in items)
				{
					Items.Add(item);
				}
			}
			finally
			{
				_suppress = false;
			}

			OnCollectionChanged(
				new NotifyCollectionChangedEventArgs(
					NotifyCollectionChangedAction.Reset));
		}

		protected override void OnCollectionChanged(
			NotifyCollectionChangedEventArgs e)
		{
			if (!_suppress)
			{
				base.OnCollectionChanged(e);
			}
		}
	}
}
