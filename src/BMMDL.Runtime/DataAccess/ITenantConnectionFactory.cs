namespace BMMDL.Runtime.DataAccess;

using Npgsql;

/// <summary>
/// Factory for creating tenant-aware database connections.
/// Handles connection string management and tenant context setup.
/// </summary>
public interface ITenantConnectionFactory
{
    /// <summary>
    /// Get a database connection, optionally configured for a specific tenant.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID to set context for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open database connection.</returns>
    /// <remarks>
    /// If tenantId is provided, the connection will have the tenant context set via
    /// PostgreSQL session variable (SET app.current_tenant_id = 'uuid').
    /// Caller is responsible for disposing the connection.
    /// </remarks>
    Task<NpgsqlConnection> GetConnectionAsync(Guid? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// Set the tenant context on an existing connection.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="tenantId">Tenant ID to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// This sets the PostgreSQL session variable used by Row-Level Security policies:
    /// SET app.current_tenant_id = 'tenant-uuid'
    /// </remarks>
    Task SetTenantContextAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Get the database schema name for a module namespace.
    /// </summary>
    /// <param name="moduleNamespace">Module namespace (e.g., "Platform", "SCM").</param>
    /// <returns>PostgreSQL schema name (e.g., "platform", "scm").</returns>
    string GetSchemaForNamespace(string moduleNamespace);

    /// <summary>
    /// Get the base connection string (without tenant context).
    /// </summary>
    string ConnectionString { get; }
}
