using System.Net.Http.Json;
using System.Text.Json;

namespace RagExample.Api.Services;

// A tool the model is allowed to call. Parameters is a JSON Schema object describing
// the function's arguments - this is what tells the model what it can pass and how.
public record ToolDefinition(string Name, string Description, object Parameters);

// A single function call the model asked for: a name plus raw JSON arguments
// (left as JsonElement - the caller knows the shape of each tool's own arguments).
public record ToolCall(string Id, string Name, JsonElement Arguments);

// One turn in the conversation. Role is "system" | "user" | "assistant" | "tool".
// An assistant turn may carry ToolCalls instead of (or alongside) Content.
// A tool turn carries the result of executing one of those calls, tagged with ToolName.
public class ChatMessage
{
    public required string Role { get; init; }
    public string Content { get; init; } = "";
    public List<ToolCall>? ToolCalls { get; init; }
    public string? ToolName { get; init; }
}

// Talks to Ollama's tool-calling chat API. This class only knows how to make one
// request/response turn - it doesn't decide *when* to call tools or loop; that
// policy lives in RagChatService. Keeping them separate mirrors how the actual
// LLM tool-use protocol works: the model only ever proposes a call, your code
// decides whether and how to run it.
public class OllamaChatService(HttpClient http, IConfiguration config)
{
    private readonly string _model = config["Ollama:ChatModel"] ?? "llama3.2";

    public async Task<ChatMessage> ChatAsync(List<ChatMessage> messages, List<ToolDefinition> tools, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            stream = false,
            messages = messages.Select(ToWireMessage),
            tools = tools.Select(ToWireTool),
        };

        var response = await http.PostAsJsonAsync("api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var messageEl = doc.RootElement.GetProperty("message");

        var content = messageEl.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

        List<ToolCall>? toolCalls = null;
        if (messageEl.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
        {
            toolCalls = tc.EnumerateArray().Select(t =>
            {
                var fn = t.GetProperty("function");
                var id = t.TryGetProperty("id", out var idEl) && idEl.GetString() is { } s
                    ? s
                    : Guid.NewGuid().ToString("N");
                return new ToolCall(id, fn.GetProperty("name").GetString()!, fn.GetProperty("arguments").Clone());
            }).ToList();
        }

        return new ChatMessage { Role = "assistant", Content = content, ToolCalls = toolCalls };
    }

    private static object ToWireMessage(ChatMessage m) => m.Role switch
    {
        "tool" => new { role = "tool", tool_name = m.ToolName, content = m.Content },
        "assistant" when m.ToolCalls is { Count: > 0 } => new
        {
            role = "assistant",
            content = m.Content,
            tool_calls = m.ToolCalls.Select(tc => new
            {
                id = tc.Id,
                function = new { name = tc.Name, arguments = tc.Arguments },
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
