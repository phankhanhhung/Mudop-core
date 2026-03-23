namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using BMMDL.Runtime.DataAccess;

public interface IAuditLogStore
{
    Task StoreAsync(DomainEvent domainEvent, CancellationToken ct = default);
    Task<List<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default);
    Task<int> CountAsync(AuditLogQuery query, CancellationToken ct = default);
    Task<List<AuditLogEntry>> QueryByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
}

public record AuditLogEntry
{
    public Guid Id { get; init; }
    public string EventName { get; init; } = "";
    public string EntityName { get; init; } = "";
    public Guid? EntityId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
    public DateTime CreatedAt { get; init; }
}

public record AuditLogQuery
{
    public string? EntityName { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? TenantId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? EventType { get; init; } // "Created", "Updated", "Deleted"
    public int Top { get; init; } = 50;
    public int Skip { get; init; } = 0;
}

public class PostgresAuditLogStore : IAuditLogStore
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresAuditLogStore> _logger;

    public PostgresAuditLogStore(ITenantConnectionFactory connectionFactory, ILogger<PostgresAuditLogStore> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task StoreAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {SchemaConstants.AuditLogsTable} (event_name, entity_name, entity_id, tenant_id, user_id, correlation_id, causation_id, payload, created_at)
            VALUES (@event_name, @entity_name, @entity_id, @tenant_id, @user_id, @correlation_id, @causation_id, @payload, @created_at)
            """;

        try
        {
            await using var conn = await _connectionFactory.GetConnectionAsync(null, ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("event_name", domainEvent.EventName);
            cmd.Parameters.AddWithValue("entity_name", domainEvent.EntityName);
            cmd.Parameters.AddWithValue("entity_id", (object?)domainEvent.EntityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(SchemaConstants.TenantIdColumn, (object?)domainEvent.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("user_id", (object?)domainEvent.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("correlation_id", (object?)domainEvent.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("causation_id", (object?)domainEvent.CausationId ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(domainEvent.Payload) });
            cmd.Parameters.AddWithValue(SchemaConstants.CreatedAtColumn, domainEvent.Timestamp);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store audit log for {EventName}", domainEvent.EventName);
            // Don't throw - audit logging should not block operations
        }
    }

    public async Task<List<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var (whereClauses, parameters) = BuildWhereClause(query);
        var whereStr = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        var sql = $"""
            SELECT id, event_name, entity_name, entity_id, tenant_id, user_id, correlation_id, causation_id, payload, created_at
            FROM {SchemaConstants.AuditLogsTable}
            {whereStr}
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset
            """;

        parameters.Add(new NpgsqlParameter("limit", query.Top));
        parameters.Add(new NpgsqlParameter("offset", query.Skip));

        await using var conn = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        var results = new List<AuditLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadEntry(reader));
        }
        return results;
    }

    public async Task<List<AuditLogEntry>> QueryByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, event_name, entity_name, entity_id, tenant_id, user_id, correlation_id, causation_id, payload, created_at
            FROM {SchemaConstants.AuditLogsTable}
            WHERE correlation_id = @correlation_id
            ORDER BY created_at ASC
            """;

        await using var conn = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("correlation_id", correlationId);

        var results = new List<AuditLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadEntry(reader));
        }
        return results;
    }

    public async Task<int> CountAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var (whereClauses, parameters) = BuildWhereClause(query);
        var whereStr = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        var sql = $"SELECT COUNT(*) FROM {SchemaConstants.AuditLogsTable} {whereStr}";

        await using var conn = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        var count = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(count);
    }

    private static (List<string> clauses, List<NpgsqlParameter> parameters) BuildWhereClause(AuditLogQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        var paramIdx = 0;

        if (!string.IsNullOrEmpty(query.EntityName))
        {
            clauses.Add($"entity_name = @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.EntityName));
        }
        if (query.EntityId.HasValue)
        {
            clauses.Add($"entity_id = @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.EntityId.Value));
        }
        if (query.UserId.HasValue)
        {
            clauses.Add($"user_id = @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.UserId.Value));
        }
        if (query.TenantId.HasValue)
        {
            clauses.Add($"tenant_id = @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.TenantId.Value));
        }
        if (query.From.HasValue)
        {
            clauses.Add($"created_at >= @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.From.Value));
        }
        if (query.To.HasValue)
        {
            clauses.Add($"created_at <= @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", query.To.Value));
        }
        if (!string.IsNullOrEmpty(query.EventType))
        {
            clauses.Add($"event_name ILIKE @p{paramIdx}");
            parameters.Add(new NpgsqlParameter($"p{paramIdx++}", $"%{query.EventType}"));
        }

        return (clauses, parameters);
    }

    private static AuditLogEntry ReadEntry(NpgsqlDataReader reader)
    {
        var idOrdinal = reader.GetOrdinal("id");
        var eventNameOrdinal = reader.GetOrdinal("event_name");
        var entityNameOrdinal = reader.GetOrdinal("entity_name");
        var entityIdOrdinal = reader.GetOrdinal("entity_id");
        var tenantIdOrdinal = reader.GetOrdinal(SchemaConstants.TenantIdColumn);
        var userIdOrdinal = reader.GetOrdinal("user_id");
        var correlationIdOrdinal = reader.GetOrdinal("correlation_id");
        var causationIdOrdinal = reader.GetOrdinal("causation_id");
        var payloadOrdinal = reader.GetOrdinal("payload");
        var createdAtOrdinal = reader.GetOrdinal(SchemaConstants.CreatedAtColumn);

        var payloadJson = reader.IsDBNull(payloadOrdinal) ? "{}" : reader.GetString(payloadOrdinal);
        return new AuditLogEntry
        {
            Id = reader.GetGuid(idOrdinal),
            EventName = reader.GetString(eventNameOrdinal),
            EntityName = reader.GetString(entityNameOrdinal),
            EntityId = reader.IsDBNull(entityIdOrdinal) ? null : reader.GetGuid(entityIdOrdinal),
            TenantId = reader.IsDBNull(tenantIdOrdinal) ? null : reader.GetGuid(tenantIdOrdinal),
            UserId = reader.IsDBNull(userIdOrdinal) ? null : reader.GetGuid(userIdOrdinal),
            CorrelationId = reader.IsDBNull(correlationIdOrdinal) ? null : reader.GetString(correlationIdOrdinal),
            CausationId = reader.IsDBNull(causationIdOrdinal) ? null : reader.GetString(causationIdOrdinal),
            Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? new(),
            CreatedAt = reader.GetDateTime(createdAtOrdinal)
        };
    }
}
