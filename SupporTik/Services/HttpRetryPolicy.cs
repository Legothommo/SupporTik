using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SupporTik.Services
{
	public sealed class ServiceRequestException : Exception
	{
		public ServiceRequestException(string message)
			: base(message)
		{
		}
	}

	public static class HttpRetryPolicy
	{
		private const int MaxAttempts = 3;

		public static async Task<HttpResponseMessage> SendAsync(
			HttpClient client,
			Func<HttpRequestMessage> requestFactory,
			CancellationToken cancellationToken)
		{
			for (int attempt = 1; attempt <= MaxAttempts; attempt++)
			{
				try
				{
					using (HttpRequestMessage request = requestFactory())
					{
						HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

						if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
						{
							return response;
						}

						TimeSpan delay = GetDelay(response, attempt);
						response.Dispose();
						await Task.Delay(delay, cancellationToken);
					}
				}
				catch (HttpRequestException ex)
				{
					if (attempt < MaxAttempts)
					{
						await Task.Delay(GetDelay(null, attempt), cancellationToken);
						continue;
					}

					LoggingService.LogError("HttpRetryPolicy.SendAsync", ex);
					throw new ServiceRequestException("Сервис временно недоступен. Попробуйте ещё раз позже.");
				}
			}

			throw new ServiceRequestException("Сервис временно недоступен. Попробуйте ещё раз позже.");
		}

		public static void EnsureSuccess(
			HttpResponseMessage response,
			string publicMessage,
			string logContext)
		{
			if (response.IsSuccessStatusCode)
			{
				return;
			}

			LoggingService.LogError(
				logContext,
				new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode}"));

			throw new ServiceRequestException(publicMessage);
		}

		private static bool IsTransient(HttpStatusCode statusCode)
		{
			int code = (int)statusCode;
			return code == 429 || code == 502 || code == 503 || code == 504;
		}

		private static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
		{
			TimeSpan? retryAfter = response?.Headers?.RetryAfter?.Delta;
			return retryAfter ?? TimeSpan.FromMilliseconds(300 * attempt * attempt);
		}
	}
}
