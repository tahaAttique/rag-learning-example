using System.Text.Json;
using RagExample.Api.Models;

namespace RagExample.Api.Services;

// The tools available to the agent, and the dispatch that executes them.
//
// Two tools with deliberately different characters, which is the interesting part:
//   search_documents - non-deterministic, needs an embedding model and a database
//   calculator       - pure, instant, exact
//
// The model has to pick between them from their descriptions alone. Those description
// strings are prompt engineering: they are the only thing telling the model when each
// tool applies. Reword them and the model's choices change.
public class AgentTools(OllamaEmbeddingService embeddings, VectorStore store, IConfiguration config)
{
    private readonly int _topK = config.GetValue<int?>("Retrieval:TopK") ?? 4;


    public static readonly List<ToolDefinition> Definitions =
    [
        new ToolDefinition(
            "search_documents",
            "Search the user's uploaded documents for passages relevant to a query. " +
            "Returns the best-matching text chunks. Call this whenever answering the " +
            "question requires information from the documents.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "What to search for, as a natural-language question or topic.",
                    },
                },
                required = new[] { "query" },
            }),
        new ToolDefinition(
            "calculator",
            "Evaluate an arithmetic expression and return the exact result. Supports " +
            "+ - * / % ^ and parentheses. Use this for any calculation rather than doing " +
            "the arithmetic yourself, because you are unreliable at mental math.",
            new
            {
                type = "object",
                properties = new
                {
                    expression = new
                    {
                        type = "string",
                        description = "The expression to evaluate, e.g. \"(1200 * 3) / 12\".",
                    },
                },
                required = new[] { "expression" },
            }),
    ];

    public async Task<(string ResultText, List<SourceChunk> Sources)> ExecuteAsync(
        string name, JsonElement arguments, CancellationToken ct)
    {
        switch (name)
        {
            case "search_documents":
            {
                var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(query))
                    return ("The search query was empty.", []);

                var embedding = await embeddings.EmbedAsync(query, ct);
                var results = await store.SearchAsync(embedding, _topK);

                var text = results.Count == 0
                    ? "No documents have been uploaded yet."
                    : string.Join("\n\n---\n\n", results.Select(r =>
                        $"[{r.DocumentName}, chunk {r.ChunkIndex}, similarity {r.Score:F2}]\n{r.Text}"));

                return (text, results);
            }

            case "calculator":
            {
                var expression = arguments.TryGetProperty("expression", out var e) ? e.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(expression))
                    return ("The expression was empty.", []);

                try
                {
                    var value = Calculator.Evaluate(expression);
                    return ($"{expression} = {value}", []);
                }
                catch (FormatException ex)
                {
                    // Errors go back to the model as a normal result, not as an exception, so it
                    // can correct itself and call again. The wording matters: small models often
                    // try to pass placeholders like "2026 - x", so the message says what to do
                    // about it rather than just reporting a parse position.
                    return ($"Could not evaluate '{expression}': {ex.Message} " +
                            "Expressions must contain only literal numbers and operators - " +
                            "variables and placeholders are not supported. Look up the actual " +
                            "value first, then send the expression with that number substituted in.",
                        []);
                }
            }

            default:
                return ($"Unknown tool: {name}", []);
        }
    }
}
