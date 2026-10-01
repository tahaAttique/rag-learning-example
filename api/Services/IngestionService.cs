using RagExample.Api.Models;

namespace RagExample.Api.Services;

// Ties the ingestion pipeline together: PDF -> text -> chunks -> embeddings -> storage.
public class IngestionService(OpenAiCompatibleEmbeddingService embeddings, VectorStore store)
{
    public async Task<DocumentSummary> IngestPdfAsync(string fileName, Stream pdfStream, CancellationToken ct = default)
    {
        var text = PdfTextExtractor.ExtractText(pdfStream);
        var chunks = TextChunker.Chunk(text);

        var documentId = await store.AddDocumentAsync(fileName);

        try
        {
            var vectors = await embeddings.EmbedAsync(chunks, ct);

            for (var i = 0; i < chunks.Count; i++)
                await store.AddChunkAsync(documentId, i, chunks[i], vectors[i]);
        }
        catch
        {
            // Embedding and storage both happen after the document row exists, so a failure
            // or a cancelled upload partway through would leave the document listed with only
            // part of its text searchable - worse than not having it at all, because retrieval
            // would silently miss the rest. Roll the whole document back instead.
            await store.DeleteDocumentAsync(documentId);
            throw;
        }

        return new DocumentSummary(documentId, fileName, chunks.Count, DateTime.UtcNow);
    }
}
