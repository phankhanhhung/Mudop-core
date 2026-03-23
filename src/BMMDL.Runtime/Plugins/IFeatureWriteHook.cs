using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// TAP: all plugins fire, side-effects only.
/// Used for lifecycle events (before/after create/update/delete).
///
/// Inspired by: Hibernate PostInsertEventListener, SAP CAP before/after.
/// All methods have default no-op implementations so features only override what they need.
/// </summary>
public interface IFeatureWriteHook : IPlatformFeature
{
    Task OnBeforeInsertAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;

    Task OnAfterInsertAsync(BmEntity entity, Dictionary<string, object?> result,
        WriteContext ctx) => Task.CompletedTask;

    Task OnBeforeUpdateAsync(BmEntity entity, Dictionary<string, object?> oldData,
        Dictionary<string, object?> newData, WriteContext ctx) => Task.CompletedTask;

    Task OnAfterUpdateAsync(BmEntity entity, Dictionary<string, object?> result,
        WriteContext ctx) => Task.CompletedTask;

    Task OnBeforeDeleteAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;

    Task OnAfterDeleteAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;
}
