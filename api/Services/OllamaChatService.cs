using System.Net.Http.Json;

namespace RagExample.Api.Services;

// The "generation" half of RAG, running fully locally through Ollama - no API key,
// no cost. Swap this out for a hosted model (Claude, etc.) later if you want better
// answer quality once the local pipeline makes sense.
public class OllamaChatService(HttpClient http, IConfiguration config)
{
    private readonly string _model = config["Ollama:ChatModel"] ?? "llama3.2";

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new OllamaChatRequest
        {
            Model = _model,
            Stream = false,
            Messages =
            [
                new OllamaChatMessage { Role = "system", Content = systemPrompt },
                new OllamaChatMessage { Role = "user", Content = userMessage },
            ],
        };

        var response = await http.PostAsJsonAsync("api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
        return result?.Message?.Content ?? throw new InvalidOperationException("Ollama returned no response.");
    }

    private class OllamaChatRequest
    {
        public string Model { get; set; } = "";
        public bool Stream { get; set; }
        public List<OllamaChatMessage> Messages { get; set; } = [];
    }

    private class OllamaChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private class OllamaChatResponse
    {
        public OllamaChatMessage? Message { get; set; }
    }
}
