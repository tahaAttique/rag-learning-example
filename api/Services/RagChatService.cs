using RagExample.Api.Models;

namespace RagExample.Api.Services;

// The "generation" half of RAG: embed the question, retrieve the closest chunks,
// stuff them into the system prompt, and ask the local model to answer from that context only.
public class RagChatService(OllamaEmbeddingService embeddings, VectorStore store, OllamaChatService chat)
{
    public async Task<ChatResponse> AskAsync(string question, CancellationToken ct = default)
    {
        var queryEmbedding = await embeddings.EmbedAsync(question, ct);
        var sources = await store.SearchAsync(queryEmbedding, topK: 4);

        var context = string.Join("\n\n---\n\n",
            sources.Select(s => $"[Source: {s.DocumentName}, chunk {s.ChunkIndex}]\n{s.Text}"));

        var systemPrompt = $"""
            You are a helpful assistant answering questions using ONLY the context below,
            which was retrieved from the user's uploaded documents.
            If the answer isn't contained in the context, say you don't know - do not make things up.

            Context:
            {context}
            """;

        var answer = await chat.CompleteAsync(systemPrompt, question, ct);

        return new ChatResponse(answer, sources);
    }
}
