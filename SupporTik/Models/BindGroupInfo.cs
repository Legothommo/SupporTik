using System.Windows.Input;

namespace SupporTik.Classes
{
	/// <summary>
	/// Название группы биндов с общим сочетанием клавиш. Хранится отдельно от самих
	/// биндов — группа как таковая нигде не существует, кроме как "несколько BindKeys
	/// с одинаковыми Key/Modifiers", поэтому имя привязывается к этой паре, а не к id.
	/// </summary>
	public class BindGroupInfo
	{
		public Key Key { get; set; }
		public ModifierKeys Modifiers { get; set; }
		public string Name { get; set; }
	}
}
