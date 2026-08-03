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
		public List<BindKeys> Binds { get; set; }
		public List<BindGroupInfo> Groups { get; set; }
		public ExportSettings Settings { get; set; }
	}

	public class ExportSettings
	{
		public bool StartMinimized { get; set; }
		public bool MinimizeToTray { get; set; }
		public int SelectedKey { get; set; }
		public int SelectedModifiers { get; set; }
	}
}
