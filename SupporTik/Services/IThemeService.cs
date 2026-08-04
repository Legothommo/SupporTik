namespace SupporTik.Services
{
	public interface IThemeService
	{
		bool IsLightTheme { get; }

		void SetTheme(bool isLight);
	}
}
