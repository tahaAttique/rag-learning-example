namespace RagExample.Api.Models;

public record DocumentSummary(string Id, string Name, int ChunkCount, DateTime UploadedAt);

// ConversationId is null on the first question of a conversation; the server issues one
// and the client sends it back on subsequent questions to keep the thread.
public record ChatRequest(string Question, string? ConversationId = null);

public record SourceChunk(string DocumentName, int ChunkIndex, string Text, double Score);

// One tool invocation the model made while answering - shown in the UI so you can
// see the model's actual decisions, not just its final answer.
public record ToolCallTrace(string ToolName, string Arguments, string Result);

public record ChatResponse(
    string Answer,
    List<SourceChunk> Sources,
    List<ToolCallTrace> ToolCalls,
    string ConversationId);
