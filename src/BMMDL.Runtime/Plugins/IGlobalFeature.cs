using BMMDL.MetaModel.Structure;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Interface for platform features that apply to ALL entities when enabled.
/// Global activation is automatic — when the plugin is enabled and the feature
/// implements this interface, it applies to every entity by default.
///
/// <para>
/// Use case: When the MultiTenancy plugin is enabled, every entity automatically
/// receives a <c>tenant_id</c> column, RLS policy, and query filters. Entities
/// must use <c>@SystemScoped</c> annotation to opt out of tenant isolation.
/// </para>
///
/// <para>
/// Features implementing this interface can exclude specific entities via
/// <see cref="ShouldExcludeFromGlobal"/>. For example, MultiTenancy excludes:
/// - Child entities in table-per-type inheritance (inherit tenant_id from parent)
/// - Entities annotated with <c>@SystemScoped</c>, <c>@System</c>, or <c>@Global</c>
/// </para>
///
/// <para>
/// Global activation is managed by <see cref="PlatformFeatureRegistry.ActivateGlobalFeature"/>
/// and triggered automatically by <see cref="PluginManager"/> when the plugin is enabled.
/// </para>
/// </summary>
public interface IGlobalFeature
{
    /// <summary>
    /// Determines whether a specific entity should be excluded from global feature application.
    /// Called only when the feature is globally active and <see cref="IPlatformFeature.AppliesTo"/>
    /// returned false for this entity.
    ///
    /// <para>
    /// Return <c>true</c> to exclude this entity from global application.
    /// Return <c>false</c> to include it (the feature will be applied).
    /// </para>
    ///
    /// <para>
    /// Example: MultiTenancy returns <c>true</c> for:
    /// - Child entities in inheritance hierarchies (inherit tenant_id from parent)
    /// - Entities annotated with <c>@SystemScoped</c> (system-wide reference data)
    /// </para>
    /// </summary>
    bool ShouldExcludeFromGlobal(BmEntity entity) => false;
}
