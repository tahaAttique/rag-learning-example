using Microsoft.AspNetCore.Mvc;
using RagExample.Api.Models;
using RagExample.Api.Services;

namespace RagExample.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(RagChatService chat) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Question is required.");

        var response = await chat.AskAsync(request.Question, ct);
        return Ok(response);
    }
}
