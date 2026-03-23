using System.Security.Claims;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Api.Hubs;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Collaboration;
using BMMDL.Runtime.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace BMMDL.Runtime.Api.Controllers;

// =============================================================================
// DTOs
// =============================================================================

public record CommentDto(
    Guid Id,
    string AuthorId,
    string AuthorName,
    string Content,
    List<string> Mentions,
    List<string> LikedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateCommentRequest(string Content, List<string>? Mentions);

public record ChangeRequestDto(
    Guid Id,
    string ProposedById,
    string ProposedByName,
    Dictionary<string, object?> ProposedChanges,
    string Status,
    string? ReviewerId,
    string? ReviewerName,
    string? ReviewComment,
    DateTime? ReviewedAt,
    DateTime CreatedAt);

public record CreateChangeRequestRequest(Dictionary<string, object?> ProposedChanges);

public record ReviewChangeRequestRequest(string Decision, string? Comment);  // Decision: "approve" | "reject"

// =============================================================================
// Controller
// =============================================================================

/// <summary>
/// REST controller for B6 Collaboration: per-record comments, change requests, and lock status.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[RequiresPlugin("Collaboration")]
public class CollaborationController : ControllerBase
{
    private readonly ICommentStore _commentStore;
    private readonly IChangeRequestStore _changeRequestStore;
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext;
    private readonly IEntityResolver _entityResolver;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<CollaborationController> _logger;

    public CollaborationController(
        ICommentStore commentStore,
        IChangeRequestStore changeRequestStore,
        IHubContext<NotificationHub, INotificationClient> hubContext,
        IEntityResolver entityResolver,
        IQueryExecutor queryExecutor,
        ILogger<CollaborationController> logger)
    {
        _commentStore = commentStore ?? throw new ArgumentNullException(nameof(commentStore));
        _changeRequestStore = changeRequestStore ?? throw new ArgumentNullException(nameof(changeRequestStore));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // COMMENTS
    // =========================================================================

    /// <summary>
    /// GET api/odata/{module}/{entityType}/{entityId}/comments
    /// Returns all comments for a specific entity record.
    /// </summary>
    [HttpGet("odata/{module}/{entityType}/{entityId}/comments")]
    public async Task<IActionResult> GetComments(
        string module, string entityType, string entityId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var recordKey = $"{module}/{entityType}/{entityId}";
        var comments = await _commentStore.GetByRecordAsync(recordKey, tenantId);
        var dtos = comments.Select(ToCommentDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// POST api/odata/{module}/{entityType}/{entityId}/comments
    /// Creates a new comment on an entity record and broadcasts via SignalR.
    /// </summary>
    [HttpPost("odata/{module}/{entityType}/{entityId}/comments")]
    public async Task<IActionResult> CreateComment(
        string module, string entityType, string entityId,
        [FromBody] CreateCommentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Content is required." });
        }

        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var authorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!Guid.TryParse(authorIdClaim, out _))
        {
            return BadRequest(new { error = "Invalid or missing user identifier claim." });
        }
        var authorId = authorIdClaim;
        var authorName = User.FindFirst(ClaimTypes.Name)?.Value ?? authorId;
        var recordKey = $"{module}/{entityType}/{entityId}";

        var comment = new Comment
        {
            RecordKey = recordKey,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            AuthorId = authorId,
            AuthorName = authorName,
            Content = request.Content,
            Mentions = request.Mentions ?? []
        };

        var created = await _commentStore.CreateAsync(comment, tenantId);

        await _hubContext.Clients.Group($"record:{recordKey}").NewComment(
            new CommentNotification(
                recordKey,
                created.Id.ToString(),
                created.AuthorId,
                created.AuthorName,
                created.Content,
                created.Mentions.ToArray(),
                created.CreatedAt));

        _logger.LogInformation("Comment {CommentId} created on record {RecordKey}", created.Id, recordKey);
        return CreatedAtAction(nameof(GetComments), new { module, entityType, entityId }, ToCommentDto(created));
    }

    /// <summary>
    /// DELETE api/odata/{module}/{entityType}/{entityId}/comments/{commentId}
    /// Deletes a comment. Only the author (or admin) may delete their own comment.
    /// </summary>
    [HttpDelete("odata/{module}/{entityType}/{entityId}/comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(
        string module, string entityType, string entityId,
        Guid commentId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var existing = await _commentStore.GetByIdAsync(commentId, tenantId);
        if (existing == null)
        {
            return NotFound(new { error = $"Comment {commentId} not found." });
        }

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin") || User.HasClaim("role", "Admin");

        if (!isAdmin && existing.AuthorId != currentUserId)
        {
            return Forbid();
        }

        await _commentStore.DeleteAsync(commentId, tenantId);
        _logger.LogInformation("Comment {CommentId} deleted by {UserId}", commentId, currentUserId);
        return NoContent();
    }

    /// <summary>
    /// POST api/odata/{module}/{entityType}/{entityId}/comments/{commentId}/like
    /// Toggles the current user's like on a comment.
    /// </summary>
    [HttpPost("odata/{module}/{entityType}/{entityId}/comments/{commentId:guid}/like")]
    public async Task<IActionResult> ToggleLike(
        string module, string entityType, string entityId,
        Guid commentId,
        CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var existing = await _commentStore.GetByIdAsync(commentId, tenantId);
        if (existing == null)
        {
            return NotFound(new { error = $"Comment {commentId} not found." });
        }

        var updated = await _commentStore.ToggleLikeAsync(commentId, userId, tenantId);
        return Ok(ToCommentDto(updated));
    }

    // =========================================================================
    // CHANGE REQUESTS
    // =========================================================================

    /// <summary>
    /// GET api/odata/{module}/{entityType}/{entityId}/change-requests
    /// Returns change requests for a record. Default: status=pending; use ?status=all for all.
    /// </summary>
    [HttpGet("odata/{module}/{entityType}/{entityId}/change-requests")]
    public async Task<IActionResult> GetChangeRequests(
        string module, string entityType, string entityId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var recordKey = $"{module}/{entityType}/{entityId}";
        var all = await _changeRequestStore.GetByRecordAsync(recordKey, tenantId);

        var filtered = status?.ToLowerInvariant() == "all"
            ? all
            : all.Where(r => r.Status == "pending").ToList();

        return Ok(filtered.Select(ToChangeRequestDto).ToList());
    }

    /// <summary>
    /// POST api/odata/{module}/{entityType}/{entityId}/change-requests
    /// Creates a new change request proposing field-level changes on a record.
    /// </summary>
    [HttpPost("odata/{module}/{entityType}/{entityId}/change-requests")]
    public async Task<IActionResult> CreateChangeRequest(
        string module, string entityType, string entityId,
        [FromBody] CreateChangeRequestRequest request,
        CancellationToken ct)
    {
        if (request.ProposedChanges == null || request.ProposedChanges.Count == 0)
        {
            return BadRequest(new { error = "ProposedChanges must not be empty." });
        }

        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var proposedById = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var proposedByName = User.FindFirst(ClaimTypes.Name)?.Value ?? proposedById;
        var recordKey = $"{module}/{entityType}/{entityId}";

        var req = new ChangeRequest
        {
            RecordKey = recordKey,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            ProposedById = proposedById,
            ProposedByName = proposedByName,
            ProposedChanges = request.ProposedChanges,
            Status = "pending"
        };

        var created = await _changeRequestStore.CreateAsync(req, tenantId);
        _logger.LogInformation("ChangeRequest {RequestId} created on record {RecordKey}", created.Id, recordKey);
        return CreatedAtAction(nameof(GetChangeRequests), new { module, entityType, entityId }, ToChangeRequestDto(created));
    }

    /// <summary>
    /// PUT api/odata/{module}/{entityType}/{entityId}/change-requests/{requestId}/review
    /// Reviews (approve or reject) a pending change request and broadcasts the update.
    /// </summary>
    [HttpPut("odata/{module}/{entityType}/{entityId}/change-requests/{requestId:guid}/review")]
    public async Task<IActionResult> ReviewChangeRequest(
        string module, string entityType, string entityId,
        Guid requestId,
        [FromBody] ReviewChangeRequestRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        var notFound = await VerifyEntityExistsInTenantAsync(module, entityType, entityId, tenantId, ct);
        if (notFound != null) return notFound;

        var existing = await _changeRequestStore.GetByIdAsync(requestId, tenantId);
        if (existing == null)
        {
            return NotFound(new { error = $"ChangeRequest {requestId} not found." });
        }

        var newStatus = request.Decision?.ToLowerInvariant() switch
        {
            "approve" => "approved",
            "reject"  => "rejected",
            _         => null
        };

        if (newStatus == null)
        {
            return BadRequest(new { error = "Decision must be 'approve' or 'reject'." });
        }

        var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var reviewerName = User.FindFirst(ClaimTypes.Name)?.Value ?? reviewerId;
        var recordKey = $"{module}/{entityType}/{entityId}";

        var updated = await _changeRequestStore.UpdateStatusAsync(
            requestId, newStatus, reviewerId, reviewerName, request.Comment, tenantId);

        await _hubContext.Clients.Group($"record:{recordKey}").ChangeRequestUpdated(
            new ChangeRequestNotification(
                recordKey,
                updated.Id.ToString(),
                updated.Status,
                updated.ProposedByName));

        _logger.LogInformation("ChangeRequest {RequestId} reviewed: {Status} by {ReviewerId}", requestId, newStatus, reviewerId);
        return Ok(ToChangeRequestDto(updated));
    }

    // =========================================================================
    // RECORD LOCK STATUS
    // =========================================================================

    /// <summary>
    /// GET api/odata/{module}/{entityType}/{entityId}/lock
    /// Returns whether a record is currently being edited (locked) by another user.
    /// Requires authentication — response includes user details of the lock holder.
    /// </summary>
    [HttpGet("odata/{module}/{entityType}/{entityId}/lock")]
    public IActionResult GetLockStatus(string module, string entityType, string entityId)
    {
        var recordKey = $"{module}/{entityType}/{entityId}";
        var lockInfo = NotificationHub.GetLock(recordKey);

        if (lockInfo == null)
        {
            return Ok(new { locked = false });
        }

        return Ok(new
        {
            locked = true,
            userId = lockInfo.UserId,
            displayName = lockInfo.DisplayName,
            startedAt = lockInfo.StartedAt
        });
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Verify that the entity record exists in the tenant's data.
    /// Returns null if the entity exists, or a NotFound result if it does not.
    /// </summary>
    private async Task<IActionResult?> VerifyEntityExistsInTenantAsync(
        string module, string entityType, string entityId, Guid tenantId, CancellationToken ct)
    {
        var entityDef = await _entityResolver.ResolveEntityAsync(module, entityType);
        if (entityDef == null)
        {
            return NotFound(new { error = $"Entity type '{module}.{entityType}' not found." });
        }

        var tableName = NamingConvention.GetQualifiedTableName(entityDef, module);

        // Build parameterized existence check with tenant isolation
        var sql = entityDef.TenantScoped
            ? $"SELECT COUNT(*) FROM {tableName} WHERE id = @p_id AND tenant_id = @p_tid"
            : $"SELECT COUNT(*) FROM {tableName} WHERE id = @p_id";

        if (!Guid.TryParse(entityId, out var entityGuid))
        {
            return NotFound(new { error = "Invalid entity ID format." });
        }

        var parameters = new List<NpgsqlParameter>
        {
            new("@p_id", entityGuid)
        };
        if (entityDef.TenantScoped)
        {
            parameters.Add(new NpgsqlParameter("@p_tid", tenantId));
        }

        var count = await _queryExecutor.ExecuteScalarAsync<long>(sql, parameters, ct);
        if (count == 0)
        {
            return NotFound(new { error = $"Entity record '{entityId}' not found." });
        }

        return null; // Entity exists
    }

    private static CommentDto ToCommentDto(Comment c) =>
        new(c.Id, c.AuthorId, c.AuthorName, c.Content, c.Mentions, c.LikedBy, c.CreatedAt, c.UpdatedAt);

    private static ChangeRequestDto ToChangeRequestDto(ChangeRequest r) =>
        new(r.Id, r.ProposedById, r.ProposedByName, r.ProposedChanges, r.Status,
            r.ReviewerId, r.ReviewerName, r.ReviewComment, r.ReviewedAt, r.CreatedAt);
}
