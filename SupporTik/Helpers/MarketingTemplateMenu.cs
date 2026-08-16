using SupporTik.Classes;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SupporTik.Helpers
{
	public static class MarketingTemplateMenu
	{
		public static void Open(
			FrameworkElement placementTarget,
			IEnumerable<MarketingTextTemplate> templates,
			Action<MarketingTextTemplate> onSelected)
		{
			var menu = new ContextMenu
			{
				PlacementTarget = placementTarget,
				Placement = PlacementMode.Bottom
			};

			foreach (MarketingTextTemplate template in templates)
			{
				var item = new MenuItem { Header = template.MenuLabel };
				item.Click += (sender, args) => onSelected(template);
				menu.Items.Add(item);
			}

			if (menu.Items.Count > 0)
			{
				menu.IsOpen = true;
			}
		}
	}
}
