namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Provides admin UI page definitions consumed by the Vue frontend.
/// The backend serves these as a manifest; the frontend renders them.
/// </summary>
public interface IAdminPageProvider : IPlatformFeature
{
    /// <summary>
    /// Returns page definitions for the admin UI.
    /// </summary>
    IReadOnlyList<PluginPageDefinition> GetPages();
}
