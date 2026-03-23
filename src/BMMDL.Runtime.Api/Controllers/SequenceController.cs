namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Controller for sequence operations.
/// Routes: /api/v1/{tenantId}/{module}/sequences/{sequenceName}
/// </summary>
[ApiController]
[Route("api/v1/{tenantId:guid}/{module}/sequences")]
[Authorize]
public class SequenceController : ControllerBase
{
    private readonly IMetaModelCache _cache;
    private readonly ISequenceService _sequenceService;
    private readonly ILogger<SequenceController> _logger;

    public SequenceController(
        IMetaModelCache cache,
        ISequenceService sequenceService,
        ILogger<SequenceController> logger)
    {
        _cache = cache;
        _sequenceService = sequenceService;
        _logger = logger;
    }

    /// <summary>
    /// List all available sequences.
    /// </summary>
    [HttpGet]
    public IActionResult ListSequences([FromRoute] string module)
    {
        var sequences = _cache.Sequences
            .Where(s => s.ForEntity?.StartsWith(module, StringComparison.OrdinalIgnoreCase) ?? true)
            .Select(s => new
            {
                name = s.Name,
                forEntity = s.ForEntity,
                forField = s.ForField,
                pattern = s.Pattern,
                scope = s.Scope.ToString(),
                resetOn = s.ResetOn.ToString(),
                startValue = s.StartValue,
                increment = s.Increment
            })
            .ToList();

        return Ok(new { sequences, count = sequences.Count });
    }

    /// <summary>
    /// Get sequence definition.
    /// </summary>
    [HttpGet("{sequenceName}")]
    public IActionResult GetSequence([FromRoute] string sequenceName)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceNotFound, $"Sequence '{sequenceName}' not found"));
        }

        return Ok(new
        {
            name = sequence.Name,
            forEntity = sequence.ForEntity,
            forField = sequence.ForField,
            pattern = sequence.Pattern,
            scope = sequence.Scope.ToString(),
            resetOn = sequence.ResetOn.ToString(),
            startValue = sequence.StartValue,
            increment = sequence.Increment,
            padding = sequence.Padding,
            maxValue = sequence.MaxValue
        });
    }

    /// <summary>
    /// Get the next value for a sequence.
    /// </summary>
    [HttpPost("{sequenceName}/next")]
    public async Task<IActionResult> GetNextValue(
        [FromRoute] Guid tenantId,
        [FromRoute] string sequenceName,
        [FromQuery] Guid? companyId,
        CancellationToken ct)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceNotFound, $"Sequence '{sequenceName}' not found"));
        }

        try
        {
            // Use company from query or extract from context
            var effectiveCompanyId = companyId ?? GetCompanyIdFromContext();

            var value = await _sequenceService.GetNextValueAsync(
                sequenceName, tenantId, effectiveCompanyId, ct);

            return Ok(new
            {
                sequence = sequenceName,
                value = value,
                tenantId = tenantId,
                companyId = effectiveCompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get next sequence value for {SequenceName}", sequenceName);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceError, "Failed to generate sequence value. Check server logs for details."));
        }
    }

    /// <summary>
    /// Get the current value for a sequence without incrementing.
    /// </summary>
    [HttpGet("{sequenceName}/current")]
    public async Task<IActionResult> GetCurrentValue(
        [FromRoute] Guid tenantId,
        [FromRoute] string sequenceName,
        [FromQuery] Guid? companyId,
        CancellationToken ct)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceNotFound, $"Sequence '{sequenceName}' not found"));
        }

        try
        {
            var effectiveCompanyId = companyId ?? GetCompanyIdFromContext();

            var value = await _sequenceService.GetCurrentValueAsync(
                sequenceName, tenantId, effectiveCompanyId, ct);

            return Ok(new
            {
                sequence = sequenceName,
                currentValue = value,
                tenantId = tenantId,
                companyId = effectiveCompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current sequence value for {SequenceName}", sequenceName);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceError, "Failed to get sequence value. Check server logs for details."));
        }
    }

    /// <summary>
    /// Reset a sequence to its start value.
    /// Requires Admin role.
    /// </summary>
    [HttpPost("{sequenceName}/reset")]
    public async Task<IActionResult> ResetSequence(
        [FromRoute] Guid tenantId,
        [FromRoute] string sequenceName,
        [FromQuery] Guid? companyId,
        CancellationToken ct)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceNotFound, $"Sequence '{sequenceName}' not found"));
        }

        // Check admin role
        var isAdmin = User.IsInRole("Admin") ||
                      User.Claims.Any(c => c.Type == "role" && c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin)
        {
            return Forbid();
        }

        try
        {
            var effectiveCompanyId = companyId ?? GetCompanyIdFromContext();

            await _sequenceService.ResetSequenceAsync(
                sequenceName, tenantId, effectiveCompanyId, ct);

            return Ok(new
            {
                sequence = sequenceName,
                reset = true,
                newStartValue = sequence.StartValue,
                tenantId = tenantId,
                companyId = effectiveCompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset sequence {SequenceName}", sequenceName);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SequenceError, "Failed to reset sequence. Check server logs for details."));
        }
    }

    /// <summary>
    /// Extract company ID from HTTP context (if set by middleware).
    /// </summary>
    private Guid? GetCompanyIdFromContext()
    {
        if (HttpContext.Items.TryGetValue("CompanyId", out var value) && value is Guid companyId)
        {
            return companyId;
        }
        return null;
    }
}
