namespace SupporTik.Classes
{
	/// <summary>
	/// Данные для прямых API-запросов к yandex.ru/business — достаются из куки уже
	/// авторизованной страницы в WebView2 (см. MarketingWindow.GetYandexBusinessAuthAsync),
	/// тем же способом, что и CSRF-токен для проверки апсейлов в DataLens.
	/// </summary>
	public class YandexBusinessAuth
	{
		public string CookieHeader { get; }
		public string CsrfToken { get; }
		public string SessionId { get; }
		public string ManagerUid { get; }

		public YandexBusinessAuth(string cookieHeader, string csrfToken, string sessionId, string managerUid)
		{
			CookieHeader = cookieHeader;
			CsrfToken = csrfToken;
			SessionId = sessionId;
			ManagerUid = managerUid;
		}
	}
}
