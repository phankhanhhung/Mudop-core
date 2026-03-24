using BMMDL.Registry.Repositories;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Plugin interface for providing a custom registry storage backend.
/// Plugins implementing this interface can replace the default EF Core / PostgreSQL
/// storage with alternatives (e.g., file-based, in-memory, remote API, cloud-native stores).
///
/// <para>
/// At most one <see cref="IRegistryStorageProvider"/> should be active at a time.
/// If multiple providers are registered, the one with the highest <see cref="IPlatformFeature.Stage"/>
/// value takes precedence.
/// </para>
///
/// <para>
/// The provider is consulted during DI resolution of <see cref="IMetaModelRepository"/>.
/// If no custom provider is found, the default <see cref="EfCoreMetaModelRepository"/> is used.
/// </para>
/// </summary>
public interface IRegistryStorageProvider
{
    /// <summary>
    /// Create a repository instance for the given service scope.
    /// The implementation should resolve any dependencies it needs from the service provider.
    /// </summary>
    /// <param name="services">The current DI service provider (scoped).</param>
    /// <returns>A fully initialized <see cref="IMetaModelRepository"/> instance.</returns>
    IMetaModelRepository CreateRepository(IServiceProvider services);
}
