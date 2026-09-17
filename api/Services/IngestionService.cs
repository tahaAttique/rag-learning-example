using RagExample.Api.Models;

namespace RagExample.Api.Services;

// Ties the ingestion pipeline together: PDF -> text -> chunks -> embeddings -> storage.
public class IngestionService(OllamaEmbeddingService embeddings, VectorStore store)
{
    public async Task<DocumentSummary> IngestPdfAsync(string fileName, Stream pdfStream, CancellationToken ct = default)
    {
        var text = PdfTextExtractor.ExtractText(pdfStream);
        var chunks = TextChunker.Chunk(text);

        var documentId = await store.AddDocumentAsync(fileName);

        for (var i = 0; i < chunks.Count; i++)
        {
            var embedding = await embeddings.EmbedAsync(chunks[i], ct);
            await store.AddChunkAsync(documentId, i, chunks[i], embedding);
        }

        return new DocumentSummary(documentId, fileName, chunks.Count, DateTime.UtcNow);
    }
}
