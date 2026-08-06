using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SupporTik.Classes
{
	public class BulkObservableCollection<T> : ObservableCollection<T>
	{
		private bool _suppress;

		public void ReplaceRange(IEnumerable<T> items)
		{
			_suppress = true;

			Items.Clear();

			foreach (var item in items)
				Items.Add(item);

			_suppress = false;

			OnCollectionChanged(
				new NotifyCollectionChangedEventArgs(
					NotifyCollectionChangedAction.Reset));
		}

		protected override void OnCollectionChanged(
			NotifyCollectionChangedEventArgs e)
		{
			if (!_suppress)
				base.OnCollectionChanged(e);
		}
	}
}
