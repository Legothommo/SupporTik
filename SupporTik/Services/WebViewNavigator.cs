using System;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;

namespace SupporTik.Services
{
	public class WebViewNavigator : IWebViewNavigator
	{
		private readonly WebView2 _webView;

		public WebViewNavigator(WebView2 webView)
		{
			_webView = webView;
		}

		public async Task<HtmlDocument> NavigateAndGetDocumentAsync(string url)
		{
			var tcs = new TaskCompletionSource<bool>();

			void Handler(object s, CoreWebView2NavigationCompletedEventArgs args)
			{
				_webView.CoreWebView2.NavigationCompleted -= Handler;
				tcs.TrySetResult(args.IsSuccess);
			}

			_webView.CoreWebView2.NavigationCompleted += Handler;
			_webView.CoreWebView2.Navigate(url);

			bool success = await tcs.Task;
			if (!success)
			{
				throw new Exception("Не удалось загрузить страницу.");
			}

			// На SPA данные могут дорисовываться уже после NavigationCompleted
			await Task.Delay(1000);

			return await GetCurrentDocumentAsync();
		}

		public async Task<HtmlDocument> GetCurrentDocumentAsync()
		{
			string html = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
			html = JsonConvert.DeserializeObject<string>(html);

			var doc = new HtmlDocument();
			doc.LoadHtml(html);
			return doc;
		}

		public async Task<bool> ClickNextPageAsync()
		{
			// Возвращаем настоящий JS boolean, а не строку 'true'/'false' — ExecuteScriptAsync
			// сериализует результат в JSON, и для строки это была бы "true" (с кавычками
			// внутри самого C#-результата), из-за чего result == "true" никогда не совпадёт
			const string script = @"
					(function() {
						var item = document.querySelector('button[name=""page-next""]');
						if (!item) return false;

						var isDisabled = item.disabled
							|| item.hasAttribute('disabled')
							|| item.getAttribute('aria-disabled') === 'true';
						if (isDisabled) return false;

						['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function(eventType) {
							var event = new MouseEvent(eventType, { bubbles: true, cancelable: true, view: window });
							item.dispatchEvent(event);
						});

						return true;
					})()
				";

			string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
			return result == "true";
		}
	}
}
