using BMMDL.Runtime.Models;

namespace BMMDL.Runtime.Services;

/// <summary>
/// Service for managing tenants and tenant membership.
/// </summary>
public interface ITenantService
{
    /// <summary>
    /// Create a new tenant and automatically assign the creator as admin.
    /// </summary>
    /// <param name="name">Tenant name</param>
    /// <param name="code">Tenant code (unique identifier)</param>
    /// <param name="creatorIdentityId">Identity ID of the user creating the tenant</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created tenant information including user's role</returns>
    Task<TenantDto> CreateTenantAsync(
        string name, 
        string code, 
        Guid creatorIdentityId, 
        CancellationToken ct = default);
}

/// <summary>
/// DTO for tenant information.
/// </summary>
public record TenantDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string UserRole { get; init; } = "";
}
