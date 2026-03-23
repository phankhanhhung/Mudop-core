namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Wraps <see cref="PluginManager"/> for plugin settings CRUD operations.
/// Provides a focused API for reading, updating, and introspecting plugin settings.
/// </summary>
public sealed class PluginSettingsService
{
    private readonly PluginManager _pluginManager;
    private readonly PlatformFeatureRegistry _registry;

    public PluginSettingsService(PluginManager pluginManager, PlatformFeatureRegistry registry)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Returns the current settings for a plugin.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetSettingsAsync(
        string pluginName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        var state = await _pluginManager.GetPluginStateAsync(pluginName, ct)
            ?? throw new InvalidOperationException($"Plugin '{pluginName}' is not installed");

        return state.Settings;
    }

    /// <summary>
    /// Updates the settings for a plugin. Validates via <see cref="ISettingsProvider"/>
    /// if the plugin implements it, then persists the new values.
    /// </summary>
    public async Task<Dictionary<string, object?>> UpdateSettingsAsync(
        string pluginName,
        Dictionary<string, object?> settings,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(settings);

        var updatedState = await _pluginManager.UpdateSettingsAsync(pluginName, settings, ct);
        return updatedState.Settings;
    }

    /// <summary>
    /// Returns the settings schema for a plugin, or null if the plugin
    /// does not implement <see cref="ISettingsProvider"/>.
    /// </summary>
    public PluginSettingsSchema? GetSettingsSchemaAsync(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        var provider = FindSettingsProvider(pluginName);
        return provider?.GetSettingsSchema();
    }

    /// <summary>
    /// Finds the <see cref="ISettingsProvider"/> for a plugin.
    /// </summary>
    private ISettingsProvider? FindSettingsProvider(string pluginName)
    {
        return _registry.GetSettingsProvider(pluginName);
    }
}
