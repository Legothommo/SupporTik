using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SupporTik.Services
{
	/// <summary>
	/// Точка входа для ViewModel'ей фичи "Бинды" — форвардит в CompositionRoot.Current,
	/// который реально владеет списком биндов, регистрацией хоткеев и паузой вставки.
	/// </summary>
	public class BindsServiceAdapter : IBindsService
	{
		private HotkeyRegistrationService Hotkeys => CompositionRoot.Current.Hotkeys;
		private ITextPasteService PasteService => CompositionRoot.Current.PasteService;
		private IHotkeyService HotkeyService => CompositionRoot.Current.HotkeyService;

		public IReadOnlyList<BindKeys> GetBinds() => Hotkeys.BindKeys;

		public void AddBind(BindKeys bind) => Hotkeys.AddBind(bind);

		public void DeleteBind(BindKeys bind) => Hotkeys.DeleteBind(bind);

		public void SaveAndReRegister() => Hotkeys.SaveAndReRegister();

		public void SaveBindsOnly() => Hotkeys.SaveBindsOnly();

		public string GetGroupName(Key key, ModifierKeys modifiers) => Hotkeys.GetGroupName(key, modifiers);

		public void SetGroupName(Key key, ModifierKeys modifiers, string name) => Hotkeys.SetGroupName(key, modifiers, name);

		public bool IsPasteEnabled => !PasteService.IsPaused;

		public void PausePaste() => PasteService.Pause();

		public void ResumePaste() => PasteService.Start();

		public void StartHotkeyCapture(Action<Key, ModifierKeys> onCaptured) => HotkeyService.StartCapture(onCaptured);

		public void CancelHotkeyCapture() => HotkeyService.CancelCapture();

		public void ShowMarketingMenu() => Hotkeys.ShowMarketingMenu();
	}
}
