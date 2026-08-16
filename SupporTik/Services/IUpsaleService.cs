using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	/// <summary>
	/// Проверка апсейлов по DataLens-дашборду (отдельный сайт datalens.yandex-team.ru,
	/// со своей авторизацией — см. MarketingWindow.EnsureDataLensAuthAsync).
	/// </summary>
	public interface IUpsaleService
	{
		/// <summary>
		/// Проверяет апсейл по всем кампаниям параллельно (с ограничением на число
		/// одновременных запросов, один HttpClient на весь пакет). Ошибка по отдельной
		/// кампании не прерывает остальные — для неё в результате будет значение "Ошибка".
		/// progress получает текст вида "Апсейлы N/M..." по мере завершения запросов.
		/// </summary>
		Task<Dictionary<string, string>> CheckUpsalesAsync(
			string cookieHeader,
			string csrfToken,
			IReadOnlyList<string> campaignIds,
			IProgress<string> progress,
			CancellationToken cancellationToken);
	}
}
