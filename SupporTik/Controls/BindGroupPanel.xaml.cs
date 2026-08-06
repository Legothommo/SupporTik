using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupporTik.ViewModels.Binds;

namespace SupporTik.Controls
{
	/// <summary>
	/// Блок ("папка") для нескольких шаблонов, у которых одно и то же сочетание клавиш.
	/// Данные и команды приходят через DataContext (BindGroupViewModel) — во View
	/// остаётся только состояние фокуса при переименовании (см. TbGroupNameInput_*)
	/// и то, что не биндится напрямую (Focus()/SelectAll() при входе в редактирование).
	/// </summary>
	public partial class BindGroupPanel : UserControl
	{
		private BindGroupViewModel _viewModel;

		// Скрытие TextBox (Visibility = Collapsed) само по себе снимает с него фокус и
		// вызывает LostFocus — этот флаг не даёт Escape "откатить" имя, а потом LostFocus
		// снова его сохранить поверх отмены
		private bool _suppressLostFocusSave;

		public BindGroupPanel()
		{
			InitializeComponent();
			DataContextChanged += BindGroupPanel_DataContextChanged;
		}

		private void BindGroupPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (_viewModel != null)
			{
				_viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			}

			_viewModel = e.NewValue as BindGroupViewModel;

			if (_viewModel != null)
			{
				_viewModel.PropertyChanged += ViewModel_PropertyChanged;
			}
		}

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(BindGroupViewModel.IsRenamingGroup))
			{
				bool renaming = _viewModel.IsRenamingGroup;
				sp_titleDisplay.Visibility = renaming ? Visibility.Collapsed : Visibility.Visible;
				TbGroupNameInput.Visibility = renaming ? Visibility.Visible : Visibility.Collapsed;

				if (renaming)
				{
					TbGroupNameInput.Focus();
					TbGroupNameInput.SelectAll();
				}
			}
		}

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
				_viewModel.CancelRenameCommand.Execute(null);
			}
		}

		private void TbGroupNameInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressLostFocusSave)
			{
				_suppressLostFocusSave = false;
				return;
			}

			_viewModel.SaveGroupNameCommand.Execute(null);
		}

		private void BrGroupKeys_MouseDown(object sender, MouseButtonEventArgs e)
		{
			e.Handled = true;
			((UIElement)sender).Focus();
			_viewModel?.StartEditKeysCommand.Execute(null);
		}

		private void BrGroupKeys_LostFocus(object sender, RoutedEventArgs e)
		{
			_viewModel?.CancelEditKeysCommand.Execute(null);
		}

		#region Инлайн-редактирование строк группы (название/текст шаблона)

		// Строки строятся из DataTemplate — своего code-behind у них нет, поэтому вместо
		// прямого доступа по имени берём DataContext конкретного элемента через sender
		private bool _suppressRowNameLostFocusSave;
		private bool _suppressRowTextLostFocusSave;

		private void RowNameInput_KeyDown(object sender, KeyEventArgs e)
		{
			var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
			if (vm == null)
			{
				return;
			}

			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				Keyboard.ClearFocus();
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_suppressRowNameLostFocusSave = true;
				vm.CancelEditNameCommand.Execute(null);
			}
		}

		private void RowNameInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressRowNameLostFocusSave)
			{
				_suppressRowNameLostFocusSave = false;
				return;
			}

			var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
			vm?.SaveNameCommand.Execute(null);
		}

		private void RowTextInput_KeyDown(object sender, KeyEventArgs e)
		{
			// Enter здесь — обычный перенос строки (текст шаблона может быть многострочным)
			if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_suppressRowTextLostFocusSave = true;
				var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
				vm?.CancelEditTextCommand.Execute(null);
			}
		}

		private void RowTextInput_LostFocus(object sender, RoutedEventArgs e)
		{
			if (_suppressRowTextLostFocusSave)
			{
				_suppressRowTextLostFocusSave = false;
				return;
			}

			var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
			vm?.SaveTextCommand.Execute(null);
		}

		// Сочетание клавиш у конкретного шаблона внутри группы — своё, независимое от
		// общего хоткея группы (BrGroupKeys_* выше). Если задать его отличным от соседей,
		// после SaveAndReRegister шаблон при пересборке списка (см. BindsPageViewModel.
		// ReloadRows) сам перегруппируется — окажется отдельной карточкой или в другой группе.
		private void RowKeys_MouseDown(object sender, MouseButtonEventArgs e)
		{
			e.Handled = true;
			var element = (UIElement)sender;
			element.Focus();

			var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
			vm?.StartEditKeysCommand.Execute(null);
		}

		private void RowKeys_LostFocus(object sender, RoutedEventArgs e)
		{
			// DataContext, а не жёсткий каст: успешный захват сочетания (см.
			// BindItemViewModel.OnKeysCaptured) синхронно поднимает RequestReload, который
			// пересобирает весь список карточек — контейнер этой строки к моменту LostFocus
			// уже мог быть отсоединён от ItemsControl, и WPF временно подставляет туда
			// служебный DisconnectedItem (MS.Internal.NamedObject) вместо реальной VM
			var vm = ((FrameworkElement)sender).DataContext as BindItemViewModel;
			vm?.CancelEditKeysCommand.Execute(null);
		}

		#endregion
	}
}
