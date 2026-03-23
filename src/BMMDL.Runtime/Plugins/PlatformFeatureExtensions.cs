using System.Reflection;
using System.Text.Json;
using BMMDL.Runtime.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// DI registration helpers for the platform feature system.
/// </summary>
public static class PlatformFeatureExtensions
{
    /// <summary>
    /// Registers all platform features, the <see cref="PlatformFeatureRegistry"/> (singleton),
    /// and <see cref="IFeatureFilterState"/> (scoped per-request).
    ///
    /// When <paramref name="bootstrapOptions"/> is provided, features listed in
    /// <see cref="PluginBootstrapOptions.Exclude"/> (and their transitive dependents) will NOT
    /// have their <see cref="IServiceContributor.RegisterServices"/> called.
    ///
    /// Usage:
    /// <code>
    /// // Auto-discover all features from BMMDL.Runtime assembly:
    /// services.AddPlatformFeatures(b => b.AddBuiltIn());
    ///
    /// // With exclusion-aware service registration:
    /// services.AddPlatformFeatures(b => b.AddBuiltIn(), bootstrapOptions);
    /// </code>
    /// </summary>
    public static IServiceCollection AddPlatformFeatures(
        this IServiceCollection services,
        Action<PlatformFeatureBuilder> configure,
        PluginBootstrapOptions? bootstrapOptions = null)
    {
        var builder = new PlatformFeatureBuilder();
        configure(builder);

        var registry = new PlatformFeatureRegistry(builder.Features);
        services.AddSingleton(registry);
        services.AddSingleton<IReadOnlyList<IPlatformFeature>>(builder.Features.ToList());

        services.AddScoped<IFeatureFilterState>(sp =>
            new FeatureFilterState(registry.AllFeatureNames));

        // Register DI services for IServiceContributor features, respecting exclusions
        var excluded = BuildServiceExclusionSet(
            registry.AllFeatures, bootstrapOptions?.Exclude ?? []);

        foreach (var feature in registry.AllFeatures)
        {
            if (feature is IServiceContributor contributor && !excluded.Contains(feature.Name))
            {
                contributor.RegisterServices(services);
            }
        }

        return services;
    }

    /// <summary>
    /// Adds dynamic plugin loading from a directory. Registers:
    /// <list type="bullet">
    ///   <item><see cref="PluginDirectoryLoader"/> — singleton, performs initial scan</item>
    ///   <item><see cref="IPluginManager"/> → <see cref="PluginManager"/> — singleton, lifecycle management</item>
    ///   <item><see cref="PluginManifestService"/> — singleton, frontend manifest aggregation</item>
    /// </list>
    ///
    /// Must be called AFTER <see cref="AddPlatformFeatures"/>.
    ///
    /// Before registering the runtime loader, performs a pre-scan of the plugins directory
    /// to discover <see cref="IServiceContributor"/> implementations in external plugin DLLs
    /// and register their services BEFORE <c>builder.Build()</c>. This ensures external plugin
    /// services are available in the DI container.
    ///
    /// Plugins directory supports both subdirectories and <c>.zip</c> archives.
    /// Zip files are auto-extracted during scan.
    /// </summary>
    public static IServiceCollection AddDynamicPluginLoading(
        this IServiceCollection services,
        string pluginsDirectory,
        bool watchDirectory = false,
        PluginBootstrapOptions? bootstrapOptions = null)
    {
        // ── Pre-scan: register IServiceContributor services from external plugins ──
        // This runs BEFORE builder.Build() so external plugin services are in the DI container.
        var preLoadedPluginNames = PreScanExternalPlugins(services, pluginsDirectory, bootstrapOptions);

        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<PlatformFeatureRegistry>();
            var builtInFeatures = sp.GetRequiredService<IReadOnlyList<IPlatformFeature>>();
            var logger = sp.GetRequiredService<ILogger<PluginDirectoryLoader>>();

            var loader = new PluginDirectoryLoader(
                pluginsDirectory, registry, builtInFeatures, logger, preLoadedPluginNames);

            // Perform initial scan (extracts .zip files, loads plugin DLLs)
            loader.ScanAndLoadAll();

            if (watchDirectory)
            {
                loader.EnableDirectoryWatching();
            }

            return loader;
        });

        // PluginManager — lifecycle management (install/enable/disable/uninstall)
        services.AddSingleton<IPluginManager>(sp =>
        {
            var connectionFactory = sp.GetRequiredService<DataAccess.ITenantConnectionFactory>();
            var registry = sp.GetRequiredService<PlatformFeatureRegistry>();
            var logger = sp.GetRequiredService<ILogger<PluginManager>>();
            var loader = sp.GetService<PluginDirectoryLoader>(); // nullable, may not be configured
            var registryClient = sp.GetService<IRegistryClient>(); // nullable, may not be configured

            return new PluginManager(connectionFactory, registry, sp, logger, loader, registryClient);
        });

        // PluginManifestService — aggregates menu items, pages, settings from all enabled plugins
        services.AddSingleton<PluginManifestService>();

        // PluginValidationPipeline — runs validation checks on staged plugins
        services.AddSingleton<Staging.PluginValidationPipeline>();

        // PluginStagingService — manages upload → validate → approve/reject workflow
        services.AddSingleton<Staging.PluginStagingService>(sp =>
        {
            var connectionFactory = sp.GetRequiredService<DataAccess.ITenantConnectionFactory>();
            var validationPipeline = sp.GetRequiredService<Staging.PluginValidationPipeline>();
            var loader = sp.GetService<PluginDirectoryLoader>(); // nullable
            var logger = sp.GetRequiredService<ILogger<Staging.PluginStagingService>>();
            return new Staging.PluginStagingService(connectionFactory, validationPipeline, loader, logger);
        });

        return services;
    }

    /// <summary>
    /// Builds a set of plugin names that should be excluded from service registration.
    /// Starts with the configured exclusion list, then cascades transitively: if any
    /// dependency of a feature is excluded, the feature itself is also excluded.
    /// </summary>
    private static HashSet<string> BuildServiceExclusionSet(
        IReadOnlyList<IPlatformFeature> allFeatures,
        IReadOnlyCollection<string> configuredExclusions)
    {
        var excluded = new HashSet<string>(configuredExclusions, StringComparer.OrdinalIgnoreCase);

        if (excluded.Count == 0)
            return excluded;

        // Iterate features in topological order (allFeatures is already sorted).
        // If any dependency is excluded, cascade exclusion to the dependent feature.
        foreach (var feature in allFeatures)
        {
            if (excluded.Contains(feature.Name))
                continue;

            foreach (var dep in feature.DependsOn)
            {
                if (excluded.Contains(dep))
                {
                    excluded.Add(feature.Name);
                    break;
                }
            }
        }

        return excluded;
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Pre-scans the plugins directory for external plugin DLLs, discovers
    /// <see cref="IServiceContributor"/> implementations, and registers their services.
    /// Returns the set of plugin names that were pre-loaded so that
    /// <see cref="PluginDirectoryLoader"/> can skip re-loading them.
    /// </summary>
    private static HashSet<string> PreScanExternalPlugins(
        IServiceCollection services,
        string pluginsDirectory,
        PluginBootstrapOptions? bootstrapOptions)
    {
        var preLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullPluginsDir = Path.GetFullPath(pluginsDirectory);

        if (!Directory.Exists(fullPluginsDir))
            return preLoaded;

        var excludeSet = new HashSet<string>(
            bootstrapOptions?.Exclude ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var subDir in Directory.GetDirectories(fullPluginsDir))
        {
            var manifestPath = Path.Combine(subDir, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;

            PluginManifestFile manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<PluginManifestFile>(json, ManifestJsonOptions)!;
            }
            catch
            {
                // Skip plugins with invalid manifests — PluginDirectoryLoader will log detailed errors
                continue;
            }

            if (!manifest.AutoLoad)
                continue;

            if (excludeSet.Contains(manifest.Name))
                continue;

            // Resolve entry assembly path
            var entryDll = manifest.EntryAssembly ?? $"{manifest.Name}.dll";
            var dllPath = Path.GetFullPath(Path.Combine(subDir, entryDll));

            // Security: verify DLL path stays within the plugins directory
            if (!dllPath.StartsWith(fullPluginsDir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(dllPath))
                continue;

            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                var features = FeatureDiscovery.DiscoverFeatures(assembly);

                foreach (var feature in features)
                {
                    if (feature is IServiceContributor contributor
                        && !excludeSet.Contains(feature.Name))
                    {
                        contributor.RegisterServices(services);
                    }
                    preLoaded.Add(feature.Name);
                }
            }
            catch
            {
                // Skip plugins that fail to load — PluginDirectoryLoader handles error reporting
            }
        }

        return preLoaded;
    }
}

/// <summary>
/// Builder for registering platform features during startup.
/// Features are collected and passed to <see cref="PlatformFeatureRegistry"/> for
/// dependency resolution and ordering.
/// </summary>
public class PlatformFeatureBuilder
{
    internal List<IPlatformFeature> Features { get; } = [];

    /// <summary>
    /// Register a feature by type. The type must have a parameterless constructor.
    /// </summary>
    public PlatformFeatureBuilder Add<T>() where T : IPlatformFeature, new()
    {
        Features.Add(new T());
        return this;
    }

    /// <summary>
    /// Register a pre-constructed feature instance.
    /// </summary>
    public PlatformFeatureBuilder Add(IPlatformFeature feature)
    {
        Features.Add(feature);
        return this;
    }

    /// <summary>
    /// Auto-discover and register all <see cref="IPlatformFeature"/> implementations
    /// from the BMMDL.Runtime assembly. This is the standard way to register built-in
    /// platform features — no hardcoded list needed.
    ///
    /// Built-in features are treated identically to external plugins:
    /// same interface, same discovery mechanism, same registration path.
    /// </summary>
    public PlatformFeatureBuilder AddBuiltIn()
    {
        Features.AddRange(FeatureDiscovery.DiscoverBuiltInFeatures());
        return this;
    }

    /// <summary>
    /// Auto-discover and register all <see cref="IPlatformFeature"/> implementations
    /// from the given assembly. Use this for plugin DLLs or extension assemblies.
    /// </summary>
    public PlatformFeatureBuilder AddFromAssembly(Assembly assembly)
    {
        Features.AddRange(FeatureDiscovery.DiscoverFeatures(assembly));
        return this;
    }
}
