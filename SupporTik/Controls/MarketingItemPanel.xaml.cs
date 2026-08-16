using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SupporTik.ViewModels;
using SupporTik.Helpers;

namespace SupporTik.Controls
{
	/// <summary>
	/// Карточка одной рекламной кампании (MarketingWindow). Данные и команда открытия
	/// приходят через DataContext (MarketingItemViewModel) — во View остаётся только
	/// подсветка рамки при выборе (не биндится напрямую, как и в BindPanel/BindGroupPanel).
	/// </summary>
	public partial class MarketingItemPanel : UserControl
	{
		private MarketingItemViewModel _viewModel;

		public MarketingItemPanel()
		{
			InitializeComponent();
			DataContextChanged += MarketingItemPanel_DataContextChanged;
		}

		private void MarketingItemPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (_viewModel != null)
			{
				_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			}

			_viewModel = e.NewValue as MarketingItemViewModel;

			if (_viewModel != null)
			{
				_viewModel.PropertyChanged += ViewModel_PropertyChanged;
			}
		}

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MarketingItemViewModel.IsSelected))
			{
				Card.BorderBrush = _viewModel.IsSelected ? (Brush)Application.Current.FindResource("AccentGreen") : null;
			}
		}

		private void CopyUpsale_Click(object sender, RoutedEventArgs e)
		{
			if (_viewModel == null || !(sender is FrameworkElement button)) return;
			MarketingTemplateMenu.Open(
				button,
				_viewModel.GetCompatibleTemplates(),
				template => _viewModel.CopyWithTemplateCommand.Execute(template));
		}
	}
}
