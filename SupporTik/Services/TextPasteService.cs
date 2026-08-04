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

		/// <summary>
		/// Опрашивает буфер обмена короткими интервалами, пока isReady не увидит нужное
		/// содержимое (до maxAttempts раз с паузой delayMs), вместо того чтобы один раз
		/// подождать фиксированное время и надеяться, что буфер уже готов. В обычном случае
		/// это быстрее фиксированной паузы, а в редком медленном — надёжнее.
		/// </summary>
		private static async Task<(bool Success, string Value)> WaitForClipboardAsync(Func<string, bool> isReady, int maxAttempts, int delayMs)
		{
			for (int attempt = 0; attempt < maxAttempts; attempt++)
			{
				var (ok, text) = await TryClipboardAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, retries: 1, delayMs: 0);

				if (ok && isReady(text))
				{
					return (true, text);
				}

				await Task.Delay(delayMs);
			}

			return (false, null);
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

			// Ждём подтверждения, что буфер реально содержит нужный текст, вместо фиксированной
			// паузы — SetText синхронный, поэтому обычно это подтверждается почти сразу
			await WaitForClipboardAsync(t => t == text, maxAttempts: 8, delayMs: 10);

			// Небольшая безусловная пауза перед Ctrl+V — не для буфера (он уже подтверждён
			// выше), а чтобы вызывающий код гарантированно получил управление обратно ДО
			// самого нажатия. Если проверка буфера подтвердилась с первой попытки, весь метод
			// до этого момента мог выполниться полностью синхронно — а вызовы вроде
			// QuickTextWindow полагаются на то, что успеют скрыться и вернуть фокус целевому
			// приложению раньше, чем сработает Ctrl+V (см. QuickMenuEntryViewModel)
			await Task.Delay(20);

			// Имитируем Ctrl + V
			var simulator = new InputSimulator();
			simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);

			// Буфер намеренно не восстанавливаем обратно: момент, когда целевое приложение
			// реально прочитало Ctrl+V, мы со своей стороны не видим (в отличие от факта
			// появления текста в буфере), а произвольная пауза перед перезаписью буфера
			// слишком часто оказывается короче, чем нужно целевому приложению — тогда либо
			// вставляется старое содержимое буфера, либо приложение вовсе ловит ошибку доступа
			// к буферу из-за гонки с нашей записью, и вставка молча не срабатывает
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

				// 3. Ждём, пока в буфере реально появится скопированный текст — буфер только
				// что очищен, поэтому "не пусто" однозначно значит "скопировалось", и не нужно
				// гадать с фиксированной паузой, сколько времени на это уйдёт у целевого приложения
				var (hasText, selectedText) = await WaitForClipboardAsync(t => !string.IsNullOrEmpty(t), maxAttempts: 15, delayMs: 20);

				if (hasText && !string.IsNullOrEmpty(selectedText))
				{
					Debug.WriteLine($"Выделенный текст для замене: {selectedText}");

					// Маскируем каждый символ звездочкой
					string replacedText = new string('*', selectedText.Length);

					// 4. Помещаем маскированный текст и вставляем (Ctrl + V)
					bool masked = await TryClipboardAsync(() => Clipboard.SetText(replacedText));
					if (masked)
					{
						await WaitForClipboardAsync(t => t == replacedText, maxAttempts: 8, delayMs: 10);

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
