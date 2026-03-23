namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.Runtime.Events;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
[RequiresPlugin("AuditLogging")]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogStore _store;

    public AuditLogController(IAuditLogStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? entityName,
        [FromQuery] Guid? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? eventType,
        [FromQuery] int top = 50,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top < 0 || top > QueryConstants.MaxPageSize)
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                $"$top must be between 0 and {QueryConstants.MaxPageSize}"));
        if (skip < 0)
            return BadRequest(ODataErrorResponse.FromException("INVALID_SKIP",
                "$skip must be non-negative"));

        var tenantId = HttpContext.GetTenantId();

        var query = new AuditLogQuery
        {
            EntityName = entityName,
            EntityId = entityId,
            UserId = userId,
            TenantId = tenantId,
            From = from,
            To = to,
            EventType = eventType,
            Top = Math.Min(top, 200),
            Skip = skip
        };

        var entries = await _store.QueryAsync(query, ct);
        var count = await _store.CountAsync(query, ct);

        return Ok(new
        {
            value = entries,
            count
        });
    }

    [HttpGet("events/{correlationId}/chain")]
    public async Task<IActionResult> GetEventChain(string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "correlationId is required"));
        }

        var entries = await _store.QueryByCorrelationIdAsync(correlationId, ct);

        return Ok(new
        {
            correlationId,
            count = entries.Count,
            chain = entries
        });
    }
}
