using System.Net;

namespace RagExample.Api.Services;

// A failed call to the chat/embedding provider, carrying what the error handler and the retry
// handler need to react correctly: the HTTP status (inherited) and whether the failure was a
// *daily* quota. That distinction matters because the two 429s want opposite treatment - a
// per-minute limit clears in seconds and is worth retrying, while a per-day limit stays
// exhausted until tomorrow, so retrying only makes the user wait for the same error.
public class ProviderException(string message, HttpStatusCode status, bool dailyQuotaExhausted)
    : HttpRequestException(message, inner: null, status)
{
    public bool DailyQuotaExhausted { get; } = dailyQuotaExhausted;

    // Providers name the quota that tripped in the error body; Gemini's is e.g.
    // "GenerateRequestsPerDayPerProjectPerModel-FreeTier".
    public static bool IsDailyQuota(string body) =>
        body.Contains("PerDay", StringComparison.OrdinalIgnoreCase);

    // The provider explains a rejected key, an unknown model or an exhausted quota in the
    // response body, and EnsureSuccessStatusCode throws that detail away - so both services
    // use this instead.
    public static async Task ThrowIfFailedAsync(
        HttpResponseMessage response, string operation, string model, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new ProviderException(
            $"{operation} request to '{model}' failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
            response.StatusCode,
            response.StatusCode == HttpStatusCode.TooManyRequests && IsDailyQuota(body));
    }
}
