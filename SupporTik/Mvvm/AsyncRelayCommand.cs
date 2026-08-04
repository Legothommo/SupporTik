using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SupporTik.Mvvm
{
	/// <summary>
	/// Команда для async-обработчиков. Пока предыдущий вызов не завершился,
	/// CanExecute возвращает false — защита от повторного запуска по двойному клику.
	/// </summary>
	public class AsyncRelayCommand : ICommand
	{
		private readonly Func<Task> _execute;
		private readonly Func<bool> _canExecute;
		private bool _isRunning;

		public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
		{
			_execute = execute ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute;
		}

		public event EventHandler CanExecuteChanged
		{
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public bool CanExecute(object parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

		public async void Execute(object parameter)
		{
			if (!CanExecute(parameter))
			{
				return;
			}

			_isRunning = true;
			CommandManager.InvalidateRequerySuggested();

			try
			{
				await _execute();
			}
			finally
			{
				_isRunning = false;
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}
}
