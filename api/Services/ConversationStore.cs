using System.Collections.Concurrent;

namespace RagExample.Api.Services;

// Per-conversation dialogue history, held in memory, so follow-up questions
// ("what about the second one?") have something to refer back to.
//
// Only the user/assistant turns are kept. The tool_call and tool_result messages from
// each turn are deliberately dropped once that turn finishes: a single search result is
// several hundred words, so keeping them would fill the context window within a few
// questions while rarely being useful later. Compacting history down to the dialogue is
// what production agents do too.
//
// In memory means history dies with the process and isn't shared across instances. A real
// deployment would put this in Redis or a table keyed by conversation id.
public class ConversationStore
{
    private const int MaxMessages = 20;

    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public List<ChatMessage> GetHistory(string conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var messages)) return [];
        lock (messages) return [.. messages];
    }

    public void Append(string conversationId, ChatMessage question, ChatMessage answer)
    {
        var history = _conversations.GetOrAdd(conversationId, _ => []);

        lock (history)
        {
            history.Add(question);
            history.Add(answer);

            if (history.Count > MaxMessages)
                history.RemoveRange(0, history.Count - MaxMessages);
        }
    }
}
