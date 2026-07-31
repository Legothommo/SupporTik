using System;
using System.Windows;
using System.Diagnostics;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace SupporTik.Services
{
	public class TextPasteService : ITextPasteService
	{
		public bool IsPaused { get; set; } = false;

		#region Вставка текста

		public async Task PasteText(string text)
		{
			if (string.IsNullOrEmpty(text) || IsPaused)
			{
				return;
			}

			// Помещаем текст в буфер
			Clipboard.SetText(text);

			await Task.Delay(100);

			// Имитируем Ctrl + V
			var simulator = new InputSimulator();
			simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
		}

		#endregion

		#region Замена выделения (NDA Masking)

		public async Task ReplaceSelectionInExternalApp()
		{
			if (IsPaused)
			{
				return;
			}

			// 1. Сохраняем предыдущее содержимое буфера обмена
			string previousClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
			var simulator = new InputSimulator();

			try
			{
				await Task.Delay(50);

				// 2. Очищаем буфер и отправляем Ctrl + C для скопирования выделенного текста
				Clipboard.Clear();
				simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

				// Небольшая пауза для Windows Clipboard API
				await Task.Delay(100);

				// 3. Если текст успешно скопировался
				if (Clipboard.ContainsText())
				{
					string selectedText = Clipboard.GetText();
					Debug.WriteLine($"Выделенный текст для замене: {selectedText}");

					if (!string.IsNullOrEmpty(selectedText))
					{
						// Маскируем каждый символ звездочкой
						string replacedText = new string('*', selectedText.Length);

						// 4. Помещаем маскированный текст и вставляем (Ctrl + V)
						Clipboard.SetText(replacedText);
						await Task.Delay(50);

						simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
						await Task.Delay(50);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Ошибка при NDA замене текста: {ex.Message}");
			}
			finally
			{
				// 5. Возвращаем исходное значение в буфер обмена
				if (!string.IsNullOrEmpty(previousClipboard))
				{
					Clipboard.SetText(previousClipboard);
				}
			}
		}

		#endregion
	}
}