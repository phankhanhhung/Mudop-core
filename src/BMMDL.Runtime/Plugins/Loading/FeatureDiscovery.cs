using System.Reflection;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Plugins.Loading;

/// <summary>
/// Discovers <see cref="IPlatformFeature"/> implementations from assemblies via reflection.
/// Used by both the runtime (DI startup) and the compiler (standalone CLI) to find features
/// without hardcoding a list.
///
/// This makes built-in features (in BMMDL.Runtime.dll) equal to external plugins —
/// both are discovered the same way.
/// </summary>
public static class FeatureDiscovery
{
    /// <summary>
    /// Discovers all concrete <see cref="IPlatformFeature"/> implementations in the given assembly.
    /// Each type must have a public parameterless constructor.
    /// </summary>
    public static IReadOnlyList<IPlatformFeature> DiscoverFeatures(
        Assembly assembly,
        ILogger? logger = null)
    {
        var features = new List<IPlatformFeature>();
        var featureType = typeof(IPlatformFeature);

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger?.LogWarning(ex,
                "Some types in assembly '{Assembly}' could not be loaded",
                assembly.GetName().Name);
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (!featureType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                continue;

            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor is null)
            {
                logger?.LogDebug(
                    "Type {Type} implements IPlatformFeature but has no parameterless constructor — skipped",
                    type.FullName);
                continue;
            }

            try
            {
                var instance = (IPlatformFeature)Activator.CreateInstance(type)!;
                features.Add(instance);
                logger?.LogDebug("Discovered feature '{Feature}' in {Assembly}",
                    instance.Name, assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to instantiate {Type}", type.FullName);
            }
        }

        return features;
    }

    /// <summary>
    /// Discovers all <see cref="IPlatformFeature"/> implementations in the assembly
    /// containing <see cref="IPlatformFeature"/> itself (BMMDL.Runtime.dll).
    /// This is the standard way to find built-in platform features.
    /// </summary>
    public static IReadOnlyList<IPlatformFeature> DiscoverBuiltInFeatures(ILogger? logger = null)
    {
        return DiscoverFeatures(typeof(IPlatformFeature).Assembly, logger);
    }

    /// <summary>
    /// Creates a <see cref="PlatformFeatureRegistry"/> from all features discovered
    /// in the given assemblies.
    /// </summary>
    public static PlatformFeatureRegistry CreateRegistry(
        IEnumerable<Assembly>? assemblies = null,
        ILogger? logger = null)
    {
        var allFeatures = new List<IPlatformFeature>();

        // Always include built-in features from BMMDL.Runtime
        allFeatures.AddRange(DiscoverBuiltInFeatures(logger));

        // Add features from additional assemblies
        if (assemblies is not null)
        {
            var runtimeAssembly = typeof(IPlatformFeature).Assembly;
            foreach (var assembly in assemblies)
            {
                if (assembly == runtimeAssembly) continue; // already scanned
                allFeatures.AddRange(DiscoverFeatures(assembly, logger));
            }
        }

        return new PlatformFeatureRegistry(allFeatures);
    }
}
