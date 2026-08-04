using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace SupporTik.Services
{
	/// <summary>
	/// Переключение светлой/тёмной темы. Помимо встроенной темы WPF-UI приходится отдельно
	/// подменять наш собственный оверлей палитры (Styles/Colors.xaml или ColorsLight.xaml) —
	/// он перекрывает часть ключевых ресурсов WPF-UI (ApplicationBackgroundBrush,
	/// TextFillColorPrimaryBrush и т.п.) своими пастельными значениями, и сама по себе смена
	/// ApplicationTheme на эти перекрытые ключи не действует.
	/// </summary>
	public class ThemeService : IThemeService
	{
		private static readonly Color AccentColor = Color.FromRgb(0x9A, 0xA3, 0xEB);

		public bool IsLightTheme => Properties.Settings.Default.IsLightTheme;

		public void SetTheme(bool isLight)
		{
			Properties.Settings.Default.IsLightTheme = isLight;
			Properties.Settings.Default.Save();

			Apply(isLight);
		}

		/// <summary>Вызывается и при старте приложения (по сохранённой настройке), и при переключении тумблером.</summary>
		public static void Apply(bool isLight)
		{
			var theme = isLight ? ApplicationTheme.Light : ApplicationTheme.Dark;

			ApplicationThemeManager.Apply(theme, updateAccent: false);
			ApplicationAccentColorManager.Apply(AccentColor, theme, false);

			ApplyColorPalette(isLight);
		}

		private static void ApplyColorPalette(bool isLight)
		{
			var dictionaries = Application.Current.Resources.MergedDictionaries;
			string newSource = isLight ? "Styles/ColorsLight.xaml" : "Styles/Colors.xaml";

			for (int i = 0; i < dictionaries.Count; i++)
			{
				string path = dictionaries[i].Source?.OriginalString ?? string.Empty;

				if (path.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith("ColorsLight.xaml", StringComparison.OrdinalIgnoreCase))
				{
					dictionaries[i] = new ResourceDictionary { Source = new Uri(newSource, UriKind.Relative) };
					return;
				}
			}
		}
	}
}
