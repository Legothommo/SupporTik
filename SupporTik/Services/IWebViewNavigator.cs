using System.Threading.Tasks;
using HtmlAgilityPack;

namespace SupporTik.Services
{
	/// <summary>
	/// Тонкая обёртка над WebView2 для операций парсинга (навигация + снимок HTML +
	/// клик по пейджеру). Логин-флоу в MarketingWindow работает с WebView2 напрямую —
	/// это View-специфичная проводка событий, сюда не выносится.
	/// </summary>
	public interface IWebViewNavigator
	{
		/// <summary>
		/// Переходит по адресу и возвращает распарсенный HTML уже загруженной страницы.
		/// WebView2 один на окно, поэтому все переходы идут строго по очереди.
		/// </summary>
		Task<HtmlDocument> NavigateAndGetDocumentAsync(string url);

		/// <summary>
		/// Снимает HTML текущей уже загруженной страницы без перехода куда-либо —
		/// нужно после клика по пейджеру, когда список меняется внутри той же SPA.
		/// </summary>
		Task<HtmlDocument> GetCurrentDocumentAsync();

		/// <summary>
		/// Кликает по кнопке следующей страницы. Если кнопки нет или она задизейблена —
		/// сразу возвращает false, не кликая.
		/// </summary>
		Task<bool> ClickNextPageAsync();
	}
}
