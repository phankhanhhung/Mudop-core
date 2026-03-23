namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Defines configurable settings for this plugin.
/// Settings are stored in core.plugin_settings table.
/// Admin UI renders a settings form based on the schema.
/// </summary>
public interface ISettingsProvider : IPlatformFeature
{
    /// <summary>
    /// Returns the settings schema for this plugin.
    /// </summary>
    PluginSettingsSchema GetSettingsSchema();

    /// <summary>
    /// Called when settings change. Plugin can validate and react.
    /// </summary>
    Task OnSettingsChangedAsync(Dictionary<string, object?> newSettings, CancellationToken ct);
}
