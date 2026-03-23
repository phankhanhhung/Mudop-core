using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;
using BMMDL.Registry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Registry.Api.Controllers;

/// <summary>
/// API endpoints for managing module and object versions.
/// </summary>
[ApiController]
[Route("api/versions")]
[Authorize(Policy = "AdminKeyPolicy")]
public class VersionController : ControllerBase
{
    private readonly ObjectVersionRepository _versionRepo;

    public VersionController(ObjectVersionRepository versionRepo)
    {
        _versionRepo = versionRepo;
    }

    /// <summary>
    /// Get version history for an object.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<ObjectVersionDto>>> GetVersionHistory(
        [FromQuery] Guid tenantId,
        [FromQuery] string objectName,
        CancellationToken ct)
    {
        var versions = await _versionRepo.GetVersionHistoryAsync(tenantId, objectName, ct);
        return Ok(versions.Select(v => ToDto(v)));
    }

    /// <summary>
    /// Get pending approvals for a module.
    /// </summary>
    [HttpGet("pending/{moduleId:guid}")]
    public async Task<ActionResult<IReadOnlyList<ObjectVersionDto>>> GetPendingApprovals(
        Guid moduleId,
        CancellationToken ct)
    {
        var versions = await _versionRepo.GetPendingApprovalsAsync(moduleId, ct);
        return Ok(versions.Select(v => ToDto(v)));
    }

    /// <summary>
    /// Get breaking changes for a version.
    /// </summary>
    [HttpGet("{versionId:guid}/breaking-changes")]
    public async Task<ActionResult<IReadOnlyList<BreakingChangeDto>>> GetBreakingChanges(
        Guid versionId,
        CancellationToken ct)
    {
        var changes = await _versionRepo.GetBreakingChangesAsync(versionId, ct);
        return Ok(changes.Select(c => ToDto(c)));
    }

    /// <summary>
    /// Approve a breaking change.
    /// </summary>
    [HttpPost("breaking-changes/{changeId:guid}/approve")]
    public async Task<IActionResult> ApproveBreakingChange(
        Guid changeId,
        [FromBody] ReviewRequest request,
        CancellationToken ct)
    {
        await _versionRepo.ReviewBreakingChangeAsync(
            changeId,
            BreakingChangeStatus.Approved,
            request.ReviewedBy,
            request.Notes,
            ct);
        return Ok(new { Message = "Breaking change approved" });
    }

    /// <summary>
    /// Reject a breaking change.
    /// </summary>
    [HttpPost("breaking-changes/{changeId:guid}/reject")]
    public async Task<IActionResult> RejectBreakingChange(
        Guid changeId,
        [FromBody] ReviewRequest request,
        CancellationToken ct)
    {
        await _versionRepo.ReviewBreakingChangeAsync(
            changeId,
            BreakingChangeStatus.Rejected,
            request.ReviewedBy,
            request.Notes,
            ct);
        return Ok(new { Message = "Breaking change rejected" });
    }

    /// <summary>
    /// Get versions with breaking changes for a module.
    /// </summary>
    [HttpGet("module/{moduleId:guid}/breaking")]
    public async Task<ActionResult<IReadOnlyList<ObjectVersionDto>>> GetVersionsWithBreakingChanges(
        Guid moduleId,
        CancellationToken ct)
    {
        var versions = await _versionRepo.GetVersionsWithBreakingChangesAsync(moduleId, ct);
        return Ok(versions.Select(v => ToDto(v)));
    }

    #region DTO Mapping

    private static ObjectVersionDto ToDto(ObjectVersion v) => new()
    {
        Id = v.Id,
        TenantId = v.TenantId,
        ModuleId = v.ModuleId,
        ObjectType = v.ObjectType,
        ObjectName = v.ObjectName,
        Version = v.Version,
        ChangeCategory = v.ChangeCategory,
        IsBreaking = v.IsBreaking,
        ChangeDescription = v.ChangeDescription,
        Status = v.Status.ToString(),
        CreatedBy = v.CreatedBy,
        CreatedAt = v.CreatedAt,
        ApprovedBy = v.ApprovedBy,
        ApprovedAt = v.ApprovedAt,
        AppliedAt = v.AppliedAt,
        BreakingChangesCount = v.BreakingChanges.Count
    };

    private static BreakingChangeDto ToDto(BreakingChange c) => new()
    {
        Id = c.Id,
        ObjectVersionId = c.ObjectVersionId,
        ChangeType = c.ChangeType,
        TargetName = c.TargetName,
        Description = c.Description,
        OldValue = c.OldValue,
        NewValue = c.NewValue,
        ImpactAnalysis = c.ImpactAnalysis,
        SuggestedAction = c.SuggestedAction,
        Status = c.Status.ToString(),
        ReviewedBy = c.ReviewedBy,
        ReviewedAt = c.ReviewedAt,
        ReviewerNotes = c.ReviewerNotes
    };

    #endregion
}

#region DTOs

public class ObjectVersionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ModuleId { get; set; }
    public string ObjectType { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string Version { get; set; } = "";
    public string? ChangeCategory { get; set; }
    public bool IsBreaking { get; set; }
    public string? ChangeDescription { get; set; }
    public string Status { get; set; } = "";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public int BreakingChangesCount { get; set; }
}

public class BreakingChangeDto
{
    public Guid Id { get; set; }
    public Guid ObjectVersionId { get; set; }
    public string ChangeType { get; set; } = "";
    public string? TargetName { get; set; }
    public string Description { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ImpactAnalysis { get; set; }
    public string? SuggestedAction { get; set; }
    public string Status { get; set; } = "";
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewerNotes { get; set; }
}

public class ReviewRequest
{
    public string ReviewedBy { get; set; } = "";
    public string? Notes { get; set; }
}

#endregion
