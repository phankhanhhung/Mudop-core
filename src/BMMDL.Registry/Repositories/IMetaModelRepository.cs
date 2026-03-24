using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Expressions;
using BMMDL.Registry.Entities.Normalized;

namespace BMMDL.Registry.Repositories;

/// <summary>
/// Abstraction over the meta-model persistence layer.
/// Consumers depend on this interface rather than on <see cref="EfCoreMetaModelRepository"/> directly,
/// enabling alternative storage backends (e.g., file-based, remote API, in-memory) via the plugin system.
///
/// <para>
/// The default implementation is <see cref="EfCoreMetaModelRepository"/> backed by PostgreSQL + EF Core.
/// Plugins can provide custom implementations through <c>IRegistryStorageProvider</c>.
/// </para>
///
/// <para>
/// Note: <c>LoadAllToCacheAsync</c> and <c>SaveAllFromCacheAsync</c> are intentionally excluded
/// from this interface because they depend on <c>MetaModelCache</c> (in BMMDL.Runtime) which would
/// create a circular dependency. These remain as concrete methods on <see cref="EfCoreMetaModelRepository"/>.
/// </para>
/// </summary>
public interface IMetaModelRepository
{
    // ============================================================
    // Model-Level Operations
    // ============================================================

    /// <summary>
    /// Load all model elements for this tenant into a BmModel.
    /// This is the primary read path used by MetaModelCacheManager and versioning workflows.
    /// </summary>
    Task<BmModel> LoadModelAsync(CancellationToken ct = default);

    // ============================================================
    // Entity Operations
    // ============================================================

    Task SaveEntityAsync(BmEntity entity, CancellationToken ct = default);
    Task SaveEntitiesAsync(IEnumerable<BmEntity> entities, CancellationToken ct = default);
    Task<EntityRecord?> GetEntityByNameAsync(string qualifiedName, CancellationToken ct = default);
    Task<IReadOnlyList<EntityRecord>> GetEntitiesAsync(string? ns = null, CancellationToken ct = default);

    // ============================================================
    // Service Operations
    // ============================================================

    Task SaveServiceAsync(BmService service, CancellationToken ct = default);
    Task SaveServicesAsync(IEnumerable<BmService> services, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceRecord>> GetServicesAsync(string? ns = null, CancellationToken ct = default);

    // ============================================================
    // Type Operations
    // ============================================================

    Task SaveTypeAsync(BmType type, CancellationToken ct = default);
    Task SaveTypesAsync(IEnumerable<BmType> types, CancellationToken ct = default);
    Task<IReadOnlyList<TypeRecord>> GetTypesAsync(string? ns = null, CancellationToken ct = default);

    // ============================================================
    // Enum Operations
    // ============================================================

    Task SaveEnumAsync(BmEnum enumType, CancellationToken ct = default);
    Task SaveEnumsAsync(IEnumerable<BmEnum> enums, CancellationToken ct = default);
    Task<IReadOnlyList<EnumRecord>> GetEnumsAsync(string? ns = null, CancellationToken ct = default);

    // ============================================================
    // Aspect Operations
    // ============================================================

    Task SaveAspectAsync(BmAspect aspect, CancellationToken ct = default);
    Task SaveAspectsAsync(IEnumerable<BmAspect> aspects, CancellationToken ct = default);
    Task<IReadOnlyList<AspectRecord>> GetAspectsAsync(string? ns = null, CancellationToken ct = default);

    // ============================================================
    // View Operations
    // ============================================================

    Task SaveViewAsync(BmView view, CancellationToken ct = default);
    Task SaveViewsAsync(IEnumerable<BmView> views, CancellationToken ct = default);

    // ============================================================
    // Rule Operations
    // ============================================================

    Task SaveRuleAsync(BmRule rule, CancellationToken ct = default);
    Task SaveRulesAsync(IEnumerable<BmRule> rules, CancellationToken ct = default);

    // ============================================================
    // Sequence Operations
    // ============================================================

    Task SaveSequenceAsync(BmSequence sequence, CancellationToken ct = default);
    Task SaveSequencesAsync(IEnumerable<BmSequence> sequences, CancellationToken ct = default);

    // ============================================================
    // Event Operations
    // ============================================================

    Task SaveEventAsync(BmEvent evt, CancellationToken ct = default);
    Task SaveEventsAsync(IEnumerable<BmEvent> events, CancellationToken ct = default);

    // ============================================================
    // Access Control Operations
    // ============================================================

    Task SaveAccessControlAsync(BmAccessControl ac, CancellationToken ct = default);
    Task SaveAccessControlsAsync(IEnumerable<BmAccessControl> accessControls, CancellationToken ct = default);

    // ============================================================
    // Migration Definition Operations
    // ============================================================

    Task SaveMigrationDefAsync(BmMigrationDef migration, CancellationToken ct = default);
    Task SaveMigrationDefsAsync(IEnumerable<BmMigrationDef> migrations, CancellationToken ct = default);

    // ============================================================
    // Stats
    // ============================================================

    Task<PersistenceStats> GetStatsAsync(CancellationToken ct = default);
}
