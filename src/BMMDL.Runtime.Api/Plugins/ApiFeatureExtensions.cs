using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// DI registration helpers for API-level feature support.
/// Call after <see cref="PlatformFeatureExtensions.AddPlatformFeatures"/> to layer
/// API behaviors on top of the platform registry.
/// </summary>
public static class ApiFeatureExtensions
{
    /// <summary>
    /// Registers the <see cref="ApiFeatureRegistry"/> as a singleton.
    /// Optionally configure API-specific behaviors via the builder action.
    /// Requires <see cref="PlatformFeatureRegistry"/> to be already registered.
    /// </summary>
    public static IServiceCollection AddApiFeatures(
        this IServiceCollection services,
        Action<ApiFeatureBuilder>? configure = null)
    {
        var builder = new ApiFeatureBuilder();
        configure?.Invoke(builder);

        services.AddSingleton(sp =>
        {
            var platformRegistry = sp.GetRequiredService<PlatformFeatureRegistry>();
            return new ApiFeatureRegistry(platformRegistry, builder.Behaviors);
        });

        return services;
    }

    /// <summary>
    /// Discovers <see cref="IAdminApiProvider"/> implementations in the API assembly
    /// AND in loaded external plugin assemblies, calls their
    /// <see cref="IAdminApiProvider.RegisterServices"/> to register DI services,
    /// and caches the providers for endpoint mapping via <see cref="MapPluginEndpoints"/>.
    /// </summary>
    public static IServiceCollection AddPluginApiEndpoints(
        this IServiceCollection services, string? pluginsDirectory = null)
    {
        // Discover from the host API assembly
        var apiProviders = FeatureDiscovery
            .DiscoverFeatures(typeof(IAdminApiProvider).Assembly)
            .OfType<IAdminApiProvider>()
            .ToList();

        // Also discover from external plugin assemblies in plugins/ directory
        if (!string.IsNullOrEmpty(pluginsDirectory))
        {
            var fullDir = Path.GetFullPath(pluginsDirectory);
            if (Directory.Exists(fullDir))
            {
                foreach (var subDir in Directory.GetDirectories(fullDir))
                {
                    foreach (var dll in Directory.GetFiles(subDir, "*.dll"))
                    {
                        try
                        {
                            var assembly = System.Reflection.Assembly.LoadFrom(dll);
                            var externalProviders = FeatureDiscovery
                                .DiscoverFeatures(assembly)
                                .OfType<IAdminApiProvider>();
                            apiProviders.AddRange(externalProviders);
                        }
                        catch
                        {
                            // Skip DLLs that fail to load — PluginDirectoryLoader handles errors
                        }
                    }
                }
            }
        }

        services.AddSingleton<IReadOnlyList<IAdminApiProvider>>(apiProviders);

        foreach (var provider in apiProviders)
        {
            provider.RegisterServices(services);
        }

        return services;
    }

    /// <summary>
    /// Maps HTTP endpoints from all discovered <see cref="IAdminApiProvider"/> plugins.
    /// Must be called after <see cref="AddPluginApiEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapPluginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiProviders = endpoints.ServiceProvider
            .GetRequiredService<IReadOnlyList<IAdminApiProvider>>();

        foreach (var provider in apiProviders)
        {
            provider.MapEndpoints(endpoints);
        }

        return endpoints;
    }

    /// <summary>
    /// Registers plugin middleware in the application pipeline.
    /// Delegates to <see cref="PluginMiddlewarePipeline.UsePluginMiddleware"/> which
    /// processes all <see cref="IMiddlewareProvider"/> implementations in dependency order.
    /// </summary>
    public static IApplicationBuilder UseApiPluginMiddleware(this IApplicationBuilder app)
    {
        return app.UsePluginMiddleware();
    }
}

/// <summary>
/// Builder for registering API-level entity operation behaviors during startup.
/// </summary>
public class ApiFeatureBuilder
{
    internal List<IEntityOperationBehavior> Behaviors { get; } = [];

    /// <summary>
    /// Register a behavior by type. The type must have a parameterless constructor.
    /// </summary>
    public ApiFeatureBuilder Add<T>() where T : IEntityOperationBehavior, new()
    {
        Behaviors.Add(new T());
        return this;
    }

    /// <summary>
    /// Register a pre-constructed behavior instance.
    /// </summary>
    public ApiFeatureBuilder Add(IEntityOperationBehavior behavior)
    {
        Behaviors.Add(behavior);
        return this;
    }
}
