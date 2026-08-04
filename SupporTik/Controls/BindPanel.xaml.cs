using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupporTik.ViewModels.Binds;

namespace SupporTik.Controls
{
	/// <summary>
	/// Карточка одиночного бинда. Название и текст редактируются прямо тут (инлайн,
	/// Enter/фокус вовне — сохранить, Escape — отменить), окно используется только
	/// для создания новых биндов. Данные и команды приходят через DataContext.
	/// </summary>
	public partial class BindPanel : UserControl
	{
		private BindItemViewModel _viewModel;

		// "Проглатывают" LostFocus сразу после Escape, чтобы отмена не переоткрывалась
		// автосохранением при потере фокуса скрывшимся полем — тот же приём, что и в BindGroupPanel
		private bool _suppressNameLostFocusSave;
		private bool _suppressTextLostFocusSave;

		public BindPanel()
		{
			InitializeComponent();
			DataContextChanged += BindPanel_DataContextChanged;
		}

		private void BindPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			_viewModel = e.NewValue as BindItemViewModel;
		}

		private void NameInput_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				Keyboard.ClearFocus(); // LostFocus сохранит и закроет редактор
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_suppressNameLostFocusSave = true;
				_viewModel?.CancelEditNameCommand.Execute(null);
			}
		}

		private void NameInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressNameLostFocusSave)
			{
				_suppressNameLostFocusSave = false;
				return;
			}

			_viewModel?.SaveNameCommand.Execute(null);
		}

		private void TextInput_KeyDown(object sender, KeyEventArgs e)
		{
			// Enter здесь — обычный перенос строки (текст шаблона может быть многострочным),
			// сохранение — по уходу фокуса; отдельно обрабатываем только отмену по Escape
			if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_suppressTextLostFocusSave = true;
				_viewModel?.CancelEditTextCommand.Execute(null);
			}
		}

		private void TextInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressTextLostFocusSave)
			{
				_suppressTextLostFocusSave = false;
				return;
			}

			_viewModel?.SaveTextCommand.Execute(null);
		}
	}
}
