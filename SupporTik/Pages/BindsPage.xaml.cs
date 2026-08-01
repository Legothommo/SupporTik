using SupporTik.Classes;
using SupporTik.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для BindsPage.xaml
	/// </summary>
	public partial class BindsPage : Page
	{
		public event EventHandler EnableText;
		public BindsPage()
		{
			InitializeComponent();
			LoadBindPanels();
			UpdateStatus(App._pasteService.IsPaused);
		}
		private void LoadBindPanels()
		{
			sp_list.Children.Clear();

			var sortedBinds = App._bindKeys.OrderBy(b => b.Modifiers).ThenBy(b => b.Key);

			foreach (var bind in sortedBinds)
			{
				var panel = new BindPanel(bind);

				panel.ItemDeleted += BindPanel_ItemDeleted;
				sp_list.Children.Add(panel);

			}
		}
		private void BindPanel_ItemDeleted(object sender, EventArgs e)
		{
			LoadBindPanels();
		}
		private void BtnAddBind(object sender, RoutedEventArgs e)
		{
			// Запоминаем состояние на случай, если пользователь уже поставил перехват на паузу
			// вручную (через трей) — диалог не должен снимать эту паузу за него
			bool wasPaused = App._pasteService.IsPaused;
			App._pasteService.Pause();

			var addWindow = new BindCreateWindow();
			addWindow.Owner = MainWindow.Instance; // Привязываем к главному окну

			if (addWindow.ShowDialog() == true)
			{
				BindKeys newBind = addWindow.ResultBind;

				// 2. Добавляем в коллекцию
				App._bindKeys.Add(newBind);

				// 3. Добавляем карточку BindPanel в UI
				LoadBindPanels();

				// 4. Сохраняем в JSON / регистрируем хоткей
				App._storageService.SaveData(App._bindKeys);
				App.RegisterDefaultHotkeys();

			}

			if (!wasPaused)
			{
				App._pasteService.Start();
			}
		}
		private void TbSearch_TextChanged(object sender, TextChangedEventArgs e)
		{
			string query = TbSearch.Text.Trim().ToLower();

			foreach (var child in sp_list.Children)
			{
				if (child is BindPanel card) // ваш UserControl карточки
				{
					// Проверяем, содержится ли поиск в свойствах вашей карточки/модели
					bool isVisible = string.IsNullOrWhiteSpace(query) ||
									 card.TbKeys.Text.Trim().ToLower().Contains(query) ||
									 card.TbText.Text.Trim().ToLower().Contains(query);

					// Скрываем или показываем карточку
					card.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
				}
			}
		}
		public void UpdateStatus(bool isEnabled)
		{
			if (!isEnabled)
			{
				Status.Fill = (Brush)Application.Current.FindResource("AccentGreen");
				TbText.Text = "Перехват клавиш активен";
			}
			else
			{
				Status.Fill = (Brush)Application.Current.FindResource("AccentCoral");
				TbText.Text = "Перехват клавиш выключен";
			}
		}
	}
}
