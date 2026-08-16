using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SupporTik.Services
{
	public sealed class MarketingTextBuildResult
	{
		public string Text { get; set; }
		public string Error { get; set; }
		public bool Success => string.IsNullOrEmpty(Error);
	}

	public class MarketingOfferTextBuilder
	{
		private readonly MarketingTemplateService _templates;

		public MarketingOfferTextBuilder(MarketingTemplateService templates)
		{
			_templates = templates;
		}

		public string BuildSingle(MarketingItem item)
		{
			MarketingTextTemplate template = GetTemplatesForSingle(item).FirstOrDefault();
			return BuildSingle(item, template);
		}

		public string BuildSingle(MarketingItem item, MarketingTextTemplate template)
		{
			bool renewal = !string.IsNullOrEmpty(item.UpsaleValue) && item.UpsaleValue.Contains("Продление");
			bool budget = int.TryParse(item.UpsaleValue, out _);
			if ((!renewal && !budget) || template == null)
			{
				return string.Empty;
			}

			int days = renewal ? ParseDays(item.UpsaleValue) : 0;
			string text = Render(template.Content, new Dictionary<string, string>
			{
				["campaign"] = item.Permalink ?? string.Empty,
				["days"] = days.ToString(),
				["amount"] = FormatNumber(item.AmountUpsale),
				["prediction"] = FormatNumber(item.Prediction),
				["url"] = BuildUpsaleUrl(item)
			});

			// Это служебное условное дополнение зависит от срока и не является переменной
			// шаблона. Поэтому {long_term_note} больше не показывается пользователю.
			if (renewal && item.Role == MarketingTemplateTags.Owner)
			{
				string note = BuildLongTermNote(days);
				if (!string.IsNullOrEmpty(note)) text += "\r\n\r\n" + note;
			}

			return text;
		}

		public IReadOnlyList<MarketingTextTemplate> GetTemplatesForSingle(MarketingItem item)
		{
			string offerType = GetOfferType(item);
			if (offerType == null) return new List<MarketingTextTemplate>();
			return _templates.GetMatching(
				MarketingTemplateTags.SingleCampaign,
				GetRoleTag(item.Role),
				offerType);
		}

		public MarketingTextBuildResult BuildMultiple(
			IReadOnlyList<MarketingItem> items,
			MarketingTextTemplate selectedTemplate = null)
		{
			IReadOnlyList<MarketingTextTemplate> matches = GetTemplatesForMultiple(items, out string error);
			if (error != null)
			{
				return Error(error);
			}

			MarketingTextTemplate template = selectedTemplate ?? matches.FirstOrDefault();
			if (template == null)
			{
				return Error("Для выбранных тегов нет шаблонов.");
			}

			string campaigns = string.Join("\r\n", items.Select(item => $"- № {item.Permalink}")) + ".";
			string renewalDetails = string.Join("\r\n", items.Select(item =>
				$"- № {item.Permalink} на {ParseDays(item.UpsaleValue)} дней - {FormatNumber(item.AmountUpsale)} ₽")) + ".";
			string urls = string.Join("\r\n", items.Select(item =>
				$"- [№ {item.Permalink}]({BuildUpsaleUrl(item)})")) + ".";

			return new MarketingTextBuildResult
			{
				Text = Render(template.Content, new Dictionary<string, string>
				{
					["campaigns"] = campaigns,
					["renewal_details"] = renewalDetails,
					["urls"] = urls
				})
			};
		}

		public IReadOnlyList<MarketingTextTemplate> GetTemplatesForMultiple(
			IReadOnlyList<MarketingItem> items,
			out string error)
		{
			error = null;
			if (items == null || items.Count < 2)
			{
				error = "Отметьте хотя бы две карточки.";
				return new List<MarketingTextTemplate>();
			}
			if (items.Any(item => !CanBuildOffer(item)))
			{
				error = "Выберите кампании с доступными предложениями.";
				return new List<MarketingTextTemplate>();
			}
			string role = items[0].Role;
			if (items.Any(item => item.Role != role))
			{
				error = "Нельзя выбирать кампании с разными ролями.";
				return new List<MarketingTextTemplate>();
			}
			bool allBudget = items.All(item => int.TryParse(item.UpsaleValue, out _));
			bool allRenewal = items.All(item => item.UpsaleValue.Contains("Продление"));
			if (!allBudget && !allRenewal)
			{
				error = "Нельзя объединять разные типы предложений.";
				return new List<MarketingTextTemplate>();
			}

			return _templates.GetMatching(
				MarketingTemplateTags.MultipleCampaigns,
				GetRoleTag(role),
				allBudget ? MarketingTemplateTags.BudgetIncrease : MarketingTemplateTags.Renewal);
		}

		public string RenderPreview(MarketingTextTemplate template)
		{
			return Render(template.Content, new Dictionary<string, string>
			{
				["campaign"] = "123456789",
				["campaigns"] = "- № 123456789\r\n- № 987654321.",
				["days"] = "90",
				["amount"] = "12 500",
				["prediction"] = "700",
				["url"] = "https://yandex.ru/business/…",
				["urls"] = "- № [123456789](https://yandex.ru/business/…)\r\n- [№ 987654321](https://yandex.ru/business/…)",
				["renewal_details"] = "- № 123456789 на 90 дней - 12 500 ₽\r\n- № 987654321 на 180 дней - 20 000 ₽."
			});
		}

		private static string Render(string content, IDictionary<string, string> values)
		{
			string result = content ?? string.Empty;
			foreach (KeyValuePair<string, string> value in values)
			{
				result = result.Replace("{" + value.Key + "}", value.Value ?? string.Empty);
			}
			return result.Trim();
		}

		private static bool CanBuildOffer(MarketingItem item)
		{
			if (string.IsNullOrEmpty(item.UpsaleValue)) return false;
			if (item.UpsaleValue == "Нет предложения" || item.UpsaleValue == "Проверь в ЛК" ||
				item.UpsaleValue == "Не продавать" || item.UpsaleValue == "Нет данных") return false;
			return item.UpsaleValue.Contains("Продление") ||
				(int.TryParse(item.UpsaleValue, out _) && item.HasBudgetIncreaseButton);
		}

		private static int ParseDays(string value)
		{
			string digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
			return int.TryParse(digits, out int days) ? days : 0;
		}

		private static string FormatNumber(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;

			const NumberStyles styles = NumberStyles.Number;
			if (!decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out decimal number) &&
				!decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("ru-RU"), out number))
			{
				return value;
			}

			// Обычный пробел удобнее в скопированном тексте, чем NBSP, который ru-RU
			// использует как разделитель групп по умолчанию.
			return number
				.ToString("#,0", CultureInfo.GetCultureInfo("ru-RU"))
				.Replace('\u00A0', ' ')
				.Replace('\u202F', ' ');
		}

		private static string GetOfferType(MarketingItem item)
		{
			if (!string.IsNullOrEmpty(item.UpsaleValue) && item.UpsaleValue.Contains("Продление"))
				return MarketingTemplateTags.Renewal;
			return int.TryParse(item.UpsaleValue, out _)
				? MarketingTemplateTags.BudgetIncrease
				: null;
		}

		private static string GetRoleTag(string role)
		{
			return role == MarketingTemplateTags.Owner
				? MarketingTemplateTags.Owner
				: MarketingTemplateTags.Observer;
		}

		private static string BuildUpsaleUrl(MarketingItem item)
		{
			return item.IsMulti
				? $"https://yandex.ru/business/subscription/campaign/{item.Permalink}?upsale_budget={item.UpsaleValue}&show_popup=upsale"
				: $"https://yandex.ru/business/priority/campaign/{item.Permalink}/main?show_popup=upsale&upsale_budget={item.UpsaleValue}";
		}

		private static string BuildLongTermNote(int days)
		{
			if (days == 90)
				return "Если планируете продвижение надолго, сроки на 180 или 360 дней принесут выгоду — экономию до 25%. Отметим, что чем дольше ваш бизнес на виду, тем надёжнее поток клиентов";
			if (days == 180)
				return "Если планируете продвижение надолго, срок на 360 дней принесёт выгоду — экономию 25%. Отметим, что чем дольше ваш бизнес на виду, тем надёжнее поток клиентов";
			return string.Empty;
		}

		private static MarketingTextBuildResult Error(string message) =>
			new MarketingTextBuildResult { Error = message };
	}
}
