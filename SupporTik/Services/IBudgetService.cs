using System.Collections.Generic;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public interface IBudgetService
	{
		/// <summary>
		/// Считает точную сумму продления кампании на durationDays дней через
		/// billing/calculate-web-renewal-budget (сам возвращает currentMonthAmount —
		/// со страницы кампании его тащить не нужно). csrfToken/sessionId свои у каждой
		/// кампании, но достаются обычным HTTP GET страницы кампании (без WebView2/JS —
		/// они лежат в HTML открытым текстом, подтверждено вручную), поэтому нужен только
		/// общий cookieHeader сессии yandex.ru, а не отдельная авторизация на страницу.
		/// Возвращает null, если не удалось найти тариф продления (RENEW_*), на котором
		/// сейчас сидит кампания, или на нужный срок в ответе нет цены.
		/// </summary>
		Task<Dictionary<int, string>> CalculateRenewalAmountAsync(string companyPermalink, string campaignId, string cookieHeader, int durationDays);
	}
}
