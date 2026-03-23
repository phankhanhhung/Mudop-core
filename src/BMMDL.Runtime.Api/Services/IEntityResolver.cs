namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;

/// <summary>
/// Resolves entity definitions from the meta-model cache with service boundary filtering.
/// Shared by EntityControllerBase (throws) and BatchController (returns null).
/// </summary>
public interface IEntityResolver
{
    /// <summary>
    /// Resolve an entity definition by module and entity name.
    /// Verifies the entity is exposed by at least one service (if services are defined).
    /// Returns null if the entity is not found.
    /// Throws <see cref="EntityNotExposedByServiceException"/> if the entity is not exposed.
    /// </summary>
    Task<BmEntity?> ResolveEntityAsync(string module, string entity);
}
