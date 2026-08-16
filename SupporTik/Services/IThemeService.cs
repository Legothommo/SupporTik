namespace SupporTik.Services
{
	public interface IThemeService
	{
		event System.EventHandler ThemeChanged;
		bool IsLightTheme { get; }
		bool IsFollowingSystem { get; }

		void SetTheme(bool isLight);
		void SetFollowSystem(bool follow);
	}
}
