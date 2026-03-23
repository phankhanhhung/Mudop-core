namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Scoped per-request. Controls which feature filters are active.
/// Stack-based: supports nested Disable/Enable scopes.
///
/// Inspired by: ABP Framework IDataFilter&lt;T&gt;.
///
/// Usage:
///   using (_filterState.Disable("SoftDelete"))
///   {
///       // SoftDelete filter disabled within this scope
///       // TenantIsolation still active
///   }
/// </summary>
public interface IFeatureFilterState
{
    /// <summary>
    /// Whether the named feature's filter is currently active.
    /// </summary>
    bool IsEnabled(string featureName);

    /// <summary>
    /// Temporarily disable the named feature's filter.
    /// Dispose the returned handle to restore the previous state.
    /// </summary>
    IDisposable Disable(string featureName);

    /// <summary>
    /// Temporarily enable the named feature's filter.
    /// Dispose the returned handle to restore the previous state.
    /// </summary>
    IDisposable Enable(string featureName);
}
