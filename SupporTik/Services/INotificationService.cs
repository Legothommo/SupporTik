namespace SupporTik.Services
{
	public interface INotificationService
	{
		void ShowBalloon(string title, string message, bool isWarning);
	}
}
