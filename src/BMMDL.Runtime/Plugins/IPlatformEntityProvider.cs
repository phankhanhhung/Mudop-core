using BMMDL.MetaModel.Structure;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Provides platform table definitions (e.g., platform.tenant, core.audit_entry).
/// Tables are created during plugin installation via <see cref="IMigrationProvider"/>.
/// Entity definitions are registered into MetaModelCache for runtime CRUD.
/// </summary>
public interface IPlatformEntityProvider : IPlatformFeature
{
    /// <summary>
    /// Returns BmEntity definitions for platform tables this plugin needs.
    /// These are NOT user-defined entities — they are platform infrastructure.
    /// </summary>
    IReadOnlyList<BmEntity> GetPlatformEntities();
}
