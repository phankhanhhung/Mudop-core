using Microsoft.Extensions.DependencyInjection;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Plugin interface for contributing DI service registrations.
/// Plugins that need runtime services (stores, background processors, event handlers)
/// implement this interface to register them during startup — instead of hardcoding
/// service registrations in Program.cs.
///
/// <para>
/// This runs during the DI container build phase (before <c>builder.Build()</c>),
/// so services are always registered. To conditionally operate based on plugin state,
/// services should check <see cref="IPluginManager.IsPluginEnabled"/> at runtime.
/// </para>
///
/// <example>
/// <code>
/// public void RegisterServices(IServiceCollection services)
/// {
///     services.AddSingleton&lt;IMyStore&gt;(sp =>
///     {
///         var connString = sp.GetRequiredService&lt;ITenantConnectionFactory&gt;().ConnectionString;
///         var logger = sp.GetRequiredService&lt;ILogger&lt;MyStore&gt;&gt;();
///         return new MyStore(connString, logger);
///     });
/// }
/// </code>
/// </example>
/// </summary>
public interface IServiceContributor
{
    /// <summary>
    /// Register DI services needed by this plugin.
    /// Called once during application startup (DI container build phase).
    /// </summary>
    void RegisterServices(IServiceCollection services);
}
