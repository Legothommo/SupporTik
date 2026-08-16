using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SupporTik.Services
{
	public class MarketingTemplateService
	{
		private const string FileName = "marketing-templates.json";
		private readonly StorageService _storage;
		private readonly List<MarketingTextTemplate> _templates;

		public MarketingTemplateService(StorageService storage)
		{
			_storage = storage;
			_templates = _storage.LoadData<MarketingTextTemplate>(FileName);
			if (_templates.Count == 0)
			{
				_templates.AddRange(CreateDefaults());
				Save();
			}
			else if (RemoveLegacyLongTermPlaceholder())
			{
				Save();
			}
		}

		public IReadOnlyList<MarketingTextTemplate> GetAll() => _templates;

		public IReadOnlyList<MarketingTextTemplate> GetMatching(
			string campaignScope,
			string audienceRole,
			string offerType)
		{
			return _templates
				.Where(t => t.CampaignScope == campaignScope &&
					t.AudienceRole == audienceRole &&
					t.OfferType == offerType)
				.OrderByDescending(t => t.IsFavorite)
				.ThenBy(t => t.SortOrder)
				.ToList();
		}

		public MarketingTextTemplate GetPreferred(
			string campaignScope,
			string audienceRole,
			string offerType)
		{
			return GetMatching(campaignScope, audienceRole, offerType).FirstOrDefault();
		}

		public MarketingTextTemplate Add()
		{
			var template = new MarketingTextTemplate
			{
				Id = Guid.NewGuid().ToString("N"),
				Name = "Новый шаблон",
				CampaignScope = MarketingTemplateTags.SingleCampaign,
				AudienceRole = MarketingTemplateTags.Owner,
				OfferType = MarketingTemplateTags.Renewal,
				Content = string.Empty,
				SortOrder = _templates.Count
			};
			_templates.Add(template);
			Save();
			return template;
		}

		public MarketingTextTemplate Duplicate(MarketingTextTemplate source)
		{
			var copy = new MarketingTextTemplate
			{
				Id = Guid.NewGuid().ToString("N"),
				Name = source.Name + " — копия",
				CampaignScope = source.CampaignScope,
				AudienceRole = source.AudienceRole,
				OfferType = source.OfferType,
				Content = source.Content,
				SortOrder = _templates.Count
			};
			_templates.Add(copy);
			Save();
			return copy;
		}

		public void Delete(MarketingTextTemplate template)
		{
			if (_templates.Count(t => HasSameTags(t, template)) <= 1)
			{
				throw new InvalidOperationException("Нельзя удалить последний шаблон этой категории.");
			}

			_templates.Remove(template);
			NormalizeOrder();
			Save();
		}

		public void SetFavorite(MarketingTextTemplate template)
		{
			foreach (MarketingTextTemplate item in _templates.Where(t => HasSameTags(t, template)))
			{
				item.IsFavorite = ReferenceEquals(item, template);
			}
			Save();
		}

		public void SaveOrder(IEnumerable<MarketingTextTemplate> ordered)
		{
			int index = 0;
			foreach (MarketingTextTemplate template in ordered)
			{
				template.SortOrder = index++;
			}
			Save();
		}

		public void ReplaceAll(IEnumerable<MarketingTextTemplate> templates)
		{
			if (templates == null) throw new ArgumentNullException(nameof(templates));

			List<MarketingTextTemplate> imported = templates.ToList();
			ValidateImported(imported);

			_templates.Clear();
			_templates.AddRange(imported.OrderBy(t => t.SortOrder));
			NormalizeOrder();
			RemoveLegacyLongTermPlaceholder();
			Save();
		}

		public void Save() => _storage.SaveData(_templates, FileName);

		private void NormalizeOrder()
		{
			for (int i = 0; i < _templates.Count; i++)
			{
				_templates[i].SortOrder = i;
			}
		}

		private static IEnumerable<MarketingTextTemplate> CreateDefaults()
		{
			return new[]
			{
				Create("Продление для владельца", MarketingTemplateCategories.SingleOwnerRenewal,
					"Видим, что подписка № {campaign} скоро завершится. Предлагаем продлить её, чтобы не прерывать показы, сохранить результаты и привлечь новую аудиторию.\r\n\r\nПродление на {days} дней составит {amount} ₽ и принесёт до {prediction} потенциальных клиентов в месяц.", 0),
				Create("Продление для наблюдателя", MarketingTemplateCategories.SingleObserverRenewal,
					"Мы заметили, что кампания по продвижению № {campaign} скоро завершится. Продлите её, чтобы не прерывать показы.\r\n\r\nПодробности отправим на почту владельца кампании", 1),
				Create("Увеличение бюджета для владельца", MarketingTemplateCategories.SingleOwnerUpsale,
					"Вижу, что у вашей кампании хорошие показатели. Их можно улучшить с помощью увеличения бюджета.\r\n\r\nКак это работает: алгоритм показа объявлений выбирает площадки в пределах бюджета. Если его увеличить, алгоритм получит новые возможности, чтобы привлекать больше потенциальных клиентов.\r\n\r\nПодробности предложения: [{url}]({url})", 2),
				Create("Увеличение бюджета для наблюдателя", MarketingTemplateCategories.SingleObserverUpsale,
					"Вижу, что у вашей кампании хорошие показатели. Их можно улучшить с помощью увеличения бюджета.\r\n\r\nКак это работает: алгоритм показа объявлений выбирает площадки в пределах бюджета. Если его увеличить, алгоритм получит новые возможности, чтобы привлекать больше потенциальных клиентов.\r\n\r\nПодробности предложения: [{url}]({url})\r\n\r\nОтправим письмо с подробностями на почту владельца кампании. Предложение действует 7 дней", 3),
				Create("Несколько увеличений для владельца", MarketingTemplateCategories.MultipleOwnerUpsale,
					"Мы заметили, что вам доступны увеличения бюджета для ваших рекламных кампаний. Увеличьте месячный бюджет на продвижения, чтобы повысить их охват:\r\n\r\n{urls}\r\n\r\nАлгоритм подбирает площадки и публикует объявления в пределах бюджета, который вы выбрали. Если его увеличить, объявления будут публиковаться чаще и на новых площадках. Это расширит клиентскую базу — об организации узнает больше пользователей.", 4),
				Create("Несколько увеличений для наблюдателя", MarketingTemplateCategories.MultipleObserverUpsale,
					"Мы заметили, что вам доступны увеличения бюджета для ваших рекламных кампаний. Увеличьте месячный бюджет на продвижения, чтобы повысить их охват:\r\n\r\n{urls}\r\n\r\nАлгоритм подбирает площадки и публикует объявления в пределах бюджета, который вы выбрали. Если его увеличить, объявления будут публиковаться чаще и на новых площадках. Это расширит клиентскую базу — об организации узнает больше пользователей.\r\n\r\nПодробности отправим на почту владельца продвижения. Предложение действует 7 дней", 5),
				Create("Несколько продлений для владельца", MarketingTemplateCategories.MultipleOwnerRenewal,
					"Мы заметили, что скоро завершатся ваши кампании по продвижению:\r\n\r\n{campaigns}\r\n\r\nПродлите их, чтобы избежать перерыва в показах. Стоимость продления:\r\n\r\n{renewal_details}\r\n\r\nТарифы на 180 или 360 дней помогут сэкономить до 25% затрат. Отметим, что чем дольше клиенты видят вас, тем надёжнее будет поток заявок", 6),
				Create("Несколько продлений для наблюдателя", MarketingTemplateCategories.MultipleObserverRenewal,
					"Мы заметили, что скоро завершатся кампании по продвижению:\r\n\r\n{campaigns}\r\n\r\nПродлите их, чтобы избежать перерыва в показах.\r\n\r\nОтправим подробности владельцам кампаний на почту", 7)
			};
		}

		private static MarketingTextTemplate Create(string name, string category, string content, int order)
		{
			return new MarketingTextTemplate
			{
				Id = Guid.NewGuid().ToString("N"),
				Name = name,
				Category = category,
				Content = content,
				IsFavorite = true,
				SortOrder = order
			};
		}

		private static bool HasSameTags(MarketingTextTemplate left, MarketingTextTemplate right)
		{
			return left.CampaignScope == right.CampaignScope &&
				left.AudienceRole == right.AudienceRole &&
				left.OfferType == right.OfferType;
		}

		public static void ValidateImported(IReadOnlyCollection<MarketingTextTemplate> templates)
		{
			if (templates.Count == 0)
				throw new InvalidOperationException("Экспорт не содержит рекламных шаблонов.");

			if (templates.Any(t => t == null || string.IsNullOrWhiteSpace(t.Id) ||
				string.IsNullOrWhiteSpace(t.Name) || t.Content == null ||
				!MarketingTemplateTags.CampaignScopes.Contains(t.CampaignScope) ||
				!MarketingTemplateTags.Roles.Contains(t.AudienceRole) ||
				!MarketingTemplateTags.OfferTypes.Contains(t.OfferType)))
			{
				throw new InvalidOperationException("Экспорт содержит некорректные рекламные шаблоны.");
			}

			if (templates.GroupBy(t => t.Id).Any(group => group.Count() > 1))
				throw new InvalidOperationException("Экспорт содержит шаблоны с одинаковыми идентификаторами.");
		}

		private bool RemoveLegacyLongTermPlaceholder()
		{
			bool changed = false;
			foreach (MarketingTextTemplate template in _templates)
			{
				if (template.Content?.Contains("{long_term_note}") == true)
				{
					template.Content = template.Content.Replace("{long_term_note}", string.Empty).Trim();
					changed = true;
				}
			}
			return changed;
		}
	}
}
