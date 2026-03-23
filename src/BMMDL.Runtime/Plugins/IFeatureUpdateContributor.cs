using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// WATERFALL: each plugin adds SET clauses to UPDATE.
/// Output of plugin N becomes input of plugin N+1.
/// </summary>
public interface IFeatureUpdateContributor : IPlatformFeature
{
    /// <summary>
    /// Contribute additional SET clauses and parameters to an UPDATE operation.
    /// Returns the (potentially modified) context for the next plugin.
    /// </summary>
    UpdateContext ContributeUpdate(BmEntity entity, UpdateContext ctx);
}
