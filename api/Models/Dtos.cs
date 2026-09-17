namespace RagExample.Api.Models;

public record DocumentSummary(string Id, string Name, int ChunkCount, DateTime UploadedAt);

public record ChatRequest(string Question);

public record SourceChunk(string DocumentName, int ChunkIndex, string Text, double Score);

public record ChatResponse(string Answer, List<SourceChunk> Sources);
