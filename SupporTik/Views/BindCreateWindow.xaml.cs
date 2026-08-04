using SupporTik.Classes;
using SupporTik.Services;
using SupporTik.ViewModels.Binds;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SupporTik.Pages
{
	/// <summary>
	/// Логика взаимодействия для BindCreateWindow.xaml. Используется только для создания
	/// нового бинда — редактирование существующих происходит инлайн в карточках BindsPage.
	/// </summary>
	public partial class BindCreateWindow : Window
	{
		public BindKeys ResultBind => _viewModel.ResultBind;

		private readonly BindCreateViewModel _viewModel;
		private readonly IBindsService _bindsService;

		/// <param name="bindsService">Сервис для захвата хоткея через тот же хук, что и вся остальная фича "Бинды".</param>
		/// <param name="presetKey">
		/// Готовое сочетание клавиш — например, "+ Добавить шаблон" внутри группы биндов
		/// с общим хоткеем: пользователь его не выбирает, оно уже задано группой.
		/// </param>
		public BindCreateWindow(IBindsService bindsService, Key? presetKey = null, ModifierKeys presetModifiers = ModifierKeys.None)
		{
			InitializeComponent();

			_bindsService = bindsService;
			_viewModel = new BindCreateViewModel(presetKey, presetModifiers);
			_viewModel.CloseRequested += (s, saved) => DialogResult = saved;
			DataContext = _viewModel;

			if (presetKey.HasValue)
			{
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
			}
		}

		#region Обработка захвата хоткея (Win32-хук — остаётся во View, это не бизнес-логика)

		private void HotkeyCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
		{
			HotkeyCaptureArea.Focus();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("StatusActiveBrush");
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("StatusActiveBrush");
			_viewModel.HotkeyDisplayText = "Нажмите сочетание клавиш...";

			// Захватываем сочетание напрямую через хук — так нажатие достаётся нам раньше,
			// чем его успела бы перехватить сторонняя программа через RegisterHotKey
			_bindsService.StartHotkeyCapture(OnHotkeyCaptured);
		}

		private void OnHotkeyCaptured(Key key, ModifierKeys modifiers)
		{
			_viewModel.OnHotkeyCaptured(key, modifiers);
			TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");
		}

		private void HotkeyCaptureArea_LostFocus(object sender, RoutedEventArgs e)
		{
			_bindsService.CancelHotkeyCapture();
			HotkeyCaptureArea.BorderBrush = (Brush)Application.Current.FindResource("BorderSubtleBrush");

			if (_viewModel.SelectedKey == Key.None)
			{
				_viewModel.HotkeyDisplayText = "Нажмите, чтобы задать хоткей...";
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush");
			}
			else
			{
				TbHotkeyDisplay.Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
			}
		}

		#endregion

		#region Управление окном (шапка, свернуть, закрыть — View-only)

		private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left)
			{
				DragMove();
			}
		}

		private void Minimize_Click(object sender, RoutedEventArgs e)
		{
			WindowState = WindowState.Minimized;
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		#endregion
	}
}
