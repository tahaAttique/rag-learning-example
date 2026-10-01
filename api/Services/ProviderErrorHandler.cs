using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RagExample.Api.Services;

// Turns a failed call to the chat/embedding provider into a short message the UI can show.
// Without this the exception page - stack trace, request headers and all - is returned as the
// response body, and the chat panel prints it verbatim. The full detail (including the
// provider's own error JSON) still goes to the log; only the sentence changes.
//
// Anything that is not a provider failure returns false and falls through to the default
// handling, so genuine bugs still surface as a plain 500 rather than being mislabelled.
public class ProviderErrorHandler(ILogger<ProviderErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not HttpRequestException http) return false;

        logger.LogError(http, "Call to the AI provider failed");

        var (status, message) = http.StatusCode switch
        {
            HttpStatusCode.TooManyRequests when http is ProviderException { DailyQuotaExhausted: true } =>
                (HttpStatusCode.ServiceUnavailable, "The AI provider's daily quota for this model is used up (free tiers are small). It resets daily - or set Chat:Model to a different model, or enable billing."),
            HttpStatusCode.TooManyRequests =>
                (HttpStatusCode.ServiceUnavailable, "The AI provider is rate-limiting requests. Wait a moment and try again."),
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                (HttpStatusCode.ServiceUnavailable, "The AI provider is overloaded right now, even after several retries. Try again in a minute."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                (HttpStatusCode.BadGateway, "The AI provider rejected the API key. Check Chat:ApiKey."),
            HttpStatusCode.NotFound =>
                (HttpStatusCode.BadGateway, "The AI provider doesn't recognise the configured model. Check Chat:Model and Chat:EmbeddingModel."),
            null =>
                (HttpStatusCode.BadGateway, "Couldn't reach the AI provider. Check your network connection."),
            _ =>
                (HttpStatusCode.BadGateway, $"The AI provider returned an error ({(int)http.StatusCode})."),
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = (int)status, Title = "AI provider error", Detail = message },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: ct);

        return true;
    }
}
