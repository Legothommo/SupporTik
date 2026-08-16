using System.Collections.Generic;

namespace SupporTik.Classes
{
	/// <summary>
	/// Полный набор данных для экспорта/импорта: бинды, названия групп и настройки,
	/// связанные с работой хоткеев (чтобы перенос на другую машину/переустановку
	/// не требовал заново настраивать всё руками).
	/// </summary>
	public class ExportPackage
	{
		public int Version { get; set; }
		public List<BindKeys> Binds { get; set; }
		public List<BindGroupInfo> Groups { get; set; }
		public ExportSettings Settings { get; set; }
		public List<MarketingTextTemplate> MarketingTemplates { get; set; }
	}

	public class ExportSettings
	{
		public bool StartMinimized { get; set; }
		public bool MinimizeToTray { get; set; }
		public int SelectedKey { get; set; }
		public int SelectedModifiers { get; set; }
		public int MarketingMenuKey { get; set; }
		public int MarketingMenuModifiers { get; set; }
		public bool MarketingMenuFromLeft { get; set; }
		public bool IsLightTheme { get; set; }
		public bool FollowSystemTheme { get; set; }
		public bool AutoStartEnabled { get; set; }
		public string RecentMarketingUids { get; set; }
	}
}
