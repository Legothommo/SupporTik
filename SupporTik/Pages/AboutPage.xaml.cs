using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SupporTik.ViewModels;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для AboutPage.xaml
	/// </summary>
	public partial class AboutPage : Page
	{
		private const string RepositoryUrl = "https://github.com/Legothommo/SupporTik";

		public AboutPage()
		{
			InitializeComponent();
			DataContext = new AboutPageViewModel();
		}

		private void BtnOpenGitHub_Click(object sender, RoutedEventArgs e)
		{
			Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
		}
	}
}
