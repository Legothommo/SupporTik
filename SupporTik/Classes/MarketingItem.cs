using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SupporTik.Classes
{
	public class MarketingItem
	{
		public string Permalink { get; set; }
		public string Status { get; set;  }
		public string Remain { get; set; }
		public string Href { get; set; }
		public string Role { get; set; }

		/// <summary>
		/// Пермалинк компании из companyDescription (не путать с Permalink выше — это ID
		/// самой кампании). Нужен для запроса billing/calculate-budget.
		/// </summary>
		public string CompanyPermalink { get; set; }

		/// <summary>Сырой результат проверки апсейла — null, пока не проверялся.</summary>
		public string UpsaleValue { get; set; }

		/// <summary>
		/// Точная сумма продления из billing/calculate-web-renewal-budget — null, пока не
		/// посчитана. Отдельно от UpsaleValue: не отображается в карточке (см.
		/// MarketingItemViewModel.SetAmountUpsale).
		/// </summary>
		public string AmountUpsale { get; set; }
		public string Prediction { get; set; }

		/// <summary>
		/// Поле isMulti из ответа get-campaign-v3 (см. BudgetService.CalculateRenewalAmountAsync) —
		/// true, если поле в ответе есть, иначе false. Не запрос (там isMulti в payload
		/// захардкожен отдельно), а именно то, что вернул сервер по конкретной кампании.
		/// </summary>
		public bool IsMulti { get; set; }

		/// <summary>
		/// businessSnapshotReviewedStatus == "NOT_REVIEWED" из ответа get-campaign-v3 (см.
		/// BudgetService.FetchCampaignInfoAsync) — подтверждено на реальных данных как
		/// признак наличия кнопки "Увеличить бюджет" на живой странице. Влияет на то, что
		/// показывать в поле апсейла для карточек с числовым UpsaleValue (см.
		/// MarketingItemViewModel.UpsaleDisplayValue).
		/// </summary>
		public bool HasBudgetIncreaseButton { get; set; }

		/// <summary>Значение для фильтра по апсейлу — три состояния вместо да/нет.</summary>
		public string UpsaleCategory
		{
			get
			{
				if (string.IsNullOrEmpty(UpsaleValue)) return "Не проверено";
				if (UpsaleValue == "Нет предложения") return "Нет предложения";
				if (UpsaleValue == "Не продавать") return "Не продавать";
				if (UpsaleValue == "Нет данных") return "Нет данных";
				if (UpsaleValue.Contains("Продление")) return "Продления";
				if (int.TryParse(UpsaleValue, out int sum)) return HasBudgetIncreaseButton ? "Апсейлы" : "Увел./умен. бюджет РК";
				if (UpsaleValue == "Проверь в ЛК") return "Проверь в ЛК";
				return "";
			}
		}
	}
}
