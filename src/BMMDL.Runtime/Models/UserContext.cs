namespace BMMDL.Runtime.Models;

/// <summary>
/// Represents the authenticated user context for the current request.
/// Contains user identity, tenant, roles, and permissions.
/// </summary>
public record UserContext(
    Guid UserId,
    string Username,
    string Email,
    Guid TenantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> AllowedTenants
)
{
    /// <summary>
    /// System user context for bootstrap/admin operations.
    /// </summary>
    public static UserContext System => new(
        UserId: Guid.Empty,
        Username: "system",
        Email: "system@bmmdl.local",
        TenantId: Guid.Empty,
        Roles: new[] { "SystemAdmin" },
        Permissions: Array.Empty<string>(),
        AllowedTenants: Array.Empty<Guid>()
    );

    /// <summary>
    /// Check if user has a specific role.
    /// </summary>
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Check if user has a specific permission.
    /// </summary>
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Check if this is the system context.
    /// </summary>
    public bool IsSystem => UserId == Guid.Empty && Username == "system";

    /// <summary>
    /// Check if user is a tenant admin.
    /// </summary>
    public bool IsTenantAdmin => HasRole("TenantAdmin") || HasRole("SystemAdmin");

    /// <summary>
    /// Check if user can access a specific tenant.
    /// </summary>
    public bool CanAccessTenant(Guid tenantId) =>
        TenantId == tenantId ||
        AllowedTenants.Contains(tenantId) ||
        HasRole("SystemAdmin");
}
