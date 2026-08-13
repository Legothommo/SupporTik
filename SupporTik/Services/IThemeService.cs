namespace SupporTik.Services
{
	public interface IThemeService
	{
		bool IsLightTheme { get; }
		bool IsFollowingSystem { get; }

		void SetTheme(bool isLight);
		void SetFollowSystem(bool follow);
	}
}
