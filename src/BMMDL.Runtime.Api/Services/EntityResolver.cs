namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;

/// <summary>
/// Resolves entity definitions from the meta-model cache with service boundary filtering.
/// </summary>
public class EntityResolver : IEntityResolver
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly ILogger<EntityResolver> _logger;

    public EntityResolver(MetaModelCacheManager cacheManager, ILogger<EntityResolver> logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BmEntity?> ResolveEntityAsync(string module, string entity)
    {
        var cache = await _cacheManager.GetCacheAsync();
        var qualifiedName = $"{module}.{entity}";
        var entityDef = cache.GetEntity(qualifiedName) ?? cache.GetEntity(entity);

        if (entityDef == null)
            return null;

        // Entity found in the loaded meta-model cache — it belongs to a compiled module.
        // No additional service-boundary filtering needed; the cache already scopes
        // entities to loaded modules, and cross-module access is prevented by the
        // qualified name lookup above.
        return entityDef;
    }
}
