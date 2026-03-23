using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using Npgsql;

namespace BMMDL.Runtime.Services;

/// <summary>
/// Represents a user preference (e.g. saved list view settings).
/// </summary>
public class UserPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string Settings { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Service for managing user preferences stored in {SchemaConstants.UserPreferencesTable}.
/// </summary>
public interface IUserPreferenceService
{
    Task<List<UserPreference>> GetPreferencesAsync(Guid userId, Guid tenantId, string category, string entityKey, CancellationToken ct = default);
    Task<UserPreference?> GetPreferenceByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserPreference> SavePreferenceAsync(UserPreference pref, CancellationToken ct = default);
    Task<UserPreference?> UpdatePreferenceAsync(Guid id, string? name, bool? isDefault, string? settings, CancellationToken ct = default);
    Task<bool> DeletePreferenceAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task SetDefaultAsync(Guid userId, Guid tenantId, string category, string entityKey, Guid id, CancellationToken ct = default);
    Task<List<UserPreference>> GetPreferencesByCategoryAsync(Guid userId, Guid tenantId, string category, CancellationToken ct = default);
}

public class UserPreferenceService : IUserPreferenceService
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public UserPreferenceService(ITenantConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<UserPreference>> GetPreferencesAsync(
        Guid userId, Guid tenantId, string category, string entityKey, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, user_id, tenant_id, category, entity_key, name, is_default, settings, created_at, updated_at
            FROM {SchemaConstants.UserPreferencesTable}
            WHERE user_id = @user_id AND tenant_id = @tenant_id AND category = @category AND entity_key = @entity_key
            ORDER BY name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("category", category));
        cmd.Parameters.Add(new NpgsqlParameter("entity_key", entityKey));

        var results = new List<UserPreference>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadPreference(reader));
        }
        return results;
    }

    public async Task<UserPreference?> GetPreferenceByIdAsync(Guid id, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, user_id, tenant_id, category, entity_key, name, is_default, settings, created_at, updated_at
            FROM {SchemaConstants.UserPreferencesTable}
            WHERE id = @id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("id", id));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPreference(reader);
    }

    public async Task<UserPreference> SavePreferenceAsync(UserPreference pref, CancellationToken ct = default)
    {
        pref.Id = Guid.NewGuid();
        pref.CreatedAt = DateTime.UtcNow;
        pref.UpdatedAt = DateTime.UtcNow;

        string sql = $"""
            INSERT INTO {SchemaConstants.UserPreferencesTable} (id, user_id, tenant_id, category, entity_key, name, is_default, settings, created_at, updated_at)
            VALUES (@id, @user_id, @tenant_id, @category, @entity_key, @name, @is_default, @settings::jsonb, @created_at, @updated_at)
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("id", pref.Id));
        cmd.Parameters.Add(new NpgsqlParameter("user_id", pref.UserId));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, pref.TenantId));
        cmd.Parameters.Add(new NpgsqlParameter("category", pref.Category));
        cmd.Parameters.Add(new NpgsqlParameter("entity_key", pref.EntityKey));
        cmd.Parameters.Add(new NpgsqlParameter("name", pref.Name));
        cmd.Parameters.Add(new NpgsqlParameter("is_default", pref.IsDefault));
        cmd.Parameters.Add(new NpgsqlParameter("settings", pref.Settings));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.CreatedAtColumn, pref.CreatedAt));
        cmd.Parameters.Add(new NpgsqlParameter("updated_at", pref.UpdatedAt));

        await cmd.ExecuteNonQueryAsync(ct);
        return pref;
    }

    public async Task<UserPreference?> UpdatePreferenceAsync(
        Guid id, string? name, bool? isDefault, string? settings, CancellationToken ct = default)
    {
        var setClauses = new List<string> { "updated_at = NOW()" };
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (name != null)
        {
            setClauses.Add("name = @name");
            parameters.Add(new NpgsqlParameter("name", name));
        }
        if (isDefault.HasValue)
        {
            setClauses.Add("is_default = @is_default");
            parameters.Add(new NpgsqlParameter("is_default", isDefault.Value));
        }
        if (settings != null)
        {
            setClauses.Add("settings = @settings::jsonb");
            parameters.Add(new NpgsqlParameter("settings", settings));
        }

        var sql = $"""
            UPDATE {SchemaConstants.UserPreferencesTable}
            SET {string.Join(", ", setClauses)}
            WHERE id = @id
            RETURNING id, user_id, tenant_id, category, entity_key, name, is_default, settings, created_at, updated_at
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters)
            cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value ?? DBNull.Value));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPreference(reader);
    }

    public async Task<bool> DeletePreferenceAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        string sql = $"""
            DELETE FROM {SchemaConstants.UserPreferencesTable}
            WHERE id = @id AND user_id = @user_id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("id", id));
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task SetDefaultAsync(
        Guid userId, Guid tenantId, string category, string entityKey, Guid id, CancellationToken ct = default)
    {
        string unmarkSql = $"""
            UPDATE {SchemaConstants.UserPreferencesTable}
            SET is_default = false, updated_at = NOW()
            WHERE user_id = @user_id AND tenant_id = @tenant_id AND category = @category AND entity_key = @entity_key AND is_default = true
            """;

        string markSql = $"""
            UPDATE {SchemaConstants.UserPreferencesTable}
            SET is_default = true, updated_at = NOW()
            WHERE id = @id AND user_id = @user_id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);

        await using var unmarkCmd = new NpgsqlCommand(unmarkSql, connection);
        unmarkCmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        unmarkCmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));
        unmarkCmd.Parameters.Add(new NpgsqlParameter("category", category));
        unmarkCmd.Parameters.Add(new NpgsqlParameter("entity_key", entityKey));
        await unmarkCmd.ExecuteNonQueryAsync(ct);

        await using var markCmd = new NpgsqlCommand(markSql, connection);
        markCmd.Parameters.Add(new NpgsqlParameter("id", id));
        markCmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        await markCmd.ExecuteNonQueryAsync(ct);
    }


    public async Task<List<UserPreference>> GetPreferencesByCategoryAsync(
        Guid userId, Guid tenantId, string category, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, user_id, tenant_id, category, entity_key, name, is_default, settings, created_at, updated_at
            FROM {SchemaConstants.UserPreferencesTable}
            WHERE user_id = @user_id AND tenant_id = @tenant_id AND category = @category
            ORDER BY entity_key, name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("category", category));

        var results = new List<UserPreference>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadPreference(reader));
        }
        return results;
    }

    private static UserPreference ReadPreference(NpgsqlDataReader reader)
    {
        return new UserPreference
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            TenantId = reader.GetGuid(2),
            Category = reader.GetString(3),
            EntityKey = reader.GetString(4),
            Name = reader.GetString(5),
            IsDefault = reader.GetBoolean(6),
            Settings = reader.GetString(7),
            CreatedAt = reader.GetDateTime(8),
            UpdatedAt = reader.GetDateTime(9),
        };
    }
}
