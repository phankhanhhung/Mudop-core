using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BMMDL.Runtime.Services;

/// <summary>
/// Service for managing tenants and tenant membership.
/// </summary>
public class TenantService : ITenantService
{
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<TenantService> _logger;
    
    public TenantService(IQueryExecutor queryExecutor, ILogger<TenantService> logger)
    {
        _queryExecutor = queryExecutor;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<TenantDto> CreateTenantAsync(
        string name, 
        string code, 
        Guid creatorIdentityId, 
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating tenant {TenantName} ({TenantCode}) for identity {IdentityId}",
            name, code, creatorIdentityId);
        
        // Step 1: Create tenant in platform.tenant
        var tenantId = Guid.NewGuid();
        var createTenantSql = @"
            INSERT INTO platform.tenant (id, name, code, status, created_at)
            VALUES (@id, @name, @code, 'active', NOW())
            RETURNING id";
        
        var tenantParams = new[]
        {
            new NpgsqlParameter("id", tenantId),
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("code", code)
        };
        
        var createdTenantId = await _queryExecutor.ExecuteScalarAsync<Guid>(createTenantSql, tenantParams, ct);
        _logger.LogInformation("Tenant created: {TenantId}", createdTenantId);
        
        // Step 2: Ensure Admin role exists for this tenant
        var adminRoleId = await EnsureAdminRoleExistsAsync(tenantId, ct);
        _logger.LogInformation("Admin role ensured: {RoleId}", adminRoleId);
        
        // Step 3: Create User record (assigns identity to tenant)
        var userId = Guid.NewGuid();
        var createUserSql = @"
            INSERT INTO core.user (id, identity_id, tenant_id, display_name, status, created_at)
            VALUES (@id, @identityId, @tenantId, @displayName, 'active', NOW())
            RETURNING id";
        
        var userParams = new[]
        {
            new NpgsqlParameter("id", userId),
            new NpgsqlParameter("identityId", creatorIdentityId),
            new NpgsqlParameter("tenantId", tenantId),
            new NpgsqlParameter("displayName", "Admin")  // Default display name
        };
        
        var createdUserId = await _queryExecutor.ExecuteScalarAsync<Guid>(createUserSql, userParams, ct);
        _logger.LogInformation("User record created: {UserId}", createdUserId);
        
        // Step 4: Assign Admin role to user
        var assignRoleSql = @"
            INSERT INTO core.user_role (id, user_id, role_id, assigned_at)
            VALUES (@id, @userId, @roleId, NOW())";
        
        var roleParams = new[]
        {
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("userId", userId),
            new NpgsqlParameter("roleId", adminRoleId)
        };
        
        await _queryExecutor.ExecuteNonQueryAsync(assignRoleSql, roleParams, ct);
        _logger.LogInformation("Admin role assigned to user {UserId}", userId);
        
        _logger.LogInformation(
            "Tenant creation complete: {TenantId} with admin user {UserId}",
            tenantId, userId);
        
        return new TenantDto
        {
            Id = tenantId,
            Name = name,
            Code = code,
            UserRole = "Admin"
        };
    }
    
    /// <summary>
    /// Ensure Admin role exists for the tenant, create if not exists.
    /// </summary>
    private async Task<Guid> EnsureAdminRoleExistsAsync(Guid tenantId, CancellationToken ct)
    {
        // Check if Admin role exists for this tenant
        var checkSql = "SELECT id FROM core.role WHERE tenant_id = @tenantId AND name = 'Admin'";
        var checkParams = new[] { new NpgsqlParameter("tenantId", tenantId) };
        
        var existingRoleId = await _queryExecutor.ExecuteScalarAsync<Guid?>(checkSql, checkParams, ct);
        if (existingRoleId.HasValue)
        {
            _logger.LogDebug("Admin role already exists: {RoleId}", existingRoleId.Value);
            return existingRoleId.Value;
        }
        
        // Create Admin role
        var roleId = Guid.NewGuid();
        var createRoleSql = @"
            INSERT INTO core.role (id, tenant_id, name, description, is_system_role, is_default, created_at)
            VALUES (@id, @tenantId, 'Admin', 'Tenant Administrator', true, false, NOW())
            RETURNING id";
        
        var roleParams = new[]
        {
            new NpgsqlParameter("id", roleId),
            new NpgsqlParameter("tenantId", tenantId)
        };
        
        var createdRoleId = await _queryExecutor.ExecuteScalarAsync<Guid>(createRoleSql, roleParams, ct);
        _logger.LogInformation("Created Admin role for tenant {TenantId}: {RoleId}", tenantId, createdRoleId);
        
        return createdRoleId;
    }
}
