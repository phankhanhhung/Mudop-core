namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Represents a registered webhook endpoint that receives event notifications.
/// </summary>
public class WebhookConfig
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetUrl { get; set; } = "";

    /// <summary>
    /// Optional HMAC secret used to sign webhook payloads (X-Webhook-Signature header).
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Glob patterns to filter which events are delivered (e.g. ["Customer.*", "Order.created"]).
    /// Empty array means all events are delivered.
    /// </summary>
    public string[] EventFilter { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Records the outcome of a single webhook delivery attempt.
/// </summary>
public class WebhookDeliveryLog
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public Guid? OutboxEntryId { get; set; }
    public string EventName { get; set; } = "";
    public string TargetUrl { get; set; } = "";

    /// <summary>
    /// HTTP status code returned by the target. 0 indicates a connection-level failure.
    /// </summary>
    public int StatusCode { get; set; }

    public bool Success { get; set; }

    /// <summary>
    /// Request body truncated to the first 2000 characters.
    /// </summary>
    public string? RequestBody { get; set; }

    /// <summary>
    /// Response body truncated to the first 500 characters.
    /// </summary>
    public string? ResponseBody { get; set; }

    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
    public DateTime AttemptedAt { get; set; }
}

/// <summary>
/// Persistence interface for webhook configuration and delivery log management.
/// </summary>
public interface IWebhookStore
{
    /// <summary>
    /// Get all webhooks with is_active = true.
    /// </summary>
    Task<IReadOnlyList<WebhookConfig>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all webhooks regardless of active status.
    /// </summary>
    Task<IReadOnlyList<WebhookConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a single webhook by its primary key.
    /// </summary>
    Task<WebhookConfig?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Insert a new webhook configuration. Id and CreatedAt are assigned by the database.
    /// </summary>
    Task<WebhookConfig> CreateAsync(WebhookConfig config, CancellationToken ct = default);

    /// <summary>
    /// Update an existing webhook configuration. UpdatedAt is set to NOW() by the database.
    /// </summary>
    Task<WebhookConfig> UpdateAsync(WebhookConfig config, CancellationToken ct = default);

    /// <summary>
    /// Permanently delete a webhook configuration by id.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Append a delivery attempt record to the delivery log.
    /// </summary>
    Task LogDeliveryAsync(WebhookDeliveryLog log, CancellationToken ct = default);

    /// <summary>
    /// Retrieve delivery log entries, optionally filtered by webhook id, ordered by most recent first.
    /// </summary>
    Task<IReadOnlyList<WebhookDeliveryLog>> GetDeliveryLogsAsync(Guid? webhookId = null, int limit = 100, CancellationToken ct = default);
}

/// <summary>
/// PostgreSQL-backed store for webhook configurations and delivery logs.
/// Uses raw Npgsql — same pattern as <see cref="OutboxStore"/>.
/// </summary>
public class WebhookStore : IWebhookStore
{
    private readonly string _connectionString;
    private readonly ILogger<WebhookStore> _logger;

    private const string WebhookConfigsTable = SchemaConstants.WebhookConfigsTable;
    private const string WebhookDeliveryLogTable = SchemaConstants.WebhookDeliveryLogTable;

    public WebhookStore(string connectionString, ILogger<WebhookStore> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WebhookConfig>> GetAllActiveAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, name, target_url, secret, event_filter, is_active, created_at, updated_at
            FROM {WebhookConfigsTable}
            WHERE is_active = true
            ORDER BY created_at
            """;

        return await QueryConfigsAsync(sql, ct);
    }

    public async Task<IReadOnlyList<WebhookConfig>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, name, target_url, secret, event_filter, is_active, created_at, updated_at
            FROM {WebhookConfigsTable}
            ORDER BY created_at
            """;

        return await QueryConfigsAsync(sql, ct);
    }

    public async Task<WebhookConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, name, target_url, secret, event_filter, is_active, created_at, updated_at
            FROM {WebhookConfigsTable}
            WHERE id = @id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadConfig(reader);
        }

        return null;
    }

    public async Task<WebhookConfig> CreateAsync(WebhookConfig config, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {WebhookConfigsTable} (name, target_url, secret, event_filter, is_active)
            VALUES (@name, @target_url, @secret, @event_filter, @is_active)
            RETURNING id, name, target_url, secret, event_filter, is_active, created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddConfigParameters(cmd, config);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var created = ReadConfig(reader);
        _logger.LogInformation("Webhook created: {WebhookId} ({Name})", created.Id, created.Name);
        return created;
    }

    public async Task<WebhookConfig> UpdateAsync(WebhookConfig config, CancellationToken ct = default)
    {
        string sql = $"""
            UPDATE {WebhookConfigsTable}
            SET name         = @name,
                target_url   = @target_url,
                secret       = @secret,
                event_filter = @event_filter,
                is_active    = @is_active,
                updated_at   = NOW()
            WHERE id = @id
            RETURNING id, name, target_url, secret, event_filter, is_active, created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", config.Id);
        AddConfigParameters(cmd, config);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException($"Webhook {config.Id} not found for update.");
        }

        var updated = ReadConfig(reader);
        _logger.LogInformation("Webhook updated: {WebhookId} ({Name})", updated.Id, updated.Name);
        return updated;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        string sql = $"DELETE FROM {WebhookConfigsTable} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Webhook deleted: {WebhookId}", id);
    }

    public async Task LogDeliveryAsync(WebhookDeliveryLog log, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {WebhookDeliveryLogTable}
                (webhook_id, outbox_entry_id, event_name, target_url, status_code,
                 success, request_body, response_body, error_message, duration_ms)
            VALUES
                (@webhook_id, @outbox_entry_id, @event_name, @target_url, @status_code,
                 @success, @request_body, @response_body, @error_message, @duration_ms)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("webhook_id", log.WebhookId);
        cmd.Parameters.AddWithValue("outbox_entry_id", (object?)log.OutboxEntryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("event_name", log.EventName);
        cmd.Parameters.AddWithValue("target_url", log.TargetUrl);
        cmd.Parameters.AddWithValue("status_code", log.StatusCode);
        cmd.Parameters.AddWithValue("success", log.Success);
        cmd.Parameters.AddWithValue("request_body", (object?)log.RequestBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("response_body", (object?)log.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_message", (object?)log.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("duration_ms", log.DurationMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryLog>> GetDeliveryLogsAsync(
        Guid? webhookId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        string whereClause = webhookId.HasValue ? "WHERE webhook_id = @webhook_id" : "";
        string sql = $"""
            SELECT id, webhook_id, outbox_entry_id, event_name, target_url,
                   status_code, success, request_body, response_body, error_message,
                   duration_ms, attempted_at
            FROM {WebhookDeliveryLogTable}
            {whereClause}
            ORDER BY attempted_at DESC
            LIMIT @limit
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (webhookId.HasValue)
        {
            cmd.Parameters.AddWithValue("webhook_id", webhookId.Value);
        }
        cmd.Parameters.AddWithValue("limit", limit);

        var logs = new List<WebhookDeliveryLog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            logs.Add(ReadDeliveryLog(reader));
        }

        return logs;
    }

    // --- private helpers ---

    private async Task<IReadOnlyList<WebhookConfig>> QueryConfigsAsync(string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);

        var configs = new List<WebhookConfig>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            configs.Add(ReadConfig(reader));
        }

        return configs;
    }

    /// <summary>
    /// Adds the five data parameters shared by INSERT and UPDATE (excludes id).
    /// </summary>
    private static void AddConfigParameters(NpgsqlCommand cmd, WebhookConfig config)
    {
        cmd.Parameters.AddWithValue("name", config.Name);
        cmd.Parameters.AddWithValue("target_url", config.TargetUrl);
        cmd.Parameters.AddWithValue("secret", (object?)config.Secret ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("event_filter", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(config.EventFilter)
        });
        cmd.Parameters.AddWithValue("is_active", config.IsActive);
    }

    private static WebhookConfig ReadConfig(NpgsqlDataReader reader)
    {
        var eventFilterJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
        var eventFilter = JsonSerializer.Deserialize<string[]>(eventFilterJson) ?? [];

        return new WebhookConfig
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            TargetUrl = reader.GetString(2),
            Secret = reader.IsDBNull(3) ? null : reader.GetString(3),
            EventFilter = eventFilter,
            IsActive = reader.GetBoolean(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
        };
    }

    private static WebhookDeliveryLog ReadDeliveryLog(NpgsqlDataReader reader)
    {
        return new WebhookDeliveryLog
        {
            Id = reader.GetGuid(0),
            WebhookId = reader.GetGuid(1),
            OutboxEntryId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            EventName = reader.GetString(3),
            TargetUrl = reader.GetString(4),
            StatusCode = reader.GetInt32(5),
            Success = reader.GetBoolean(6),
            RequestBody = reader.IsDBNull(7) ? null : reader.GetString(7),
            ResponseBody = reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
            DurationMs = reader.GetInt32(10),
            AttemptedAt = reader.GetDateTime(11)
        };
    }
}
