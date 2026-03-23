using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// WATERFALL: each plugin adds columns/values to INSERT.
/// Output of plugin N becomes input of plugin N+1.
/// </summary>
public interface IFeatureInsertContributor : IPlatformFeature
{
    /// <summary>
    /// Contribute additional columns, value placeholders, and parameters to an INSERT operation.
    /// Returns the (potentially modified) context for the next plugin.
    /// </summary>
    InsertContext ContributeInsert(BmEntity entity, InsertContext ctx);
}
