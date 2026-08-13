using Microsoft.Win32;
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
		public bool IsFollowingSystem => Properties.Settings.Default.FollowSystemTheme;

		/// <summary>Ручной выбор темы всегда отключает слежение за системной — иначе следующая же смена темы в Windows молча перезаписала бы выбор пользователя.</summary>
		public void SetTheme(bool isLight)
		{
			SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
			Properties.Settings.Default.FollowSystemTheme = false;

			ApplyAndPersist(isLight);
		}

		public void SetFollowSystem(bool follow)
		{
			Properties.Settings.Default.FollowSystemTheme = follow;

			SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;

			if (follow)
			{
				SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
				ApplyAndPersist(ReadSystemIsLightTheme());
			}
			else
			{
				Properties.Settings.Default.Save();
			}
		}

		/// <summary>
		/// Вызывается один раз при старте приложения — восстанавливает тему (системную или
		/// последнюю выбранную вручную, смотря что было включено) и, если было включено
		/// слежение, подписывается на дальнейшую смену системной темы на лету.
		/// </summary>
		public static void ApplyStartupTheme()
		{
			bool followSystem = Properties.Settings.Default.FollowSystemTheme;
			bool isLight = followSystem ? ReadSystemIsLightTheme() : Properties.Settings.Default.IsLightTheme;

			Apply(isLight);

			if (followSystem)
			{
				SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
			}
		}

		private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
		{
			// General — самая широкая категория (в неё попадает и смена темы), но фильтровать
			// точнее штатными средствами SystemEvents нельзя; лишний Apply на других событиях
			// этой категории просто применит то же самое значение повторно — не проблема
			if (e.Category == UserPreferenceCategory.General)
			{
				ApplyAndPersist(ReadSystemIsLightTheme());
			}
		}

		private static void ApplyAndPersist(bool isLight)
		{
			Properties.Settings.Default.IsLightTheme = isLight;
			Properties.Settings.Default.Save();

			Apply(isLight);
		}

		/// <summary>Светлая/тёмная тема оформления Windows — HKCU, есть начиная с Windows 10 1607.</summary>
		public static bool ReadSystemIsLightTheme()
		{
			try
			{
				using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
				{
					if (key?.GetValue("AppsUseLightTheme") is int value)
					{
						return value != 0;
					}
				}
			}
			catch (Exception)
			{
				// Ключа нет/недоступен — считаем светлой, как и сама Windows по умолчанию
			}

			return true;
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
