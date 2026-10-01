using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RagExample.Api.Services;

// Turns text into embedding vectors via the OpenAI-compatible /embeddings endpoint.
// Like OpenAiCompatibleChatService there is nothing Google-specific in here - the same
// class works against any provider exposing that shape, which is why both read their
// URL, model and key from the same Chat:* configuration.
public class OpenAiCompatibleEmbeddingService
{
    // The endpoint takes an array of inputs, so chunks are embedded in batches rather than
    // one request each. On a hosted API that is the difference between one round trip and
    // one per chunk, and it keeps a large upload from burning through a free tier's
    // requests-per-minute allowance. Bounded because a whole book in one request would
    // exceed the payload limit.
    private const int MaxInputsPerRequest = 100;

    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiCompatibleEmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Chat:EmbeddingModel"]
            ?? throw new InvalidOperationException("Chat:EmbeddingModel is not configured.");

        var apiKey = config["Chat:ApiKey"]
            ?? throw new InvalidOperationException(
                "Chat:ApiKey is not configured. Set it with 'dotnet user-secrets set \"Chat:ApiKey\" \"<key>\"' " +
                "so it stays out of source control.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        (await EmbedAsync([text], ct))[0];

    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);

        for (var offset = 0; offset < texts.Count; offset += MaxInputsPerRequest)
        {
            var batch = texts.Skip(offset).Take(MaxInputsPerRequest).ToList();
            vectors.AddRange(await EmbedOneBatchAsync(batch, ct));
        }

        return vectors;
    }

    private async Task<List<float[]>> EmbedOneBatchAsync(List<string> batch, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("embeddings", new { model = _model, input = batch }, ct);

        await ProviderException.ThrowIfFailedAsync(response, "Embedding", _model, ct);

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
        var data = result?.Data ?? throw new InvalidOperationException("The embeddings endpoint returned no data.");

        if (data.Count != batch.Count)
            throw new InvalidOperationException(
                $"Asked for {batch.Count} embeddings but got {data.Count} back.");

        // Results carry an explicit index; the endpoint is not required to return them in
        // request order, and a silent mis-ordering would attach every chunk's text to the
        // wrong vector - retrieval would still "work", just return nonsense.
        return data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
