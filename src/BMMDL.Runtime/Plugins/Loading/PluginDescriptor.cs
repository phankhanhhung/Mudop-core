using System.Reflection;

namespace BMMDL.Runtime.Plugins.Loading;

/// <summary>
/// Describes a loaded external plugin: its manifest, discovered features,
/// the <see cref="PluginAssemblyLoadContext"/> used for isolation, and the loaded assembly.
///
/// Tracked by <see cref="PluginDirectoryLoader"/> for the plugin's entire lifetime.
/// Disposed when the plugin is unloaded (triggers GC of the AssemblyLoadContext).
/// </summary>
public sealed class PluginDescriptor : IDisposable
{
    /// <summary>
    /// The deserialized plugin.json manifest.
    /// </summary>
    public required PluginManifestFile Manifest { get; init; }

    /// <summary>
    /// Absolute path to the plugin directory (e.g., /app/plugins/MyPlugin/).
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Absolute path to the entry-point assembly DLL.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// The custom <see cref="PluginAssemblyLoadContext"/> used to load this plugin.
    /// Collectible — unloading it releases the assembly and all its types.
    /// </summary>
    public required PluginAssemblyLoadContext LoadContext { get; init; }

    /// <summary>
    /// The loaded entry-point assembly.
    /// </summary>
    public required Assembly Assembly { get; init; }

    /// <summary>
    /// All <see cref="IPlatformFeature"/> instances discovered in the plugin assembly.
    /// Created via parameterless constructors.
    /// </summary>
    public required IReadOnlyList<IPlatformFeature> Features { get; init; }

    /// <summary>
    /// When the plugin was loaded.
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Unloads the plugin assembly by unloading its <see cref="PluginAssemblyLoadContext"/>.
    /// After disposal, all types from this plugin become unavailable.
    /// The actual unload happens on next GC cycle (collectible ALC behavior).
    /// </summary>
    public void Dispose()
    {
        LoadContext.Unload();
    }
}
