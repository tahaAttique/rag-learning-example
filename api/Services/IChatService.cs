using System.Text.Json;

namespace RagExample.Api.Services;

// A tool the model is allowed to call. Parameters is a JSON Schema object describing
// the function's arguments - this is what tells the model what it can pass and how.
public record ToolDefinition(string Name, string Description, object Parameters);

// A single function call the model asked for: a name plus raw JSON arguments
// (left as JsonElement - the caller knows the shape of each tool's own arguments).
//
// ProviderExtra is an opaque bag a provider can stash data in when it returns a tool
// call, to be replayed verbatim when that call is echoed back in a later turn - e.g.
// Gemini's OpenAI-compat endpoint requires its tool_calls[].extra_content.google
// "thought signature" back unchanged or multi-round tool calls fail with 400. Nothing
// but the provider that set it ever reads it; the agent loop just carries it along.
public record ToolCall(string Id, string Name, JsonElement Arguments, JsonElement? ProviderExtra = null);

// One turn in the conversation. Role is "system" | "user" | "assistant" | "tool".
// An assistant turn may carry ToolCalls instead of (or alongside) Content.
// A tool turn carries the result of executing one of those calls, tagged with the
// ToolCallId of the call it answers - that id is how the model pairs a result back to
// the request it made, which matters when it asks for several tools in one turn.
public class ChatMessage
{
    public required string Role { get; init; }
    public string Content { get; init; } = "";
    public List<ToolCall>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
}

// One request/response turn against a chat model that supports tool calling.
// Implementations only know how to make a single turn - deciding whether and when to
// run the tools the model asks for is RagChatService's job. That split is what lets
// the same agent loop run against a local model or a hosted one unchanged.
public interface IChatService
{
    Task<ChatMessage> ChatAsync(List<ChatMessage> messages, List<ToolDefinition> tools, CancellationToken ct = default);
}
