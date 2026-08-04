using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SupporTik.Classes;

namespace SupporTik.Services
{
	public interface IMarketingCampaignService
	{
		/// <summary>
		/// Ищет кампании по UID, проходит по всем страницам пейджера и (если searchRoles)
		/// определяет роль пользователя в каждой найденной кампании. progress получает
		/// текст вида "Роли N/M..." — на время прохода по ролям.
		/// </summary>
		Task<List<MarketingItem>> SearchAsync(string uid, bool searchRoles, IProgress<string> progress);
	}
}
