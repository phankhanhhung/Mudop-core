using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BMMDL.Runtime;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Admin controller for runtime management operations.
/// Requires Admin role OR X-Admin-Key header for all operations.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminKeyPolicy")]
public class RuntimeAdminController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly ILogger<RuntimeAdminController> _logger;

    public RuntimeAdminController(
        MetaModelCacheManager cacheManager,
        ILogger<RuntimeAdminController> logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    /// <summary>
    /// Reload the MetaModel cache from database.
    /// Call this after bootstrapping or installing new modules.
    /// Requires Admin role.
    /// </summary>
    [HttpPost("reload-cache")]
    public async Task<IActionResult> ReloadCache()
    {
        _logger.LogInformation("Admin {User} reloading MetaModel cache...", User.Identity?.Name ?? "unknown");

        var cache = await _cacheManager.ReloadAsync();

        var tenantScopedCount = cache.Model.Entities.Count(e => e.TenantScoped);
        return Ok(new
        {
            Success = true,
            EntityCount = cache.Model.Entities.Count,
            TenantScopedCount = tenantScopedCount,
            ServiceCount = cache.Model.Services.Count,
            Message = "MetaModel cache reloaded successfully"
        });
    }

    /// <summary>
    /// Get current cache statistics.
    /// Requires Admin role.
    /// </summary>
    [HttpGet("cache-stats")]
    public async Task<IActionResult> GetCacheStats()
    {
        var cache = await _cacheManager.GetCacheAsync();

        return Ok(new
        {
            EntityCount = cache.Model.Entities.Count,
            ServiceCount = cache.Model.Services.Count,
            TypeCount = cache.Model.Types.Count,
            EnumCount = cache.Model.Enums.Count,
            Entities = cache.Model.Entities.Select(e => e.QualifiedName).ToList()
        });
    }
}
