using System.Text.Json.Serialization;

namespace BMMDL.Runtime.Plugins.Loading;

/// <summary>
/// Deserialization model for <c>plugin.json</c> manifest files found in each plugin subdirectory.
///
/// Example plugin.json:
/// <code>
/// {
///   "name": "CustomAuditTrail",
///   "version": "1.0.0",
///   "description": "Detailed audit trail with diff tracking",
///   "author": "Plugin Team",
///   "entryAssembly": "CustomAuditTrail.dll",
///   "dependencies": ["AuditField"]
/// }
/// </code>
/// </summary>
public sealed class PluginManifestFile
{
    /// <summary>
    /// Unique plugin name. Must match <see cref="IPlatformFeature.Name"/> of the main feature.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Semantic version string, e.g., "1.0.0".
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Human-readable description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Plugin author name or organization.
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>
    /// Name of the entry-point DLL (relative to the plugin directory).
    /// If omitted, the loader looks for <c>{Name}.dll</c>.
    /// </summary>
    [JsonPropertyName("entryAssembly")]
    public string? EntryAssembly { get; init; }

    /// <summary>
    /// Names of other plugins this plugin depends on.
    /// Must be available (built-in or loaded) before this plugin can load.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; init; } = [];

    /// <summary>
    /// Whether the plugin should be loaded automatically on startup.
    /// Defaults to true. Set to false to require manual loading via API.
    /// </summary>
    [JsonPropertyName("autoLoad")]
    public bool AutoLoad { get; init; } = true;

    /// <summary>
    /// BMMDL module definitions bundled with this plugin.
    /// Each module will be compiled and installed into the Registry when the plugin is installed.
    /// </summary>
    [JsonPropertyName("bmmdlModules")]
    public List<BmmdlModuleEntry> BmmdlModules { get; init; } = [];
}

/// <summary>
/// Entry describing a BMMDL module bundled with a plugin (in plugin.json).
/// </summary>
public sealed class BmmdlModuleEntry
{
    /// <summary>Module name for identification.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Path to the BMMDL source file relative to the plugin directory.
    /// The file content is read at installation time.
    /// </summary>
    [JsonPropertyName("sourceFile")]
    public required string SourceFile { get; init; }

    /// <summary>Whether to initialize database tables after compilation.</summary>
    [JsonPropertyName("initSchema")]
    public bool InitSchema { get; init; } = true;

    /// <summary>
    /// Whether to automatically install this module when the plugin is installed.
    /// If false, modules must be installed manually via the API.
    /// </summary>
    [JsonPropertyName("autoInstall")]
    public bool AutoInstall { get; init; } = true;
}
