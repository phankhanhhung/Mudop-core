using BMMDL.Runtime.Plugins.Loading;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Plugin lifecycle status. Tracks whether a plugin is installed, enabled, or disabled.
/// </summary>
public enum PluginStatus
{
    /// <summary>Plugin is installed but not yet enabled.</summary>
    Installed,
    /// <summary>Plugin is active and contributing to the platform.</summary>
    Enabled,
    /// <summary>Plugin is installed but temporarily deactivated.</summary>
    Disabled
}

/// <summary>
/// Persisted state for a single plugin, stored in core.plugin_state.
/// </summary>
public record PluginStateRecord(
    string Name,
    int Version,
    PluginStatus Status,
    DateTimeOffset InstalledAt,
    DateTimeOffset? EnabledAt,
    Dictionary<string, object?> Settings
);

/// <summary>
/// Manages plugin lifecycle: install, enable, disable, uninstall, and settings.
/// Reads/writes core.plugin_state and core.plugin_migration_history.
/// Executes migrations via <see cref="IMigrationProvider"/> and calls lifecycle hooks.
/// Validates dependencies (can't disable plugin X if plugin Y depends on it).
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// Returns the persisted state for all known plugins.
    /// Plugins without a state record are "available" (not yet installed).
    /// </summary>
    Task<IReadOnlyList<PluginStateRecord>> GetAllPluginStatesAsync(CancellationToken ct);

    /// <summary>
    /// Returns the persisted state for a single plugin, or null if not installed.
    /// </summary>
    Task<PluginStateRecord?> GetPluginStateAsync(string name, CancellationToken ct);

    /// <summary>
    /// Installs a plugin: runs migrations and sets status to Installed.
    /// </summary>
    Task<PluginStateRecord> InstallPluginAsync(string name, CancellationToken ct);

    /// <summary>
    /// Enables an installed plugin: sets status to Enabled and calls OnEnabledAsync.
    /// </summary>
    Task<PluginStateRecord> EnablePluginAsync(string name, CancellationToken ct);

    /// <summary>
    /// Disables an enabled plugin: sets status to Disabled and calls OnDisabledAsync.
    /// Validates that no other enabled plugins depend on this one.
    /// </summary>
    Task<PluginStateRecord> DisablePluginAsync(string name, CancellationToken ct);

    /// <summary>
    /// Uninstalls a plugin: runs down-migrations and calls OnUninstalledAsync.
    /// </summary>
    Task UninstallPluginAsync(string name, CancellationToken ct);

    /// <summary>
    /// Updates the settings for a plugin. Calls OnSettingsChangedAsync if the plugin
    /// implements <see cref="ISettingsProvider"/>.
    /// </summary>
    Task<Dictionary<string, object?>> UpdateSettingsAsync(
        string name,
        Dictionary<string, object?> settings,
        CancellationToken ct);

    /// <summary>
    /// Returns whether the given plugin is currently enabled.
    /// Used for fast runtime checks without async DB access (may use cached state).
    /// </summary>
    bool IsPluginEnabled(string name);

    /// <summary>
    /// Async check whether a plugin is currently enabled, querying the database directly.
    /// Prefer this over <see cref="IsPluginEnabled"/> in async contexts (e.g., middleware, filters).
    /// </summary>
    Task<bool> IsPluginEnabledAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Compiles and installs BMMDL modules bundled with a plugin into the Registry.
    /// This is called automatically during plugin installation if modules have autoInstall=true.
    /// Can also be called manually for on-demand installation.
    /// </summary>
    /// <returns>Results for each module installation attempt.</returns>
    Task<IReadOnlyList<ModuleInstallResult>> InstallPluginModulesAsync(
        string pluginName,
        bool force = false,
        CancellationToken ct = default);

    // ── Dynamic loading ──────────────────────────────────────────

    /// <summary>
    /// Loads a plugin from a directory on disk. Discovers features and adds them to the registry.
    /// The plugin is NOT automatically installed — call <see cref="InstallPluginAsync"/> after loading.
    /// </summary>
    /// <returns>The plugin descriptor, or null if the directory has no valid plugin.</returns>
    PluginDescriptor? LoadPluginFromDirectory(string pluginDirectory);

    /// <summary>
    /// Loads a plugin from a <c>.zip</c> archive on disk.
    /// The zip is extracted to the plugins directory, then loaded normally.
    /// </summary>
    /// <returns>The plugin descriptor, or null if loading failed.</returns>
    PluginDescriptor? LoadPluginFromZip(string zipPath);

    /// <summary>
    /// Loads a plugin from a <c>.zip</c> archive provided as a <see cref="Stream"/>.
    /// Useful for HTTP file uploads.
    /// </summary>
    /// <returns>The plugin descriptor, or null if loading failed.</returns>
    PluginDescriptor? LoadPluginFromZipStream(Stream zipStream, string? originalFileName = null);

    /// <summary>
    /// Unloads a previously loaded external plugin by name.
    /// The plugin must be disabled and uninstalled first.
    /// Removes features from registry and releases the assembly load context.
    /// </summary>
    void UnloadPlugin(string pluginName);

    /// <summary>
    /// Scans the configured plugins directory for new plugins and loads them.
    /// Already-loaded plugins are skipped.
    /// </summary>
    /// <returns>Names of newly loaded plugins.</returns>
    IReadOnlyList<string> ScanPluginDirectory();

    /// <summary>
    /// Returns descriptors for all currently loaded external plugins.
    /// </summary>
    IReadOnlyDictionary<string, PluginDescriptor> GetLoadedExternalPlugins();
}
