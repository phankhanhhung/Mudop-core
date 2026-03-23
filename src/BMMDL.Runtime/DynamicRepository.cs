using System.Data;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using Npgsql;

namespace BMMDL.Runtime;

/// <summary>
/// Dynamic repository for CRUD operations on meta-model entities.
/// Uses raw SQL based on entity definition from meta-model cache.
/// </summary>
/// <remarks>
/// <para>
/// Connection Management: Each method creates a new connection from the factory.
/// This is safe because Npgsql has built-in connection pooling - calling Dispose()
/// returns the connection to the pool rather than closing it.
/// </para>
/// <para>
/// Tenant Context: When tenantId is provided, each connection sets the PostgreSQL
/// session variable for Row-Level Security (RLS) policies.
/// </para>
/// <para>
/// Performance Note: For high-throughput scenarios, consider using a request-scoped
/// connection to avoid repeated tenant context setup per operation.
/// </para>
/// </remarks>
public class DynamicRepository
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly Guid? _tenantId;
    private readonly BmEntity _entity;
    private readonly MetaModelCache _cache;
    private readonly string _tableName;
    
    /// <summary>
    /// Create a new dynamic repository using the connection factory for proper connection pooling.
    /// </summary>
    /// <param name="connectionFactory">Connection factory for managing database connections.</param>
    /// <param name="entity">Entity definition from meta-model.</param>
    /// <param name="cache">Meta-model cache.</param>
    /// <param name="tenantId">Optional tenant ID for tenant-scoped operations.</param>
    public DynamicRepository(ITenantConnectionFactory connectionFactory, BmEntity entity, MetaModelCache cache, Guid? tenantId = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tenantId = tenantId;
        _tableName = $"{SchemaConstants.PlatformSchema}.{NamingConvention.ToSnakeCase(entity.Name)}";
    }
    
    /// <summary>
    /// Gets the entity definition.
    /// </summary>
    public BmEntity Entity => _entity;
    
    /// <summary>
    /// Gets the table name.
    /// </summary>
    public string TableName => _tableName;
    
    /// <summary>
    /// Create a new record.
    /// </summary>
    public async Task<Guid> CreateAsync(Dictionary<string, object?> data, CancellationToken ct = default)
    {
        // Ensure ID is set
        if (!data.ContainsKey("id"))
        {
            data["id"] = Guid.NewGuid();
        }
        
        var id = (Guid)data["id"]!;
        
        // Build INSERT statement
        var columns = new List<string>();
        var parameters = new List<string>();
        var values = new List<NpgsqlParameter>();
        
        int paramIndex = 0;
        foreach (var kvp in data)
        {
            var columnName = NamingConvention.ToSnakeCase(kvp.Key);
            columns.Add(columnName);
            parameters.Add($"@p{paramIndex}");
            values.Add(new NpgsqlParameter($"@p{paramIndex}", kvp.Value ?? DBNull.Value));
            paramIndex++;
        }
        
        var sql = $@"INSERT INTO {_tableName} ({string.Join(", ", columns)}) 
                     VALUES ({string.Join(", ", parameters)})";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(values.ToArray());
        
        await cmd.ExecuteNonQueryAsync(ct);
        
        return id;
    }
    
    /// <summary>
    /// Get a record by ID.
    /// </summary>
    public async Task<Dictionary<string, object?>?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tableName} WHERE id = @id";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        
        return RowReader.ReadRow(reader);
    }

    /// <summary>
    /// Get all records.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> GetAllAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tableName}";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        var results = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(RowReader.ReadRow(reader));
        }

        return results;
    }

    /// <summary>
    /// Query records with a WHERE clause.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string whereClause, 
        Dictionary<string, object>? parameters = null,
        CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_tableName} WHERE {whereClause}";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        
        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                cmd.Parameters.AddWithValue(kvp.Key, kvp.Value);
            }
        }
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        var results = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(RowReader.ReadRow(reader));
        }

        return results;
    }

    /// <summary>
    /// Update a record by ID.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, Dictionary<string, object?> data, CancellationToken ct = default)
    {
        if (data.Count == 0)
            return false;
            
        // Build UPDATE statement
        var setClauses = new List<string>();
        var values = new List<NpgsqlParameter>();
        
        int paramIndex = 0;
        foreach (var kvp in data)
        {
            if (kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
                continue; // Don't update ID
                
            var columnName = NamingConvention.ToSnakeCase(kvp.Key);
            setClauses.Add($"{columnName} = @p{paramIndex}");
            values.Add(new NpgsqlParameter($"@p{paramIndex}", kvp.Value ?? DBNull.Value));
            paramIndex++;
        }
        
        var sql = $"UPDATE {_tableName} SET {string.Join(", ", setClauses)} WHERE id = @id";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(values.ToArray());
        cmd.Parameters.AddWithValue("@id", id);
        
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }
    
    /// <summary>
    /// Delete a record by ID.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"DELETE FROM {_tableName} WHERE id = @id";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }
    
    /// <summary>
    /// Check if a record exists by ID.
    /// </summary>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"SELECT 1 FROM {_tableName} WHERE id = @id LIMIT 1";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }
    
    /// <summary>
    /// Count all records.
    /// </summary>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT COUNT(*) FROM {_tableName}";
        
        await using var conn = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        
        return Convert.ToInt64(result);
    }
    
}
