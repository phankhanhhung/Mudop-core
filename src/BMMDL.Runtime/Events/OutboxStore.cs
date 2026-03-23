namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Represents a single entry in the event outbox table.
/// </summary>
public class OutboxEntry
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = "";
    public string EntityName { get; set; } = "";
    public Guid? EntityId { get; set; }
    public Guid? TenantId { get; set; }
    public string Payload { get; set; } = "{}";
    public string? Context { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether this outbox entry represents an integration event that should be
    /// routed to an external broker adapter instead of the in-memory event publisher.
    /// Parsed from the context JSONB column.
    /// </summary>
    public bool IsIntegration { get; set; }
}

/// <summary>
/// Interface for the outbox store — transactional event persistence for guaranteed delivery.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Enqueue an event into the outbox within the provided transaction.
    /// The INSERT is part of the same transaction as the entity CRUD, ensuring atomicity.
    /// </summary>
    Task EnqueueAsync(DomainEvent @event, NpgsqlTransaction? transaction, bool isIntegration = false, CancellationToken ct = default);

    /// <summary>
    /// Get pending outbox entries ready for delivery (status='pending' and next_retry_at is past or null).
    /// Uses FOR UPDATE SKIP LOCKED for concurrent-safe batch processing.
    /// </summary>
    Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize = 50, CancellationToken ct = default);

    /// <summary>
    /// Mark an outbox entry as successfully delivered.
    /// </summary>
    Task MarkDeliveredAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Mark an outbox entry as failed, incrementing retry count with exponential backoff.
    /// Moves to 'dead_letter' status when max retries exceeded.
    /// </summary>
    Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken ct = default);

}

/// <summary>
/// PostgreSQL-backed outbox store for durable event delivery.
/// Events are inserted within the entity CRUD transaction for atomicity.
/// A background processor polls for pending entries and dispatches them.
/// </summary>
public class OutboxStore : IOutboxStore
{
    private readonly string _connectionString;
    private readonly ILogger<OutboxStore> _logger;

    public OutboxStore(string connectionString, ILogger<OutboxStore> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnqueueAsync(DomainEvent @event, NpgsqlTransaction? transaction, bool isIntegration = false, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {SchemaConstants.EventOutboxTable} (event_name, entity_name, entity_id, tenant_id, payload, context, schema_version)
            VALUES (@event_name, @entity_name, @entity_id, @tenant_id, @payload, @context, @schema_version)
            """;

        NpgsqlCommand cmd;
        if (transaction != null)
        {
            // Use the transaction's connection — same transaction as entity CRUD
            cmd = new NpgsqlCommand(sql, transaction.Connection, transaction);
        }
        else
        {
            // Standalone (no active UoW)
            var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            cmd = new NpgsqlCommand(sql, conn);
        }

        try
        {
            cmd.Parameters.AddWithValue("event_name", @event.EventName);
            cmd.Parameters.AddWithValue("entity_name", @event.EntityName);
            cmd.Parameters.AddWithValue("entity_id", (object?)@event.EntityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(SchemaConstants.TenantIdColumn, (object?)@event.TenantId ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(@event.Payload)
            });
            cmd.Parameters.Add(new NpgsqlParameter("context", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(new
                {
                    eventId = @event.EventId,
                    correlationId = @event.CorrelationId,
                    causationId = @event.CausationId,
                    userId = @event.UserId,
                    sourceModule = @event.SourceModule,
                    isIntegration
                })
            });
            cmd.Parameters.AddWithValue("schema_version", @event.SchemaVersion);

            await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Outbox: enqueued event {EventName} for {EntityName}/{EntityId}",
                @event.EventName, @event.EntityName, @event.EntityId);
        }
        finally
        {
            // If we created a standalone connection, dispose it
            if (transaction == null && cmd.Connection != null)
            {
                await cmd.Connection.DisposeAsync();
            }
            await cmd.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize = 50, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, event_name, entity_name, entity_id, tenant_id, payload, context,
                   schema_version, status, retry_count, max_retries, next_retry_at,
                   created_at, processed_at, error_message
            FROM {SchemaConstants.EventOutboxTable}
            WHERE status = 'pending'
              AND (next_retry_at IS NULL OR next_retry_at <= NOW())
            ORDER BY created_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("batch_size", batchSize);

        var entries = new List<OutboxEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public async Task MarkDeliveredAsync(Guid id, CancellationToken ct = default)
    {
        string sql = $"""
            UPDATE {SchemaConstants.EventOutboxTable}
            SET status = 'delivered', processed_at = NOW()
            WHERE id = @id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken ct = default)
    {
        // Exponential backoff: 2^retryCount seconds (1s, 2s, 4s, 8s, 16s, ...)
        // Move to dead_letter when max_retries exceeded
        string sql = $"""
            UPDATE {SchemaConstants.EventOutboxTable}
            SET retry_count = retry_count + 1,
                error_message = @error_message,
                next_retry_at = NOW() + make_interval(secs => power(2, retry_count + 1)),
                status = CASE WHEN retry_count + 1 >= max_retries THEN 'dead_letter' ELSE 'pending' END
            WHERE id = @id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("error_message", errorMessage);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static OutboxEntry ReadEntry(NpgsqlDataReader reader)
    {
        var context = reader.IsDBNull(6) ? null : reader.GetString(6);
        var isIntegration = false;
        if (context != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(context);
                if (doc.RootElement.TryGetProperty("isIntegration", out var prop) && prop.ValueKind == JsonValueKind.True)
                {
                    isIntegration = true;
                }
            }
            catch (JsonException)
            {
                // Malformed JSON context is non-fatal — defaults to non-integration event.
                // This can occur if an older event was persisted with an incompatible context format.
            }
        }

        return new OutboxEntry
        {
            Id = reader.GetGuid(0),
            EventName = reader.GetString(1),
            EntityName = reader.GetString(2),
            EntityId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            TenantId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Payload = reader.IsDBNull(5) ? "{}" : reader.GetString(5),
            Context = context,
            SchemaVersion = reader.GetInt32(7),
            Status = reader.GetString(8),
            RetryCount = reader.GetInt32(9),
            MaxRetries = reader.GetInt32(10),
            NextRetryAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            CreatedAt = reader.GetDateTime(12),
            ProcessedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            ErrorMessage = reader.IsDBNull(14) ? null : reader.GetString(14),
            IsIntegration = isIntegration
        };
    }
}
