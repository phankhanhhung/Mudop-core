namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Aggregated plugin manifest consumed by the frontend admin UI.
/// Served at GET /api/plugins/manifest.
/// </summary>
public class PluginManifest
{
    public List<PluginManifestEntry> Plugins { get; init; } = [];
}

/// <summary>
/// Manifest entry for a single plugin, including its current status,
/// capabilities, menu items, pages, and settings.
/// </summary>
public class PluginManifestEntry
{
    /// <summary>Plugin unique name (e.g., "MultiTenancy").</summary>
    public required string Name { get; init; }

    /// <summary>Current status: "available" | "installed" | "enabled" | "disabled".</summary>
    public required string Status { get; init; }

    /// <summary>Names of plugins this one depends on.</summary>
    public List<string> DependsOn { get; init; } = [];

    /// <summary>Capability interface names implemented by this plugin.</summary>
    public List<string> Capabilities { get; init; } = [];

    /// <summary>Sidebar menu items (only populated when enabled).</summary>
    public List<PluginMenuItem> MenuItems { get; init; } = [];

    /// <summary>Admin UI page definitions (only populated when enabled).</summary>
    public List<PluginPageDefinition> Pages { get; init; } = [];

    /// <summary>Whether this plugin is currently in global mode (applies to all entities).</summary>
    public bool IsGloballyActive { get; init; }

    /// <summary>Settings schema and current values (null if plugin has no settings).</summary>
    public PluginManifestSettings? Settings { get; init; }

    /// <summary>BMMDL modules bundled with this plugin (if any).</summary>
    public List<PluginBmmdlModuleInfo> BmmdlModules { get; init; } = [];
}

/// <summary>
/// Plugin settings section in the manifest: the schema for rendering the form
/// and the current persisted values.
/// </summary>
public class PluginManifestSettings
{
    /// <summary>Settings schema defining available configuration options.</summary>
    public required PluginSettingsSchema Schema { get; init; }

    /// <summary>Current persisted setting values.</summary>
    public Dictionary<string, object?> Values { get; init; } = new();
}

/// <summary>
/// Information about a BMMDL module bundled with a plugin.
/// Included in the manifest for the frontend to display.
/// </summary>
public class PluginBmmdlModuleInfo
{
    /// <summary>Module name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it will be auto-installed with the plugin.</summary>
    public bool AutoInstall { get; init; } = true;

    /// <summary>Whether database tables will be initialized.</summary>
    public bool InitSchema { get; init; } = true;
}
