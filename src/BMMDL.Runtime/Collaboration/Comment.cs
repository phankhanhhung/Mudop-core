namespace BMMDL.Runtime.Collaboration;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public class Comment
{
    public Guid Id { get; set; }
    public string RecordKey { get; set; } = "";      // "{module}/{entityType}/{entityId}"
    public string Module { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Content { get; set; } = "";
    public List<string> Mentions { get; set; } = [];  // usernames mentioned with @
    public List<string> LikedBy { get; set; } = [];   // user IDs who liked
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ChangeRequest
{
    public Guid Id { get; set; }
    public string RecordKey { get; set; } = "";
    public string Module { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string ProposedById { get; set; } = "";
    public string ProposedByName { get; set; } = "";
    public Dictionary<string, object?> ProposedChanges { get; set; } = [];  // JSONB
    public string Status { get; set; } = "pending";   // "pending" | "approved" | "rejected"
    public string? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public string? ReviewComment { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface ICommentStore
{
    Task<List<Comment>> GetByRecordAsync(string recordKey, Guid tenantId);
    Task<Comment?> GetByIdAsync(Guid id, Guid tenantId);
    Task<Comment> CreateAsync(Comment comment, Guid tenantId);
    Task<Comment> ToggleLikeAsync(Guid commentId, string userId, Guid tenantId);
    Task DeleteAsync(Guid id, Guid tenantId);
}

public interface IChangeRequestStore
{
    Task<List<ChangeRequest>> GetByRecordAsync(string recordKey, Guid tenantId);
    Task<ChangeRequest?> GetByIdAsync(Guid id, Guid tenantId);
    Task<ChangeRequest> CreateAsync(ChangeRequest req, Guid tenantId);
    Task<ChangeRequest> UpdateStatusAsync(Guid id, string status, string reviewerId, string reviewerName, string? reviewComment, Guid tenantId);
}

/// <summary>
/// PostgreSQL-backed store for record comments.
/// Uses raw Npgsql — same pattern as <see cref="BMMDL.Runtime.Events.WebhookStore"/>.
/// </summary>
public class CommentStore : ICommentStore
{
    private readonly string _connectionString;
    private readonly ILogger<CommentStore> _logger;

    private const string CommentsTable = SchemaConstants.CommentTable;

    public CommentStore(string connectionString, ILogger<CommentStore> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Comment>> GetByRecordAsync(string recordKey, Guid tenantId)
    {
        string sql = $"""
            SELECT id, record_key, module, entity_type, entity_id,
                   author_id, author_name, content, mentions, liked_by,
                   created_at, updated_at
            FROM {CommentsTable}
            WHERE record_key = @record_key AND tenant_id = @tenant_id
            ORDER BY created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("record_key", recordKey);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var results = new List<Comment>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadComment(reader));
        }

        return results;
    }

    public async Task<Comment?> GetByIdAsync(Guid id, Guid tenantId)
    {
        string sql = $"""
            SELECT id, record_key, module, entity_type, entity_id,
                   author_id, author_name, content, mentions, liked_by,
                   created_at, updated_at
            FROM {CommentsTable}
            WHERE id = @id AND tenant_id = @tenant_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadComment(reader);
        }

        return null;
    }

    public async Task<Comment> CreateAsync(Comment comment, Guid tenantId)
    {
        string sql = $"""
            INSERT INTO {CommentsTable}
                (record_key, module, entity_type, entity_id, author_id, author_name, content, mentions, liked_by, tenant_id)
            VALUES
                (@record_key, @module, @entity_type, @entity_id, @author_id, @author_name, @content, @mentions, @liked_by, @tenant_id)
            RETURNING id, record_key, module, entity_type, entity_id,
                      author_id, author_name, content, mentions, liked_by,
                      created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddCommentParameters(cmd, comment);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var created = ReadComment(reader);
        _logger.LogInformation("Comment created: {CommentId} on {RecordKey}", created.Id, created.RecordKey);
        return created;
    }

    public async Task<Comment> ToggleLikeAsync(Guid commentId, string userId, Guid tenantId)
    {
        // V6: Atomic toggle using a single SQL statement to avoid TOCTOU race condition.
        // Uses jsonb array manipulation: if userId is in liked_by, remove it; otherwise, add it.
        // This is done in a single UPDATE so concurrent requests cannot produce duplicates or lost updates.
        string sql = $$"""
            UPDATE {{CommentsTable}}
            SET liked_by = CASE
                WHEN liked_by @> to_jsonb(@user_id::text)
                THEN (
                    SELECT COALESCE(jsonb_agg(elem), '[]'::jsonb)
                    FROM jsonb_array_elements(liked_by) AS elem
                    WHERE elem #>> '{}' <> @user_id
                )
                ELSE liked_by || to_jsonb(@user_id::text)
            END,
                updated_at = NOW()
            WHERE id = @id AND tenant_id = @tenant_id
            RETURNING id, record_key, module, entity_type, entity_id,
                      author_id, author_name, content, mentions, liked_by,
                      created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", commentId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Comment {commentId} not found for like toggle.");
        }

        return ReadComment(reader);
    }

    public async Task DeleteAsync(Guid id, Guid tenantId)
    {
        string sql = $"DELETE FROM {CommentsTable} WHERE id = @id AND tenant_id = @tenant_id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Comment deleted: {CommentId}", id);
    }

    // --- private helpers ---

    private static void AddCommentParameters(NpgsqlCommand cmd, Comment comment)
    {
        cmd.Parameters.AddWithValue("record_key", comment.RecordKey);
        cmd.Parameters.AddWithValue("module", comment.Module);
        cmd.Parameters.AddWithValue("entity_type", comment.EntityType);
        cmd.Parameters.AddWithValue("entity_id", comment.EntityId);
        cmd.Parameters.AddWithValue("author_id", comment.AuthorId);
        cmd.Parameters.AddWithValue("author_name", comment.AuthorName);
        cmd.Parameters.AddWithValue("content", comment.Content);
        cmd.Parameters.Add(new NpgsqlParameter("mentions", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(comment.Mentions)
        });
        cmd.Parameters.Add(new NpgsqlParameter("liked_by", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(comment.LikedBy)
        });
    }

    private static Comment ReadComment(NpgsqlDataReader reader)
    {
        var mentionsJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8);
        var likedByJson = reader.IsDBNull(9) ? "[]" : reader.GetString(9);

        return new Comment
        {
            Id = reader.GetGuid(0),
            RecordKey = reader.GetString(1),
            Module = reader.GetString(2),
            EntityType = reader.GetString(3),
            EntityId = reader.GetString(4),
            AuthorId = reader.GetString(5),
            AuthorName = reader.GetString(6),
            Content = reader.GetString(7),
            Mentions = JsonSerializer.Deserialize<List<string>>(mentionsJson) ?? [],
            LikedBy = JsonSerializer.Deserialize<List<string>>(likedByJson) ?? [],
            CreatedAt = reader.GetDateTime(10),
            UpdatedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11)
        };
    }
}

/// <summary>
/// PostgreSQL-backed store for change requests against entity records.
/// Uses raw Npgsql — same pattern as <see cref="BMMDL.Runtime.Events.WebhookStore"/>.
/// </summary>
public class ChangeRequestStore : IChangeRequestStore
{
    private readonly string _connectionString;
    private readonly ILogger<ChangeRequestStore> _logger;

    private static readonly JsonSerializerOptions ProposedChangesOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ChangeRequestsTable = SchemaConstants.ChangeRequestTable;

    public ChangeRequestStore(string connectionString, ILogger<ChangeRequestStore> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ChangeRequest>> GetByRecordAsync(string recordKey, Guid tenantId)
    {
        string sql = $"""
            SELECT id, record_key, module, entity_type, entity_id,
                   proposed_by_id, proposed_by_name, proposed_changes,
                   status, reviewer_id, reviewer_name, review_comment,
                   reviewed_at, created_at
            FROM {ChangeRequestsTable}
            WHERE record_key = @record_key AND tenant_id = @tenant_id
            ORDER BY created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("record_key", recordKey);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var results = new List<ChangeRequest>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadChangeRequest(reader));
        }

        return results;
    }

    public async Task<ChangeRequest?> GetByIdAsync(Guid id, Guid tenantId)
    {
        string sql = $"""
            SELECT id, record_key, module, entity_type, entity_id,
                   proposed_by_id, proposed_by_name, proposed_changes,
                   status, reviewer_id, reviewer_name, review_comment,
                   reviewed_at, created_at
            FROM {ChangeRequestsTable}
            WHERE id = @id AND tenant_id = @tenant_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadChangeRequest(reader);
        }

        return null;
    }

    public async Task<ChangeRequest> CreateAsync(ChangeRequest req, Guid tenantId)
    {
        string sql = $"""
            INSERT INTO {ChangeRequestsTable}
                (record_key, module, entity_type, entity_id,
                 proposed_by_id, proposed_by_name, proposed_changes, status, tenant_id)
            VALUES
                (@record_key, @module, @entity_type, @entity_id,
                 @proposed_by_id, @proposed_by_name, @proposed_changes, @status, @tenant_id)
            RETURNING id, record_key, module, entity_type, entity_id,
                      proposed_by_id, proposed_by_name, proposed_changes,
                      status, reviewer_id, reviewer_name, review_comment,
                      reviewed_at, created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddChangeRequestParameters(cmd, req);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var created = ReadChangeRequest(reader);
        _logger.LogInformation("ChangeRequest created: {ChangeRequestId} on {RecordKey}", created.Id, created.RecordKey);
        return created;
    }

    public async Task<ChangeRequest> UpdateStatusAsync(
        Guid id,
        string status,
        string reviewerId,
        string reviewerName,
        string? reviewComment,
        Guid tenantId)
    {
        string sql = $"""
            UPDATE {ChangeRequestsTable}
            SET status         = @status,
                reviewer_id    = @reviewer_id,
                reviewer_name  = @reviewer_name,
                review_comment = @review_comment,
                reviewed_at    = NOW()
            WHERE id = @id AND tenant_id = @tenant_id
            RETURNING id, record_key, module, entity_type, entity_id,
                      proposed_by_id, proposed_by_name, proposed_changes,
                      status, reviewer_id, reviewer_name, review_comment,
                      reviewed_at, created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("reviewer_id", reviewerId);
        cmd.Parameters.AddWithValue("reviewer_name", reviewerName);
        cmd.Parameters.AddWithValue("review_comment", (object?)reviewComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"ChangeRequest {id} not found for status update.");
        }

        var updated = ReadChangeRequest(reader);
        _logger.LogInformation("ChangeRequest {ChangeRequestId} status updated to {Status}", updated.Id, updated.Status);
        return updated;
    }

    // --- private helpers ---

    private static void AddChangeRequestParameters(NpgsqlCommand cmd, ChangeRequest req)
    {
        cmd.Parameters.AddWithValue("record_key", req.RecordKey);
        cmd.Parameters.AddWithValue("module", req.Module);
        cmd.Parameters.AddWithValue("entity_type", req.EntityType);
        cmd.Parameters.AddWithValue("entity_id", req.EntityId);
        cmd.Parameters.AddWithValue("proposed_by_id", req.ProposedById);
        cmd.Parameters.AddWithValue("proposed_by_name", req.ProposedByName);
        cmd.Parameters.Add(new NpgsqlParameter("proposed_changes", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(req.ProposedChanges)
        });
        cmd.Parameters.AddWithValue("status", req.Status);
    }

    private ChangeRequest ReadChangeRequest(NpgsqlDataReader reader)
    {
        var proposedChangesJson = reader.IsDBNull(7) ? "{}" : reader.GetString(7);
        var proposedChanges = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            proposedChangesJson, ProposedChangesOptions) ?? [];

        return new ChangeRequest
        {
            Id = reader.GetGuid(0),
            RecordKey = reader.GetString(1),
            Module = reader.GetString(2),
            EntityType = reader.GetString(3),
            EntityId = reader.GetString(4),
            ProposedById = reader.GetString(5),
            ProposedByName = reader.GetString(6),
            ProposedChanges = proposedChanges,
            Status = reader.GetString(8),
            ReviewerId = reader.IsDBNull(9) ? null : reader.GetString(9),
            ReviewerName = reader.IsDBNull(10) ? null : reader.GetString(10),
            ReviewComment = reader.IsDBNull(11) ? null : reader.GetString(11),
            ReviewedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            CreatedAt = reader.GetDateTime(13)
        };
    }
}
