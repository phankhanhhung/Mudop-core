using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// WATERFALL: each plugin transforms the query context.
/// Output of plugin N becomes input of plugin N+1.
/// Used for SELECT query building.
///
/// Inspired by: Webpack SyncWaterfallHook.
/// </summary>
public interface IFeatureQueryFilter : IPlatformFeature
{
    /// <summary>
    /// Apply this feature's filter to the query context.
    /// Returns the (potentially modified) context for the next plugin.
    /// </summary>
    QueryFilterContext ApplyFilter(BmEntity entity, QueryFilterContext ctx);
}
