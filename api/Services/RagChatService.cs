using RagExample.Api.Models;

namespace RagExample.Api.Services;

// The agent loop. Instead of always fetching context before asking the model anything
// (the earlier, "static RAG" version of this class), the model is given tools and decides
// for itself whether, when, and how many times to call them. The loop is:
//
//   ask the model -> did it request a tool call?
//     yes -> run the tool, feed the result back as a new message, ask again
//     no  -> its content is the final answer, stop
//
// A round cap guards against a model that never stops calling tools.
public class RagChatService(OllamaChatService chat, AgentTools tools, ConversationStore conversations)
{
    private const int MaxToolRounds = 5;

    private const string SystemPrompt = """
        You answer questions about the user's uploaded documents, and you can do arithmetic.

        You have no built-in knowledge of the documents' contents - use the search_documents
        tool to find relevant passages before answering anything document-specific. If it finds
        nothing relevant, say you don't know rather than guessing.

        Use the calculator tool for every calculation, even simple ones. Never do arithmetic in
        your head. Expressions must contain only literal numbers - if you need a value from a
        document, search for it first, then put the actual number in the expression.

        Earlier turns of the conversation are included, so resolve references like "it" or
        "the second one" against them.
        """;

    public async Task<ChatResponse> AskAsync(string question, string? conversationId, CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(conversationId) ? Guid.NewGuid().ToString("N") : conversationId;

        var messages = new List<ChatMessage> { new() { Role = "system", Content = SystemPrompt } };
        messages.AddRange(conversations.GetHistory(id));

        var userMessage = new ChatMessage { Role = "user", Content = question };
        messages.Add(userMessage);

        var allSources = new List<SourceChunk>();
        var toolTrace = new List<ToolCallTrace>();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var reply = await chat.ChatAsync(messages, AgentTools.Definitions, ct);

            if (reply.ToolCalls is not { Count: > 0 })
            {
                conversations.Append(id, userMessage, new ChatMessage { Role = "assistant", Content = reply.Content });
                return new ChatResponse(reply.Content, allSources, toolTrace, id);
            }

            messages.Add(reply);

            foreach (var call in reply.ToolCalls)
            {
                var (resultText, sources) = await tools.ExecuteAsync(call.Name, call.Arguments, ct);
                allSources.AddRange(sources);
                toolTrace.Add(new ToolCallTrace(call.Name, call.Arguments.GetRawText(), resultText));

                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolName = call.Name,
                    Content = resultText,
                });
            }
        }

        const string giveUp = "I wasn't able to finish answering after several tool calls - try rephrasing the question.";
        conversations.Append(id, userMessage, new ChatMessage { Role = "assistant", Content = giveUp });
        return new ChatResponse(giveUp, allSources, toolTrace, id);
    }
}
