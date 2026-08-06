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
		///
		/// Результат: result[0] — сумма продления, result[1] — округлённый прогноз
		/// (monthPrediction.to), result[2] — "True"/"False" (isMulti из ответа get-campaign-v3,
		/// есть в ответе — берётся его значение, нет — "False"), result[3] — "True"/"False"
		/// (businessSnapshotReviewedStatus == "NOT_REVIEWED" — подтверждено на реальных
		/// данных как признак наличия кнопки "Увеличить бюджет").
		/// </summary>
		Task<Dictionary<int, string>> CalculateRenewalAmountAsync(string companyPermalink, string campaignId, string cookieHeader, int durationDays);

		/// <summary>
		/// isMulti (из get-campaign-v3) и HasBudgetIncreaseButton
		/// (businessSnapshotReviewedStatus == "NOT_REVIEWED") — без полного расчёта
		/// продления, для карточек, которые не проходят через CalculateRenewalAmountAsync
		/// (апсейлы-числа, а не "Продление N дней"), но эти два флага им всё равно нужны.
		/// Возвращает (false, false), если запрос не удался.
		/// </summary>
		Task<(bool IsMulti, bool HasBudgetIncreaseButton)> GetCampaignFlagsAsync(string campaignId, string cookieHeader);
	}
}
