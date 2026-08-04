using System.Windows;

namespace SupporTik.Services
{
	/// <summary>
	/// Заменяет прямые обращения к статическому MainWindow.Instance (тот же
	/// антипаттерн, что и App._xxx, только для окон) — используется как Owner
	/// для модальных диалогов.
	/// </summary>
	public interface IMainWindowProvider
	{
		Window Current { get; }
	}

	public class MainWindowProvider : IMainWindowProvider
	{
		public Window Current => MainWindow.Instance;
	}
}
