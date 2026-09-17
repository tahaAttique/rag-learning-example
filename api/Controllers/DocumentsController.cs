using Microsoft.AspNetCore.Mvc;
using RagExample.Api.Services;

namespace RagExample.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(IngestionService ingestion, VectorStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var docs = await store.ListDocumentsAsync();
        return Ok(docs);
    }

    [HttpPost]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Empty file.");

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        await using var stream = file.OpenReadStream();
        var summary = await ingestion.IngestPdfAsync(file.FileName, stream, ct);
        return Ok(summary);
    }
}
