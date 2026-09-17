using System.Net.Http.Json;

namespace RagExample.Api.Services;

// Talks to a locally running Ollama instance (https://ollama.com) to turn text
// into an embedding vector. Anthropic's API doesn't offer embeddings, so this
// is the piece that stands in for that step.
public class OllamaEmbeddingService(HttpClient http, IConfiguration config)
{
    private readonly string _model = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/embeddings", new { model = _model, prompt = text }, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: ct);
        return result?.Embedding ?? throw new InvalidOperationException("Ollama returned no embedding.");
    }

    private class OllamaEmbeddingResponse
    {
        public float[] Embedding { get; set; } = [];
    }
}
