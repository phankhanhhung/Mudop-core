using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// BAIL: first plugin returning non-null wins, rest skipped.
/// Used for DELETE behavior selection (SoftDelete vs TemporalClose vs HardDelete).
///
/// Inspired by: Webpack SyncBailHook.
/// </summary>
public interface IFeatureDeleteStrategy : IPlatformFeature
{
    /// <summary>
    /// Return a delete operation to override the default hard DELETE,
    /// or null to defer to the next plugin.
    /// </summary>
    DeleteOperation? GetDeleteOperation(BmEntity entity, DeleteContext ctx);
}
