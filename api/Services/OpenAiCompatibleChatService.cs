using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RagExample.Api.Services;

// Talks to any hosted chat API that speaks the OpenAI /chat/completions format. Gemini,
// Groq, Cerebras, OpenRouter and Together all expose one, so moving between them is a
// base URL, a model name and a key in configuration rather than a new class.
//
// Two details of that format are easy to get wrong and are handled below:
//   - function arguments arrive as a JSON *string*, not a JSON object, so they need
//     parsing before anything can read them
//   - Gemini attaches an extra_content.google "thought signature" to each tool call and
//     rejects the next request with a 400 unless it is echoed back unchanged, so tool
//     calls carry it through the loop in ToolCall.ProviderExtra. Providers that don't
//     use it simply never set it.
public class OpenAiCompatibleChatService : IChatService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiCompatibleChatService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Chat:Model"]
            ?? throw new InvalidOperationException("Chat:Model is not configured.");

        var apiKey = config["Chat:ApiKey"]
            ?? throw new InvalidOperationException(
                "Chat:ApiKey is not configured. Set it with 'dotnet user-secrets set \"Chat:ApiKey\" \"<key>\"' " +
                "so it stays out of source control.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<ChatMessage> ChatAsync(List<ChatMessage> messages, List<ToolDefinition> tools, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            messages = messages.Select(ToWireMessage),
            tools = tools.Select(ToWireTool),
        };

        var response = await _http.PostAsJsonAsync("chat/completions", request, ct);
        await ProviderException.ThrowIfFailedAsync(response, "Chat", _model, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var messageEl = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        // content is explicitly null (not absent) on a turn that only requests tool calls.
        var content = messageEl.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? ""
            : "";

        List<ToolCall>? toolCalls = null;
        if (messageEl.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
        {
            toolCalls = tc.EnumerateArray().Select(t =>
            {
                var fn = t.GetProperty("function");
                var id = t.TryGetProperty("id", out var idEl) && idEl.GetString() is { } s
                    ? s
                    : Guid.NewGuid().ToString("N");

                var rawArguments = fn.TryGetProperty("arguments", out var a) ? a.GetString() : null;
                using var arguments = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(rawArguments) ? "{}" : rawArguments);

                var providerExtra = t.TryGetProperty("extra_content", out var extra) ? extra.Clone() : (JsonElement?)null;

                return new ToolCall(id, fn.GetProperty("name").GetString()!, arguments.RootElement.Clone(), providerExtra);
            }).ToList();
        }

        return new ChatMessage { Role = "assistant", Content = content, ToolCalls = toolCalls };
    }

    private static object ToWireMessage(ChatMessage m) => m.Role switch
    {
        "tool" => new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content },
        "assistant" when m.ToolCalls is { Count: > 0 } => new
        {
            role = "assistant",
            content = m.Content,
            tool_calls = m.ToolCalls.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.Arguments.GetRawText() },
                extra_content = tc.ProviderExtra is { } extra ? (object)extra : null,
            }),
        },
        _ => new { role = m.Role, content = m.Content },
    };

    private static object ToWireTool(ToolDefinition t) => new
    {
        type = "function",
        function = new { name = t.Name, description = t.Description, parameters = t.Parameters },
    };
}
