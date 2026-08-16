namespace SupporTik.Classes
{
	public static class MarketingTemplateTags
	{
		public const string SingleCampaign = "Одна кампания";
		public const string MultipleCampaigns = "Несколько кампаний";
		public const string Owner = "Владелец";
		public const string Observer = "Наблюдатель";
		public const string Renewal = "Продление";
		public const string BudgetIncrease = "Увеличение бюджета";

		public static readonly string[] CampaignScopes = { SingleCampaign, MultipleCampaigns };
		public static readonly string[] Roles = { Owner, Observer };
		public static readonly string[] OfferTypes = { Renewal, BudgetIncrease };
	}

	public static class MarketingTemplateCategories
	{
		public const string SingleOwnerRenewal = "Одна кампания · владелец · продление";
		public const string SingleObserverRenewal = "Одна кампания · наблюдатель · продление";
		public const string SingleOwnerUpsale = "Одна кампания · владелец · увеличение бюджета";
		public const string SingleObserverUpsale = "Одна кампания · наблюдатель · увеличение бюджета";
		public const string MultipleOwnerRenewal = "Несколько кампаний · владелец · продление";
		public const string MultipleObserverRenewal = "Несколько кампаний · наблюдатель · продление";
		public const string MultipleOwnerUpsale = "Несколько кампаний · владелец · увеличение бюджета";
		public const string MultipleObserverUpsale = "Несколько кампаний · наблюдатель · увеличение бюджета";
	}

	public class MarketingTextTemplate
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string CampaignScope { get; set; }
		public string AudienceRole { get; set; }
		public string OfferType { get; set; }

		// Оставлено для чтения существующего marketing-templates.json. После первой
		// загрузки старое составное значение раскладывается на три редактируемых тега.
		public string Category
		{
			get => $"{CampaignScope} · {AudienceRole?.ToLowerInvariant()} · {OfferType?.ToLowerInvariant()}";
			set
			{
				if (!string.IsNullOrEmpty(CampaignScope) || string.IsNullOrEmpty(value)) return;
				CampaignScope = value.StartsWith("Несколько")
					? MarketingTemplateTags.MultipleCampaigns
					: MarketingTemplateTags.SingleCampaign;
				AudienceRole = value.Contains("наблюдатель")
					? MarketingTemplateTags.Observer
					: MarketingTemplateTags.Owner;
				OfferType = value.Contains("увеличение бюджета")
					? MarketingTemplateTags.BudgetIncrease
					: MarketingTemplateTags.Renewal;
			}
		}

		public string Content { get; set; }
		public bool IsFavorite { get; set; }
		public int SortOrder { get; set; }
		public string MenuLabel => IsFavorite ? $"★  {Name}" : Name;
	}
}
