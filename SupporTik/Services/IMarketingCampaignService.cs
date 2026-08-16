using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SupporTik.Classes;

namespace SupporTik.Services
{
	public interface IMarketingCampaignService
	{
		/// <summary>
		/// Ищет кампании по UID через прямой API-запрос (постраничный список). Роль
		/// пользователя в каждой кампании приходит в том же ответе (массив users) —
		/// отдельного прохода по страницам настроек больше не требуется.
		/// progress получает текст вида "Страница N..." на время пагинации.
		/// </summary>
		Task<List<MarketingItem>> SearchAsync(
			string uid,
			YandexBusinessAuth auth,
			IProgress<string> progress,
			CancellationToken cancellationToken);
	}
}
