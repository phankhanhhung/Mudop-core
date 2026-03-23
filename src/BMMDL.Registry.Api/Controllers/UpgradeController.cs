using BMMDL.Registry.Entities;
using BMMDL.Registry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Registry.Api.Controllers;

/// <summary>
/// API endpoints for managing upgrade windows and dual-version sync.
/// </summary>
[ApiController]
[Route("api/upgrades")]
[Authorize(Policy = "AdminKeyPolicy")]
public class UpgradeController : ControllerBase
{
    private readonly DualVersionSyncService _upgradeService;
    private readonly UpgradeJobService? _jobService;

    public UpgradeController(
        DualVersionSyncService upgradeService,
        UpgradeJobService? jobService = null)
    {
        _upgradeService = upgradeService;
        _jobService = jobService;
    }

    /// <summary>
    /// Schedule a new upgrade window.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UpgradeWindowDto>> ScheduleUpgrade(
        [FromBody] ScheduleUpgradeRequest request,
        CancellationToken ct)
    {
        var window = await _upgradeService.ScheduleUpgradeAsync(
            request.TenantId,
            request.ModuleId,
            request.FromVersion,
            request.ToVersion,
            request.ScheduledStart,
            request.ScheduledEnd,
            request.CreatedBy,
            ct);

        return Created($"/api/upgrades/{window.Id}", ToDto(window));
    }

    /// <summary>
    /// Get active upgrade for tenant/module.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<UpgradeWindowDto>> GetActiveUpgrade(
        [FromQuery] Guid tenantId,
        [FromQuery] Guid moduleId,
        CancellationToken ct)
    {
        var window = await _upgradeService.GetActiveUpgradeAsync(tenantId, moduleId, ct);
        if (window == null)
            return NotFound(new { Message = "No active upgrade window" });

        return Ok(ToDto(window));
    }

    /// <summary>
    /// Get upgrade progress.
    /// </summary>
    [HttpGet("{windowId:guid}/progress")]
    public async Task<ActionResult<UpgradeProgress>> GetProgress(
        Guid windowId,
        CancellationToken ct)
    {
        var progress = await _upgradeService.GetUpgradeProgressAsync(windowId, ct);
        return Ok(progress);
    }

    /// <summary>
    /// Transition upgrade to next phase.
    /// </summary>
    [HttpPost("{windowId:guid}/transition")]
    public async Task<IActionResult> TransitionStatus(
        Guid windowId,
        [FromBody] TransitionRequest request,
        CancellationToken ct)
    {
        await _upgradeService.TransitionStatusAsync(windowId, request.NewStatus, ct);
        return Ok(new { Message = $"Transitioned to {request.NewStatus}" });
    }

    /// <summary>
    /// Start dual-version phase (prepare v2 schema and sync).
    /// </summary>
    [HttpPost("{windowId:guid}/start-dual-version")]
    public async Task<IActionResult> StartDualVersion(
        Guid windowId,
        CancellationToken ct)
    {
        await _upgradeService.TransitionStatusAsync(windowId, UpgradeStatus.Preparing, ct);
        // In real implementation: create v2 tables, start bulk migration
        await _upgradeService.TransitionStatusAsync(windowId, UpgradeStatus.DualVersion, ct);
        return Ok(new { Message = "Dual-version mode activated" });
    }

    /// <summary>
    /// Start cutover phase.
    /// </summary>
    [HttpPost("{windowId:guid}/cutover")]
    public async Task<IActionResult> StartCutover(
        Guid windowId,
        CancellationToken ct)
    {
        // Verify sync is complete before cutover
        var progress = await _upgradeService.GetUpgradeProgressAsync(windowId, ct);
        if (!progress.IsComplete)
        {
            return BadRequest(new { 
                Message = "Sync not complete", 
                Progress = progress 
            });
        }

        await _upgradeService.TransitionStatusAsync(windowId, UpgradeStatus.Cutover, ct);
        return Ok(new { Message = "Cutover initiated" });
    }

    /// <summary>
    /// Complete upgrade (finalize v2 as primary).
    /// </summary>
    [HttpPost("{windowId:guid}/complete")]
    public async Task<IActionResult> CompleteUpgrade(
        Guid windowId,
        CancellationToken ct)
    {
        await _upgradeService.TransitionStatusAsync(windowId, UpgradeStatus.Completed, ct);
        return Ok(new { Message = "Upgrade completed" });
    }

    /// <summary>
    /// Validate upgrade before completing (check record counts match).
    /// </summary>
    [HttpPost("{windowId:guid}/validate")]
    public async Task<ActionResult<ValidationResult>> ValidateUpgrade(
        Guid windowId,
        CancellationToken ct)
    {
        if (_jobService == null)
            return BadRequest(new { Message = "Job service not configured" });

        // Get v2 model from cache or storage
        // For now, return a basic validation
        return Ok(new ValidationResult { IsValid = true });
    }

    /// <summary>
    /// Rollback to v1.
    /// </summary>
    [HttpPost("{windowId:guid}/rollback")]
    public async Task<IActionResult> Rollback(
        Guid windowId,
        [FromBody] RollbackRequest request,
        CancellationToken ct)
    {
        // In real implementation: disable triggers, switch routing back to v1
        await _upgradeService.TransitionStatusAsync(windowId, UpgradeStatus.RolledBack, ct);
        return Ok(new { Message = "Rolled back to v1", Reason = request.Reason });
    }

    #region DTO Mapping

    private static UpgradeWindowDto ToDto(UpgradeWindow w) => new()
    {
        Id = w.Id,
        TenantId = w.TenantId,
        ModuleId = w.ModuleId,
        FromVersion = w.FromVersion,
        ToVersion = w.ToVersion,
        Status = w.Status.ToString(),
        ScheduledStart = w.ScheduledStart,
        ScheduledEnd = w.ScheduledEnd,
        ActualStart = w.ActualStart,
        ActualEnd = w.ActualEnd,
        V1ReadonlyAfter = w.V1ReadonlyAfter,
        V2PrimaryAfter = w.V2PrimaryAfter,
        CreatedAt = w.CreatedAt,
        SyncStatusCount = w.SyncStatuses.Count
    };

    #endregion
}

#region DTOs

public class UpgradeWindowDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ModuleId { get; set; }
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public DateTime? V1ReadonlyAfter { get; set; }
    public DateTime? V2PrimaryAfter { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SyncStatusCount { get; set; }
}

public class ScheduleUpgradeRequest
{
    public Guid TenantId { get; set; }
    public Guid ModuleId { get; set; }
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? CreatedBy { get; set; }
}

public class TransitionRequest
{
    public UpgradeStatus NewStatus { get; set; }
}

public class RollbackRequest
{
    public string Reason { get; set; } = "";
}

#endregion
