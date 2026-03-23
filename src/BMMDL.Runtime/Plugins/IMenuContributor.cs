namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Contributes items to the admin sidebar menu.
/// Menu items are aggregated from all enabled plugins and served via the plugin manifest.
/// </summary>
public interface IMenuContributor : IPlatformFeature
{
    /// <summary>
    /// Returns menu items to add to the admin sidebar.
    /// </summary>
    IReadOnlyList<PluginMenuItem> GetMenuItems();
}
