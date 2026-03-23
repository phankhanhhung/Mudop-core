using System.Reflection;
using System.Runtime.Loader;

namespace BMMDL.Runtime.Plugins.Loading;

/// <summary>
/// Custom <see cref="AssemblyLoadContext"/> for plugin isolation.
/// Each external plugin gets its own context so that:
///   1. Plugin dependencies don't pollute the host.
///   2. Plugins can be unloaded (collectible = true).
///   3. Shared framework assemblies (e.g., BMMDL.Runtime) are resolved from the host.
/// </summary>
public sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    /// <summary>
    /// Resolves assemblies by looking in the plugin's own directory first.
    /// If not found there, falls back to the default (host) context — this lets
    /// plugins share BMMDL.Runtime, BMMDL.MetaModel, etc. without bundling them.
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try plugin-local resolution first
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    /// <summary>
    /// Resolves native libraries from the plugin directory.
    /// </summary>
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}
