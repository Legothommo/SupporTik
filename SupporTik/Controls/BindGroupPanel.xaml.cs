using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SupporTik.Classes;
using SupporTik.Pages;

namespace SupporTik.Controls
{
	/// <summary>
	/// Блок ("папка") для нескольких шаблонов, у которых одно и то же сочетание клавиш.
	/// Хоткей показывается один раз в заголовке, а не на каждой карточке отдельно.
	/// </summary>
	public partial class BindGroupPanel : UserControl
	{
		private List<BindKeys> _binds;
		private Key _groupKey;
		private ModifierKeys _groupModifiers;

		public event EventHandler ItemDeleted;

		public BindGroupPanel(List<BindKeys> binds)
		{
			InitializeComponent();
			SetBinds(binds);
		}

		public void SetBinds(List<BindKeys> binds)
		{
			_binds = binds;
			sp_rows.Children.Clear();

			var firstBind = binds.First();
			_groupKey = firstBind.Key;
			_groupModifiers = firstBind.Modifiers;

			TbKeys.Text = KeyExtensions.ToFriendlyShortcut(_groupModifiers, _groupKey);
			UpdateTitleDisplay();

			for (int i = 0; i < binds.Count; i++)
			{
				sp_rows.Children.Add(BuildRow(binds[i]));

				if (i < binds.Count - 1)
				{
					sp_rows.Children.Add(new Separator
					{
						Style = (Style)Application.Current.FindResource("TraySeparatorStyle")
					});
				}
			}
		}

		#region Название группы

		private void UpdateTitleDisplay()
		{
			string customName = App.GetGroupName(_groupKey, _groupModifiers);
			string countText = $"{_binds.Count} {TemplateWord(_binds.Count)}";

			if (!string.IsNullOrEmpty(customName))
			{
				TbGroupTitle.Text = customName;
				TbGroupTitle.FontWeight = FontWeights.SemiBold;
				TbGroupTitle.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
				TbGroupSubtitle.Text = countText;
				TbGroupSubtitle.Visibility = Visibility.Visible;
			}
			else
			{
				TbGroupTitle.Text = countText;
				TbGroupTitle.FontWeight = FontWeights.Normal;
				TbGroupTitle.Foreground = (Brush)Application.Current.FindResource("TextSecondary");
				TbGroupSubtitle.Text = string.Empty;
				TbGroupSubtitle.Visibility = Visibility.Collapsed;
			}
		}

		private void BtnRenameGroup_Click(object sender, RoutedEventArgs e)
		{
			TbGroupNameInput.Text = App.GetGroupName(_groupKey, _groupModifiers) ?? string.Empty;

			sp_titleDisplay.Visibility = Visibility.Collapsed;
			TbGroupNameInput.Visibility = Visibility.Visible;

			TbGroupNameInput.Focus();
			TbGroupNameInput.SelectAll();
		}

		// Скрытие TextBox (Visibility = Collapsed) само по себе снимает с него фокус и
		// вызывает LostFocus — этот флаг не даёт Escape "откатить" имя, а потом LostFocus
		// снова его сохранить поверх отмены
		private bool _suppressLostFocusSave;

		private void TbGroupNameInput_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				Keyboard.ClearFocus(); // LostFocus сохранит и закроет редактор
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_suppressLostFocusSave = true;
				CloseNameEditor(save: false);
			}
		}

		private void TbGroupNameInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressLostFocusSave)
			{
				_suppressLostFocusSave = false;
				return;
			}

			CloseNameEditor(save: true);
		}

		private void CloseNameEditor(bool save)
		{
			if (save)
			{
				App.SetGroupName(_groupKey, _groupModifiers, TbGroupNameInput.Text);
				UpdateTitleDisplay();
			}

			TbGroupNameInput.Visibility = Visibility.Collapsed;
			sp_titleDisplay.Visibility = Visibility.Visible;
		}

		#endregion

		private static string TemplateWord(int count)
		{
			int mod100 = count % 100;
			int mod10 = count % 10;

			if (mod100 >= 11 && mod100 <= 14) return "шаблонов";
			if (mod10 == 1) return "шаблон";
			if (mod10 >= 2 && mod10 <= 4) return "шаблона";
			return "шаблонов";
		}

		/// <summary>Поиск проверяет и общий хоткей группы, и каждый входящий в неё шаблон.</summary>
		public bool Matches(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				return true;
			}

			if (TbKeys.Text.Trim().ToLower().Contains(query))
			{
				return true;
			}

			return _binds.Any(b =>
				b.Name.ToLower().Contains(query) ||
				b.Text.ToLower().Contains(query));
		}

		private UIElement BuildRow(BindKeys bind)
		{
			var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			var textStack = new StackPanel();

			var nameBlock = new TextBlock
			{
				Text = bind.Name,
				Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
				FontWeight = FontWeights.SemiBold,
				FontSize = 13,
				TextWrapping = TextWrapping.Wrap
			};

			var textBlock = new TextBlock
			{
				Text = bind.Text,
				Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
				FontSize = 12,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(0, 2, 0, 0)
			};

			textStack.Children.Add(nameBlock);
			textStack.Children.Add(textBlock);
			Grid.SetColumn(textStack, 0);

			var editBtn = new Button
			{
				Content = "✏",
				Width = 28,
				Height = 28,
				Margin = new Thickness(6, 0, 0, 0),
				Style = (Style)Application.Current.FindResource("EditBtnStyle")
			};
			editBtn.Click += (s, e) => EditBind(bind);
			Grid.SetColumn(editBtn, 1);

			var deleteBtn = new Button
			{
				Content = "✕",
				Width = 28,
				Height = 28,
				Margin = new Thickness(6, 0, 0, 0),
				Style = (Style)Application.Current.FindResource("DeleteBtnStyle")
			};
			deleteBtn.Click += (s, e) => DeleteBind(bind);
			Grid.SetColumn(deleteBtn, 2);

			grid.Children.Add(textStack);
			grid.Children.Add(editBtn);
			grid.Children.Add(deleteBtn);

			return grid;
		}

		private void EditBind(BindKeys bind)
		{
			// Запоминаем состояние на случай, если пользователь уже поставил перехват на паузу
			// вручную (через трей) — диалог не должен снимать эту паузу за него
			bool wasPaused = App._pasteService.IsPaused;
			App._pasteService.Pause();

			var editWindow = new BindCreateWindow(bind)
			{
				Owner = MainWindow.Instance
			};

			if (editWindow.ShowDialog() == true)
			{
				BindKeys newBind = editWindow.ResultBind;

				bind.Name = newBind.Name;
				bind.Text = newBind.Text;
				bind.Modifiers = newBind.Modifiers;
				bind.Key = newBind.Key;

				App._storageService.SaveData(App._bindKeys);
				App.RegisterDefaultHotkeys();
			}

			if (!wasPaused)
			{
				App._pasteService.Start();
			}

			// Хоткей мог измениться — бинд, возможно, больше не входит в эту группу,
			// поэтому просим страницу перестроить список целиком
			ItemDeleted?.Invoke(this, EventArgs.Empty);
		}

		private void DeleteBind(BindKeys bind)
		{
			App._bindKeys.Remove(bind);
			App._storageService.SaveData(App._bindKeys);
			App.RegisterDefaultHotkeys();

			ItemDeleted?.Invoke(this, EventArgs.Empty);
		}

		private void BtnAddToGroup_Click(object sender, RoutedEventArgs e)
		{
			bool wasPaused = App._pasteService.IsPaused;
			App._pasteService.Pause();

			var firstBind = _binds.First();
			var seed = new BindKeys { Key = firstBind.Key, Modifiers = firstBind.Modifiers };

			var addWindow = new BindCreateWindow(seed, presetHotkeyOnly: true)
			{
				Owner = MainWindow.Instance
			};

			if (addWindow.ShowDialog() == true)
			{
				App._bindKeys.Add(addWindow.ResultBind);
				App._storageService.SaveData(App._bindKeys);
				App.RegisterDefaultHotkeys();
			}

			if (!wasPaused)
			{
				App._pasteService.Start();
			}

			ItemDeleted?.Invoke(this, EventArgs.Empty);
		}
	}
}
