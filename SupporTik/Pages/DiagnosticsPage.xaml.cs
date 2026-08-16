using SupporTik.Services;
using SupporTik.ViewModels;
using System.Windows.Controls;

namespace SupporTik.Pages
{
	public partial class DiagnosticsPage : Page
	{
		public DiagnosticsPage()
		{
			InitializeComponent();
			DataContext = new DiagnosticsPageViewModel(
				CompositionRoot.Current,
				new NotificationServiceAdapter());
		}
	}
}
