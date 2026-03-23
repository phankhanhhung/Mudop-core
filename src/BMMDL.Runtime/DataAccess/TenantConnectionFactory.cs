namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Utilities;
using Npgsql;

/// <summary>
/// Factory for creating tenant-aware database connections.
/// Implements connection pooling and tenant context setup for RLS.
/// </summary>
public class TenantConnectionFactory : ITenantConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Create a new tenant connection factory.
    /// </summary>
    /// <param name="connectionString">Base database connection string.</param>
    public TenantConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        // Ensure multiplexing is disabled to prevent NpgsqlOperationInProgressException
        // when multiple commands run on connections sharing the same physical multiplexed connection.
        if (!connectionString.Contains("Multiplexing", StringComparison.OrdinalIgnoreCase))
            connectionString += ";Multiplexing=false";

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public string ConnectionString => _connectionString;

    /// <inheritdoc />
    public async Task<NpgsqlConnection> GetConnectionAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        
        try
        {
            await connection.OpenAsync(ct);
            
            if (tenantId.HasValue)
            {
                await SetTenantContextAsync(connection, tenantId.Value, ct);
            }
            
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetTenantContextAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken ct = default)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to set tenant context.");
        }

        // Set PostgreSQL session variable for Row-Level Security
        // This is used by RLS policies: current_setting('app.current_tenant_id')::uuid
        // Using set_config() function with parameterized value for SQL injection safety
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tenantId, false)",
            connection);
        cmd.Parameters.AddWithValue("@tenantId", tenantId.ToString("D"));
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public string GetSchemaForNamespace(string moduleNamespace)
    {
        if (string.IsNullOrWhiteSpace(moduleNamespace))
            return "public";
            
        return NamingConvention.GetSchemaName(moduleNamespace);
    }
}
