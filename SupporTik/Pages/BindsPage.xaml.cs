using SupporTik.Services;
using SupporTik.ViewModels.Binds;
using System.Windows.Controls;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для BindsPage.xaml
	/// </summary>
	public partial class BindsPage : Page
	{
		private readonly BindsPageViewModel _viewModel;

		public BindsPage()
		{
			InitializeComponent();

			IBindsService bindsService = new BindsServiceAdapter();
			IMainWindowProvider mainWindowProvider = new MainWindowProvider();
			_viewModel = new BindsPageViewModel(bindsService, mainWindowProvider);
			DataContext = _viewModel;
		}

		/// <summary>Вызывается извне (трей-меню) при переключении паузы вставки.</summary>
		public void UpdateStatus(bool isPaused)
		{
			_viewModel.UpdateStatus(isPaused);
		}
	}
}
