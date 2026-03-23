using BMMDL.Runtime.Plugins;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Registers middleware from all enabled plugins in dependency-resolved order.
/// Called once during app startup.
/// </summary>
public static class PluginMiddlewarePipeline
{
    /// <summary>
    /// Register all plugin middleware in the application pipeline.
    /// Plugins are processed in the order determined by PlatformFeatureRegistry
    /// (topological sort by DependsOn, then Stage as tiebreaker).
    /// </summary>
    public static IApplicationBuilder UsePluginMiddleware(
        this IApplicationBuilder app)
    {
        var registry = app.ApplicationServices.GetRequiredService<PlatformFeatureRegistry>();
        var pluginManager = app.ApplicationServices.GetService<IPluginManager>();

        // Get all middleware providers in dependency order
        var providers = registry.AllFeatures
            .OfType<IMiddlewareProvider>()
            .ToList();

        foreach (var provider in providers)
        {
            // Only register middleware for enabled plugins.
            // If no IPluginManager is registered, all plugins are treated as enabled
            // (built-in features without explicit state management).
            if (pluginManager != null && !pluginManager.IsPluginEnabled(provider.Name))
                continue;

            provider.UseMiddleware(app);
        }

        return app;
    }
}
