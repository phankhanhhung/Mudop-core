namespace BMMDL.Runtime.Services;

/// <summary>
/// Service for querying platform tenant entities.
/// </summary>
public interface IPlatformTenantService
{
    Task<Dictionary<string, object?>?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> CreateTenantAsync(Dictionary<string, object?> tenantData, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> UpdateTenantAsync(Guid tenantId, Dictionary<string, object?> updates, CancellationToken ct = default);
    Task<bool> TenantCodeExistsAsync(string code, CancellationToken ct = default);
    Task<List<Dictionary<string, object?>>> GetUserTenantsAsync(Guid userId, CancellationToken ct = default);
}
