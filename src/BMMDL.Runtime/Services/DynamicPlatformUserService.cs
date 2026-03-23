namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using Npgsql;

/// <summary>
/// Standard implementation of IPlatformUserService using DynamicSqlBuilder.
/// Uses the established patterns for entity queries with OData filter support.
/// </summary>
public class DynamicPlatformUserService : PlatformServiceBase, IPlatformUserService
{
    /// <summary>
    /// Whitelist of allowed OAuth provider ID column names.
    /// Only these columns may be used in GetUserByExternalIdAsync / LinkExternalProviderAsync.
    /// </summary>
    private static readonly HashSet<string> AllowedProviderIdColumns = new(StringComparer.Ordinal)
    {
        "google_id",
        "microsoft_id",
        "apple_id"
    };

    public DynamicPlatformUserService(
        IDynamicSqlBuilder sqlBuilder,
        IMetaModelCache cache,
        ITenantConnectionFactory connectionFactory)
        : base(sqlBuilder, cache, connectionFactory)
    {
    }

    public async Task<Dictionary<string, object?>?> GetUserByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity)
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        // Build base SELECT without filters, then add parameterized WHERE clause
        var options = new QueryOptions { Top = 1 };
        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entity, options);

        // Inject parameterized email + is_active filter into WHERE clause
        var extraParams = new List<NpgsqlParameter>(parameters)
        {
            new("p_email_filter", usernameOrEmail.ToLowerInvariant())
        };
        var emailFilter = $"{NamingConvention.QuoteIdentifier("is_active")} = true AND LOWER({NamingConvention.QuoteIdentifier("email")}) = @p_email_filter";
        if (sql.Contains("WHERE ", StringComparison.Ordinal))
        {
            sql = sql.Replace("WHERE ", $"WHERE {emailFilter} AND ", StringComparison.Ordinal);
        }
        else
        {
            var insertPoint = sql.IndexOf(" ORDER BY", StringComparison.OrdinalIgnoreCase);
            if (insertPoint < 0) insertPoint = sql.IndexOf(" LIMIT", StringComparison.OrdinalIgnoreCase);
            if (insertPoint < 0) insertPoint = sql.Length;
            sql = sql.Insert(insertPoint, $" WHERE {emailFilter}");
        }

        return await ExecuteSingleAsync(sql, extraParams, ct);
    }

    public async Task<Dictionary<string, object?>?> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entity, id: userId);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<bool> UsernameOrEmailExistsAsync(string username, string email, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity)
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        // Build base COUNT without filters, then add parameterized WHERE clause
        var options = new QueryOptions { Top = 1 };
        var (sql, parameters) = _sqlBuilder.BuildCountQuery(entity, options);

        // Inject parameterized email filter into WHERE clause
        var extraParams = new List<NpgsqlParameter>(parameters)
        {
            new("p_email_filter", email.ToLowerInvariant())
        };
        var emailFilter = $"LOWER({NamingConvention.QuoteIdentifier("email")}) = @p_email_filter";
        if (sql.Contains("WHERE ", StringComparison.Ordinal))
        {
            sql = sql.Replace("WHERE ", $"WHERE {emailFilter} AND ", StringComparison.Ordinal);
        }
        else
        {
            // No WHERE clause (e.g., @SystemScoped entity without tenant filter)
            // Insert WHERE before any GROUP BY, ORDER BY, or LIMIT
            var insertPoint = sql.IndexOf(" GROUP BY", StringComparison.OrdinalIgnoreCase);
            if (insertPoint < 0) insertPoint = sql.IndexOf(" ORDER BY", StringComparison.OrdinalIgnoreCase);
            if (insertPoint < 0) insertPoint = sql.IndexOf(" LIMIT", StringComparison.OrdinalIgnoreCase);
            if (insertPoint < 0) insertPoint = sql.Length;
            sql = sql.Insert(insertPoint, $" WHERE {emailFilter}");
        }

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, extraParams);

        var count = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(count) > 0;
    }

    public async Task<Dictionary<string, object?>?> CreateUserAsync(Dictionary<string, object?> userData, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        var (sql, parameters) = _sqlBuilder.BuildInsertQuery(entity, userData);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<List<string>> GetUserRolesAsync(Guid userId, CancellationToken ct = default)
    {
        // JOIN query - need raw SQL as DynamicSqlBuilder doesn't support JOINs
        string sql = $"""
            SELECT r.name
            FROM core.user_role ur
            JOIN core.role r ON r.id = ur.role_id
            WHERE ur.user_id = @p0
              AND (ur.expires_at IS NULL OR ur.expires_at > NOW())
            """;


        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("p0", userId));
        
        var roles = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            roles.Add(reader.GetString(0));
        }

        return roles;
    }

    public async Task UpdateLastLoginAsync(Guid userId, string ipAddress, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        var updateData = new Dictionary<string, object?>
        {
            ["LastLoginAt"] = DateTime.UtcNow,
            ["LastLoginIp"] = ipAddress,
            ["FailedLoginAttempts"] = 0
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entity, userId, updateData);
        
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, parameters);
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Dictionary<string, object?>>> GetTenantUsersAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Get Users in a tenant (Core.User, not Identity)
        var entity = _cache.GetEntity(PlatformEntityNames.User) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.User}' not found in cache");

        var options = new QueryOptions
        {
            OrderBy = "displayName asc"
        };

        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entity, options);
        
        return await ExecuteListAsync(sql, parameters, ct);
    }

    public async Task<Dictionary<string, object?>?> UpdateUserAsync(Guid userId, Dictionary<string, object?> updates, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        if (updates.Count == 0)
            return await GetUserByIdAsync(userId, ct);

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entity, userId, updates);
        
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    public async Task<bool> DeactivateUserAsync(Guid userId, CancellationToken ct = default)
    {
        var entity = _cache.GetEntity(PlatformEntityNames.Identity) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Identity}' not found in cache");

        var updateData = new Dictionary<string, object?>
        {
            ["IsActive"] = false
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entity, userId, updateData);
        
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, parameters);
        
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task AssignRoleAsync(Guid userId, string roleName, Guid assignedById, CancellationToken ct = default)
    {
        // First find the role by name using parameterized query
        var roleEntity = _cache.GetEntity(PlatformEntityNames.Role)
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.Role}' not found in cache");

        var roleOptions = new QueryOptions { Top = 1 };
        var (roleSql, roleParams) = _sqlBuilder.BuildSelectQuery(roleEntity, roleOptions);

        // Inject parameterized name filter into WHERE clause
        var roleExtraParams = new List<NpgsqlParameter>(roleParams)
        {
            new("p_role_name_filter", roleName.ToLowerInvariant())
        };
        roleSql = roleSql.Replace("WHERE ", $"WHERE LOWER({NamingConvention.QuoteIdentifier("name")}) = @p_role_name_filter AND ", StringComparison.Ordinal);

        var role = await ExecuteSingleAsync(roleSql, roleExtraParams, ct);
        
        if (role == null)
            throw new InvalidOperationException($"Role '{roleName}' not found");

        var roleId = (Guid)role["Id"]!;

        // Create role assignment
        var assignmentEntity = _cache.GetEntity(PlatformEntityNames.UserRole) 
            ?? throw new InvalidOperationException($"Entity '{PlatformEntityNames.UserRole}' not found in cache");

        var assignmentData = new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["UserId"] = userId,
            ["RoleId"] = roleId,
            ["AssignedById"] = assignedById,
            ["AssignedAt"] = DateTime.UtcNow
        };

        var (insertSql, insertParams) = _sqlBuilder.BuildInsertQuery(assignmentEntity, assignmentData);
        
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, insertSql, insertParams);
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveRoleAsync(Guid userId, string roleName, CancellationToken ct = default)
    {
        // Need JOIN - use raw SQL
        string sql = $"""
            DELETE FROM core.user_role ur
            USING core.role r
            WHERE ur.role_id = r.id
              AND ur.user_id = @user_id
              AND LOWER(r.name) = LOWER(@role_name)
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter("role_name", roleName));
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        // JOIN query - needs raw SQL
        string sql = $"""
            SELECT DISTINCT p.name
            FROM core.user_role ur
            JOIN core.role_permission rp ON rp.role_id = ur.role_id
            JOIN platform.permission p ON p.id = rp.permission_id
            WHERE ur.user_id = @p0
              AND p.is_active = true
              AND (ur.expires_at IS NULL OR ur.expires_at > NOW())
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("p0", userId));
        
        var permissions = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            permissions.Add(reader.GetString(0));
        }

        return permissions;
    }

    public async Task<List<string>> GetUserDirectPermissionsAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT sp.name FROM core.system_permission sp
            INNER JOIN core.systempermission_user j ON j.system_permission_id = sp.id
            WHERE j.user_id = @user_id AND j.tenant_id = @tenant_id
            ORDER BY sp.name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));

        var permissions = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            permissions.Add(reader.GetString(0));
        }

        return permissions;
    }

    public async Task AssignPermissionAsync(Guid userId, string permissionName, Guid tenantId, CancellationToken ct = default)
    {
        // Look up SystemPermission by name + tenant
        const string lookupSql = """
            SELECT id FROM core.system_permission
            WHERE LOWER(name) = LOWER(@perm_name) AND tenant_id = @tenant_id
            LIMIT 1
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var lookupCmd = new NpgsqlCommand(lookupSql, connection);
        lookupCmd.Parameters.Add(new NpgsqlParameter("perm_name", permissionName));
        lookupCmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));

        var permId = await lookupCmd.ExecuteScalarAsync(ct);
        if (permId == null)
            throw new InvalidOperationException($"SystemPermission '{permissionName}' not found for this tenant");

        var permissionId = (Guid)permId;

        // Insert into junction table with ON CONFLICT DO NOTHING
        const string insertSql = """
            INSERT INTO core.systempermission_user (user_id, system_permission_id, tenant_id)
            VALUES (@user_id, @perm_id, @tenant_id)
            ON CONFLICT DO NOTHING
            """;

        await using var insertCmd = new NpgsqlCommand(insertSql, connection);
        insertCmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        insertCmd.Parameters.Add(new NpgsqlParameter("perm_id", permissionId));
        insertCmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));

        await insertCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemovePermissionAsync(Guid userId, string permissionName, Guid tenantId, CancellationToken ct = default)
    {
        string sql = $"""
            DELETE FROM core.systempermission_user j
            USING core.system_permission sp
            WHERE j.system_permission_id = sp.id
              AND j.user_id = @user_id
              AND LOWER(sp.name) = LOWER(@perm_name)
              AND j.tenant_id = @tenant_id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter("perm_name", permissionName));
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Dictionary<string, object?>>> ListSystemPermissionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT id, name, description, is_system_role, is_default
            FROM core.system_permission
            WHERE tenant_id = @tenant_id
            ORDER BY name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter(SchemaConstants.TenantIdColumn, tenantId));

        var results = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new Dictionary<string, object?>
            {
                ["Id"] = reader.GetGuid(0).ToString(),
                ["Name"] = reader.GetString(1),
                ["Description"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["IsSystemRole"] = reader.IsDBNull(3) ? false : reader.GetBoolean(3),
                ["IsDefault"] = reader.IsDBNull(4) ? false : reader.GetBoolean(4)
            });
        }

        return results;
    }

    public async Task StoreRefreshTokenAsync(Guid userId, string tokenHash, string? deviceInfo, string? ipAddress, DateTime expiresAt, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {SchemaConstants.RefreshTokenTable} 
                (id, identity_id, token_hash, device_info, ip_address, expires_at, created_at)
            VALUES 
                (@id, @identity_id, @token_hash, @device_info, @ip_address, @expires_at, NOW())
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        
        cmd.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        cmd.Parameters.Add(new NpgsqlParameter("identity_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter("token_hash", tokenHash));
        cmd.Parameters.Add(new NpgsqlParameter("device_info", deviceInfo ?? (object)DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("ip_address", ipAddress ?? (object)DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("expires_at", expiresAt));
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ValidateRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT 1 FROM {SchemaConstants.RefreshTokenTable}
            WHERE token_hash = @token_hash
              AND expires_at > NOW()
              AND revoked_at IS NULL
            LIMIT 1
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("token_hash", tokenHash));
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    public async Task<Guid?> GetUserIdByRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT identity_id FROM {SchemaConstants.RefreshTokenTable}
            WHERE token_hash = @token_hash
              AND expires_at > NOW()
              AND revoked_at IS NULL
            LIMIT 1
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("token_hash", tokenHash));
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as Guid?;
    }

    public async Task RevokeRefreshTokenAsync(string tokenHash, string reason, CancellationToken ct = default)
    {
        string sql = $"""
            UPDATE {SchemaConstants.RefreshTokenTable}
            SET revoked_at = NOW(), revoked_reason = @reason
            WHERE token_hash = @token_hash
              AND revoked_at IS NULL
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("token_hash", tokenHash));
        cmd.Parameters.Add(new NpgsqlParameter("reason", reason));
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        string sql = $"""
            UPDATE {SchemaConstants.RefreshTokenTable}
            SET revoked_at = NOW(), revoked_reason = @reason
            WHERE identity_id = @identity_id
              AND revoked_at IS NULL
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("identity_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter("reason", reason));
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid> CreateRoleAsync(string roleName, string? description, Guid? tenantId, bool isSystemRole, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO core.role 
                (id, name, description, is_system_role, is_default, created_at)
            VALUES 
                (@id, @name, @description, @is_system, false, NOW())
            RETURNING id
            """;

        var roleId = Guid.NewGuid();
        
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        
        cmd.Parameters.Add(new NpgsqlParameter("id", roleId));
        cmd.Parameters.Add(new NpgsqlParameter("name", roleName));
        cmd.Parameters.Add(new NpgsqlParameter("description", description ?? (object)DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("is_system", isSystemRole));
        
        await cmd.ExecuteNonQueryAsync(ct);
        return roleId;
    }

    public async Task CreateTenantUserAsync(Guid identityId, Guid tenantId, string displayName, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO core.user (id, identity_id, tenant_id, display_name, status, created_at)
            VALUES (@id, @identityId, @tenantId, @displayName, 'active', NOW())
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        cmd.Parameters.Add(new NpgsqlParameter("identityId", identityId));
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("displayName", displayName));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Dictionary<string, object?>?> GetUserByExternalIdAsync(string providerIdField, string providerId, CancellationToken ct = default)
    {
        var columnName = NamingConvention.ToSnakeCase(providerIdField);
        if (!AllowedProviderIdColumns.Contains(columnName))
            throw new ArgumentException($"Invalid provider ID field '{providerIdField}'. Allowed fields: {string.Join(", ", AllowedProviderIdColumns)}", nameof(providerIdField));

        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        var sql = $"""
            SELECT id, email, display_name, google_id, microsoft_id, apple_id,
                   is_active, is_email_verified, created_at, updated_at
            FROM platform.identity
            WHERE {quotedColumn} = @provider_id
              AND is_active = true
            LIMIT 1
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("provider_id", providerId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return RowReader.ReadRow(reader);
    }

    public async Task<bool> LinkExternalProviderAsync(Guid userId, string providerIdField, string providerId, CancellationToken ct = default)
    {
        var columnName = NamingConvention.ToSnakeCase(providerIdField);
        if (!AllowedProviderIdColumns.Contains(columnName))
            throw new ArgumentException($"Invalid provider ID field '{providerIdField}'. Allowed fields: {string.Join(", ", AllowedProviderIdColumns)}", nameof(providerIdField));

        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        var sql = $"""
            UPDATE platform.identity
            SET {quotedColumn} = @provider_id, updated_at = NOW()
            WHERE id = @user_id
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("user_id", userId));
        cmd.Parameters.Add(new NpgsqlParameter("provider_id", providerId));

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }
}
