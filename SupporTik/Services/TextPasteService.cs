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

		public void Start() => IsPaused = false;

		public void Pause() => IsPaused = true;

		#region Буфер обмена (с повторными попытками)

		// Clipboard.* время от времени бросает исключение "OpenClipboard Failed" (например,
		// если буфер на мгновение занят историей буфера обмена Windows или другим процессом
		// сразу после Ctrl+C). Это транзиентная ошибка — почти всегда решается повторной
		// попыткой через несколько миллисекунд, поэтому вместо однократного вызова используем
		// небольшой retry вместо того, чтобы вставка молча ничего не делала.

		private static async Task<bool> TryClipboardAsync(Action action, int retries = 5, int delayMs = 25)
		{
			for (int attempt = 0; attempt < retries; attempt++)
			{
				try
				{
					action();
					return true;
				}
				catch (Exception ex)
				{
					if (attempt == retries - 1)
					{
						Debug.WriteLine($"Ошибка доступа к буферу обмена: {ex.Message}");
						return false;
					}

					await Task.Delay(delayMs);
				}
			}

			return false;
		}

		private static async Task<(bool Success, T Value)> TryClipboardAsync<T>(Func<T> func, int retries = 5, int delayMs = 25)
		{
			for (int attempt = 0; attempt < retries; attempt++)
			{
				try
				{
					return (true, func());
				}
				catch (Exception ex)
				{
					if (attempt == retries - 1)
					{
						Debug.WriteLine($"Ошибка доступа к буферу обмена: {ex.Message}");
						return (false, default);
					}

					await Task.Delay(delayMs);
				}
			}

			return (false, default);
		}

		#endregion

		#region Вставка текста

		public async Task PasteText(string text)
		{
			if (string.IsNullOrEmpty(text) || IsPaused)
			{
				return;
			}

			bool copied = await TryClipboardAsync(() => Clipboard.SetText(text));
			if (!copied)
			{
				return;
			}

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
			var (_, previousClipboard) = await TryClipboardAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);

			var simulator = new InputSimulator();

			try
			{
				await Task.Delay(50);

				// 2. Очищаем буфер и отправляем Ctrl + C для скопирования выделенного текста
				await TryClipboardAsync(() => Clipboard.Clear());
				simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

				// Небольшая пауза для Windows Clipboard API
				await Task.Delay(100);

				// 3. Если текст успешно скопировался
				var (hasText, selectedText) = await TryClipboardAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);

				if (hasText && !string.IsNullOrEmpty(selectedText))
				{
					Debug.WriteLine($"Выделенный текст для замене: {selectedText}");

					// Маскируем каждый символ звездочкой
					string replacedText = new string('*', selectedText.Length);

					// 4. Помещаем маскированный текст и вставляем (Ctrl + V)
					bool masked = await TryClipboardAsync(() => Clipboard.SetText(replacedText));
					if (masked)
					{
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
					await TryClipboardAsync(() => Clipboard.SetText(previousClipboard));
				}
			}
		}

		#endregion
	}
}
