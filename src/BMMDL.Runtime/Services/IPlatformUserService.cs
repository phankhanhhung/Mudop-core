namespace BMMDL.Runtime.Services;

/// <summary>
/// Service for querying platform entities (users, tenants, roles).
/// Uses ParameterizedQueryExecutor for safe SQL execution.
/// </summary>
public interface IPlatformUserService
{
    Task<Dictionary<string, object?>?> GetUserByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> CreateUserAsync(Dictionary<string, object?> userData, CancellationToken ct = default);
    Task<bool> UsernameOrEmailExistsAsync(string username, string email, CancellationToken ct = default);
    Task<List<string>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);
    Task UpdateLastLoginAsync(Guid userId, string ipAddress, CancellationToken ct = default);
    
    // Tenant user management
    Task<List<Dictionary<string, object?>>> GetTenantUsersAsync(Guid tenantId, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> UpdateUserAsync(Guid userId, Dictionary<string, object?> updates, CancellationToken ct = default);
    Task<bool> DeactivateUserAsync(Guid userId, CancellationToken ct = default);
    Task AssignRoleAsync(Guid userId, string roleName, Guid assignedById, CancellationToken ct = default);
    Task RemoveRoleAsync(Guid userId, string roleName, CancellationToken ct = default);
    
    // Permission management (role-based, aggregated)
    Task<List<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);
    
    // Direct permission management (M:M User ↔ SystemPermission)
    Task<List<string>> GetUserDirectPermissionsAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task AssignPermissionAsync(Guid userId, string permissionName, Guid tenantId, CancellationToken ct = default);
    Task RemovePermissionAsync(Guid userId, string permissionName, Guid tenantId, CancellationToken ct = default);
    Task<List<Dictionary<string, object?>>> ListSystemPermissionsAsync(Guid tenantId, CancellationToken ct = default);
    
    // Refresh token management
    Task StoreRefreshTokenAsync(Guid userId, string tokenHash, string? deviceInfo, string? ipAddress, DateTime expiresAt, CancellationToken ct = default);
    Task<bool> ValidateRefreshTokenAsync(string tokenHash, CancellationToken ct = default);
    Task<Guid?> GetUserIdByRefreshTokenAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeRefreshTokenAsync(string tokenHash, string reason, CancellationToken ct = default);
    Task RevokeAllUserTokensAsync(Guid userId, string reason, CancellationToken ct = default);
    
    // Role and tenant management
    Task<Guid> CreateRoleAsync(string roleName, string? description, Guid? tenantId, bool isSystemRole, CancellationToken ct = default);
    Task CreateTenantUserAsync(Guid identityId, Guid tenantId, string displayName, CancellationToken ct = default);
    
    // OAuth/External provider support
    Task<Dictionary<string, object?>?> GetUserByExternalIdAsync(string providerIdField, string providerId, CancellationToken ct = default);
    Task<bool> LinkExternalProviderAsync(Guid userId, string providerIdField, string providerId, CancellationToken ct = default);
}
