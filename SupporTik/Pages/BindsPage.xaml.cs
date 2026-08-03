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

			var groups = App._bindKeys
				.GroupBy(b => new { b.Key, b.Modifiers })
				.OrderBy(g => g.Key.Modifiers)
				.ThenBy(g => g.Key.Key);

			foreach (var group in groups)
			{
				var binds = group.ToList();

				if (binds.Count == 1)
				{
					var panel = new BindPanel(binds[0]);
					panel.ItemDeleted += BindPanel_ItemDeleted;
					sp_list.Children.Add(panel);
				}
				else
				{
					// Несколько шаблонов на одном сочетании клавиш — показываем одним блоком,
					// а не отдельными карточками с повторяющимся хоткеем
					var groupPanel = new BindGroupPanel(binds);
					groupPanel.ItemDeleted += BindPanel_ItemDeleted;
					sp_list.Children.Add(groupPanel);
				}
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

				// 3. Сохраняем в JSON / регистрируем хоткей
				App._storageService.SaveData(App._bindKeys);
				App.RegisterDefaultHotkeys();

				// 4. Перестраиваем карточки — обязательно ПОСЛЕ RegisterDefaultHotkeys(),
				// иначе он перечитает App._bindKeys с диска и подменит объекты в списке,
				// а уже созданная карточка будет ссылаться на "осиротевший" экземпляр:
				// первое редактирование правило бы его, а сохранялся бы актуальный список
				LoadBindPanels();
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
				if (child is BindPanel card)
				{
					// Проверяем, содержится ли поиск в свойствах вашей карточки/модели
					bool isVisible = string.IsNullOrWhiteSpace(query) ||
									 card.TbKeys.Text.Trim().ToLower().Contains(query) ||
									 card.TbText.Text.Trim().ToLower().Contains(query);

					// Скрываем или показываем карточку
					card.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
				}
				else if (child is BindGroupPanel group)
				{
					group.Visibility = group.Matches(query) ? Visibility.Visible : Visibility.Collapsed;
				}
			}
		}
		public void UpdateStatus(bool isEnabled)
		{
			if (!isEnabled)
			{
				Status.Fill = (Brush)FindResource("StatusActiveBrush");
				TbText.Text = "Перехват клавиш активен";
			}
			else
			{
				Status.Fill = (Brush)FindResource("StatusPausedBrush");
				TbText.Text = "Перехват клавиш выключен";
			}
		}
	}
}
