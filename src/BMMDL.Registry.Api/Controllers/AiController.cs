using BMMDL.Registry.Api.Models;
using BMMDL.Registry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Registry.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize(Policy = "AdminKeyPolicy")]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;
    private readonly ILogger<AiController> _logger;

    public AiController(IAiService aiService, ILogger<AiController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType<AiStatusResponse>(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(new AiStatusResponse
        {
            Configured = _aiService.IsConfigured,
            Model = _aiService.ModelName,
        });
    }

    [HttpPost("assist")]
    [ProducesResponseType<AiAssistResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Assist([FromBody] AiAssistRequest request, CancellationToken ct)
    {
        if (!_aiService.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "AI not configured. Set AI:AnthropicApiKey in appsettings or environment." });
        }

        if (string.IsNullOrWhiteSpace(request.Context))
            return BadRequest(new { error = "context is required" });

        if (!new[] { "complete", "generate", "review", "explain-error" }.Contains(request.Operation))
            return BadRequest(new { error = $"Unknown operation: {request.Operation}" });

        try
        {
            _logger.LogDebug("AI assist request: operation={Op}, contextLen={Len}",
                request.Operation, request.Context.Length);
            var result = await _aiService.AssistAsync(request, ct);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assist failed for operation {Op}", request.Operation);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = $"AI request failed: {ex.Message}" });
        }
    }

    [HttpPost("nl-query")]
    [ProducesResponseType<NlQueryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> NlQuery([FromBody] NlQueryRequest request, CancellationToken ct)
    {
        if (!_aiService.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "AI not configured. Set AI:AnthropicApiKey in appsettings or environment." });
        }

        if (string.IsNullOrWhiteSpace(request.EntityType))
            return BadRequest(new { error = "entityType is required" });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "prompt is required" });

        try
        {
            _logger.LogDebug("NL query request: entity={Entity}, promptLen={Len}",
                request.EntityType, request.Prompt.Length);
            var result = await _aiService.NlQueryAsync(request, ct);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NL query failed for entity {Entity}", request.EntityType);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = $"AI request failed: {ex.Message}" });
        }
    }
}
