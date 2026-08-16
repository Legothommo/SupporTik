using SupporTik.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SupporTik.Pages
{
	public partial class MarketingTemplatesPage : Page
	{
		private Point _dragStart;
		private readonly MarketingTemplatesPageViewModel _viewModel;

		public MarketingTemplatesPage()
		{
			InitializeComponent();
			_viewModel = new MarketingTemplatesPageViewModel(
				CompositionRoot.Current.MarketingTemplates,
				new Services.NotificationServiceAdapter());
			DataContext = _viewModel;
		}

		private void TemplatesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			_dragStart = e.GetPosition(TemplatesList);
		}

		private void TemplatesList_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed) return;
			Point current = e.GetPosition(TemplatesList);
			if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
				Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

			if (TemplatesList.SelectedItem is MarketingTemplateItemViewModel item)
			{
				DragDrop.DoDragDrop(TemplatesList, item, DragDropEffects.Move);
			}
		}

		private void TemplatesList_Drop(object sender, DragEventArgs e)
		{
			var source = e.Data.GetData(typeof(MarketingTemplateItemViewModel)) as MarketingTemplateItemViewModel;
			DependencyObject element = e.OriginalSource as DependencyObject;
			while (element != null && !(element is ListBoxItem))
			{
				element = VisualTreeHelper.GetParent(element);
			}

			var target = (element as ListBoxItem)?.DataContext as MarketingTemplateItemViewModel;
			_viewModel.Move(source, target);
		}
	}
}
