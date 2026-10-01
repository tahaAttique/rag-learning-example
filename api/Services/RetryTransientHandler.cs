using System.Net;

namespace RagExample.Api.Services;

// Retries requests the provider rejected for reasons that usually pass on their own:
// 429 (rate limit) and 502/503/504 (overloaded or briefly down). Hosted models spike -
// "This model is currently experiencing high demand" is Gemini's 503 - and without this a
// single blip fails the user's whole question, or rolls back a whole document upload.
//
// 500 is deliberately not retried: it more often means a request the provider can never
// handle than a passing fault, and repeating it just delays the error.
//
// Safe to resend because both callers send JSON bodies, which are re-serialised on every
// attempt; a streamed one-shot body would not survive a second send.
public class RetryTransientHandler(ILogger<RetryTransientHandler> logger) : DelegatingHandler
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await base.SendAsync(request, ct);

            if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                return response;

            if (await IsDailyQuotaAsync(response, ct))
                return response;

            var delay = DelayBeforeRetry(response, attempt);
            logger.LogWarning(
                "{Method} {Uri} returned {Status}; retrying in {Delay:0.0}s (attempt {Attempt} of {Max})",
                request.Method, request.RequestUri, (int)response.StatusCode, delay.TotalSeconds, attempt, MaxAttempts);

            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    // A per-day quota does not recover in seconds, so retrying it would only hold the user up
    // for the full backoff before returning the identical error. The body is buffered first so
    // the caller can still read it afterwards.
    private static async Task<bool> IsDailyQuotaAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return false;

        await response.Content.LoadIntoBufferAsync(ct);
        return ProviderException.IsDailyQuota(await response.Content.ReadAsStringAsync(ct));
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan DelayBeforeRetry(HttpResponseMessage response, int attempt)
    {
        // When the provider says how long to wait, believe it - guessing shorter just burns
        // another attempt against a limit that hasn't reset yet.
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
        if (requested is { } wait && wait > TimeSpan.Zero)
            return wait < MaxDelay ? wait : MaxDelay;

        // 1s, 2s, 4s, 8s, plus jitter so concurrent requests don't retry in lockstep.
        var backoff = BaseDelay * Math.Pow(2, attempt - 1);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var delay = backoff + jitter;
        return delay < MaxDelay ? delay : MaxDelay;
    }
}
