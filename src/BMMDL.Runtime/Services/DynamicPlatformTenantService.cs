namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using Npgsql;

/// <summary>
/// Standard implementation of IPlatformTenantService using DynamicSqlBuilder.
/// Uses the established patterns for entity queries with OData filter support.
/// </summary>
public class DynamicPlatformTenantService : PlatformServiceBase, IPlatformTenantService
{
    public DynamicPlatformTenantService(
        IDynamicSqlBuilder sqlBuilder,
        IMetaModelCache cache,
        ITenantConnectionFactory connectionFactory)
        : base(sqlBuilder, cache, connectionFactory)
    {
    }

    public async Task<Dictionary<string, object?>?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Tenant) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Tenant}' not found in cache");

        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entity, id: tenantId);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<bool> TenantCodeExistsAsync(string code, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Tenant) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Tenant}' not found in cache");

        // Build base COUNT without filters, then add parameterized WHERE clause
        var options = new QueryOptions { Top = 1 };
        var (sql, parameters) = _sqlBuilder.BuildCountQuery(entity, options);

        // Inject parameterized code filter into WHERE clause
        var extraParams = new List<NpgsqlParameter>(parameters)
        {
            new("p_code_filter", code.ToLowerInvariant())
        };
        sql = sql.Replace("WHERE ", $"WHERE LOWER({NamingConvention.QuoteIdentifier("code")}) = @p_code_filter AND ", StringComparison.Ordinal);
        if (!sql.Contains("WHERE"))
            sql += $" WHERE LOWER({NamingConvention.QuoteIdentifier("code")}) = @p_code_filter";

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, extraParams);
        
        var count = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(count) > 0;
    }

    public async Task<Dictionary<string, object?>?> CreateTenantAsync(Dictionary<string, object?> tenantData, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Tenant) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Tenant}' not found in cache");

        var (sql, parameters) = _sqlBuilder.BuildInsertQuery(entity, tenantData);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<Dictionary<string, object?>?> UpdateTenantAsync(Guid tenantId, Dictionary<string, object?> updates, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Tenant) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Tenant}' not found in cache");

        if (updates.Count == 0)
            return await GetTenantByIdAsync(tenantId, ct);

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entity, tenantId, updates);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<List<Dictionary<string, object?>>> GetUserTenantsAsync(Guid userId, CancellationToken ct = default)
    {
        // JOIN query - get tenants where Identity is a member
        const string sql = """
            SELECT DISTINCT t.id, t.code, t.name, t.is_active, t.subscription_tier
            FROM platform.tenant t
            JOIN core.user u ON u.tenant_id = t.id
            WHERE u.identity_id = @p0 AND t.is_active = true
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("p0", userId));
        
        var results = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(RowReader.ReadRow(reader));
        }

        return results;
    }
}
