using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SupporTik.Classes;
using SupporTik.Pages;

namespace SupporTik.Controls
{
	/// <summary>
	/// Логика взаимодействия для BindPanel.xaml
	/// </summary>
	public partial class BindPanel : UserControl
	{
		private readonly BindKeys _bind;
		private bool _isMenuOpen = false;

		public event EventHandler ItemDeleted;

		public BindPanel(BindKeys bind)
		{
			InitializeComponent();
			_bind = bind;

			TbName.Text = _bind.Name;
			TbText.Text = _bind.Text;
			TbKeys.Text = KeyExtensions.ToFriendlyShortcut(_bind.Modifiers, _bind.Key);
		}

		#region Управление меню карточки

		private void Menu_Click(object sender, RoutedEventArgs e)
		{
			double targetAngle = _isMenuOpen ? 180 : 0;
			DoubleAnimation rotateAnimation = new DoubleAnimation
			{
				To = targetAngle,
				Duration = TimeSpan.FromSeconds(0.2),
				EasingFunction = new QuadraticEase()
			};
			ArrowTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);

			if (!_isMenuOpen)
			{
				var sb = (Storyboard)FindResource("OpenMenu");
				sb.Begin();
				_isMenuOpen = true;
			}
			else
			{
				var sb = (Storyboard)FindResource("CloseMenu");
				sb.Begin();
				_isMenuOpen = false;
			}
		}

		#endregion

		#region Действия с биндом (Редактирование / Удаление)

		private void EditHotkey_Click(object sender, RoutedEventArgs e)
		{
			App._pasteService.IsPaused = true;

			var addWindow = new BindCreateWindow(_bind)
			{
				Owner = MainWindow.Instance
			};

			if (addWindow.ShowDialog() == true)
			{
				BindKeys newBind = addWindow.ResultBind;

				// Обновление полей напрямую у редактируемого объекта
				_bind.Name = newBind.Name;
				_bind.Text = newBind.Text;
				_bind.Modifiers = newBind.Modifiers;
				_bind.Key = newBind.Key;

				// Сохранение и перерегистрация хоткеев
				App._storageService.SaveData(App._bindKeys);
				App.RegisterDefaultHotkeys();
			}

			App._pasteService.IsPaused = false;

			// Оповещаем родительский View (Page) об обновлении UI
			ItemDeleted?.Invoke(this, EventArgs.Empty);
		}

		private void DeleteHotkey_Click(object sender, RoutedEventArgs e)
		{
			App._bindKeys.Remove(_bind);
			App._storageService.SaveData(App._bindKeys);
			App.RegisterDefaultHotkeys();

			ItemDeleted?.Invoke(this, EventArgs.Empty);
		}

		#endregion
	}
}