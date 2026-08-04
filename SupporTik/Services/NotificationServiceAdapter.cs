using Hardcodet.Wpf.TaskbarNotification;

namespace SupporTik.Services
{
	public class NotificationServiceAdapter : INotificationService
	{
		public void ShowBalloon(string title, string message, bool isWarning)
		{
			CompositionRoot.Current.NotifyIcon?.ShowBalloonTip(title, message, isWarning ? BalloonIcon.Warning : BalloonIcon.None);
		}
	}
}
