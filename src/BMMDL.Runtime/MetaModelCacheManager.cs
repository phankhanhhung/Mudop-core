using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime;

using BMMDL.MetaModel;
using BMMDL.Registry.Repositories;
using BMMDL.Runtime.Plugins;

/// <summary>
/// Manages MetaModelCache with reload capability and lazy loading.
/// Cache is loaded from database on first access, not during construction.
/// This improves startup time and allows the application to start even if the database is empty.
/// When the registry database returns 0 entities, falls back to IPlatformEntityProvider
/// implementations from the PlatformFeatureRegistry (if available).
/// </summary>
public class MetaModelCacheManager : IDisposable
{
    private bool _disposed;
    private readonly string _connectionString;
    private readonly ILogger<MetaModelCacheManager> _logger;
    private readonly PlatformFeatureRegistry? _featureRegistry;
    private readonly IMetaModelRepository? _repository;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile MetaModelCache? _cache;
    private Task<MetaModelCache>? _loadingTask;
    private long _version;

    /// <summary>
    /// Monotonically increasing version counter. Incremented each time the cache is loaded or reloaded.
    /// Consumers can compare this value to detect when the underlying model has changed.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Creates a MetaModelCacheManager that uses a connection string to construct EfCoreMetaModelRepository on demand.
    /// This is the legacy constructor for backward compatibility.
    /// </summary>
    public MetaModelCacheManager(string connectionString, ILogger<MetaModelCacheManager> logger,
        PlatformFeatureRegistry? featureRegistry = null)
    {
        _connectionString = connectionString;
        _logger = logger;
        _featureRegistry = featureRegistry;
        _repository = null;
    }

    /// <summary>
    /// Creates a MetaModelCacheManager that uses a pluggable <see cref="IMetaModelRepository"/> for storage.
    /// This constructor enables custom storage backends via the plugin system.
    /// </summary>
    public MetaModelCacheManager(IMetaModelRepository repository, ILogger<MetaModelCacheManager> logger,
        PlatformFeatureRegistry? featureRegistry = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _connectionString = string.Empty; // Not used when repository is provided
        _logger = logger;
        _featureRegistry = featureRegistry;
    }

    /// <summary>
    /// Get the current MetaModelCache instance synchronously.
    /// If cache is not loaded, this will block until loading completes.
    /// For async code, prefer using GetCacheAsync().
    /// </summary>
    /// <remarks>
    /// This property uses sync-over-async (Task.Run + GetResult) which can cause thread pool starvation
    /// under high load. Prefer <see cref="GetCacheAsync"/> in async contexts.
    /// </remarks>
    [Obsolete("Use GetCacheAsync() instead. This sync-over-async wrapper risks thread pool starvation.")]
    public virtual MetaModelCache Cache
    {
        get
        {
            // Fast path: cache already loaded (no blocking)
            var cache = _cache;
            if (cache != null)
                return cache;

            // Slow path: need to load - use Task.Run to avoid deadlock
            // Task.Run schedules work on thread pool, avoiding sync context capture
            return Task.Run(() => GetCacheAsync()).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Get the current MetaModelCache instance asynchronously.
    /// Loads from database on first access (lazy initialization).
    /// Thread-safe: multiple concurrent calls will share the same loading task.
    /// </summary>
    public virtual async Task<MetaModelCache> GetCacheAsync(CancellationToken ct = default)
    {
        // Fast path: cache already loaded
        var cache = _cache;
        if (cache != null)
            return cache;

        // Slow path: need to load
        await _loadLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock — re-read volatile field into local
            // to prevent JIT from optimizing away the second check
            cache = _cache;
            if (cache != null)
                return cache;

            // If there's already a loading task, wait for it
            if (_loadingTask != null)
            {
                return await _loadingTask;
            }

            // Start loading — clear _loadingTask on failure so next caller retries
            // instead of awaiting a faulted task forever
            try
            {
                _loadingTask = LoadCacheAsync(ct);
                _cache = await _loadingTask;
                _loadingTask = null;
                Interlocked.Increment(ref _version);
                return _cache;
            }
            catch
            {
                _loadingTask = null;
                throw;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Reload the cache from database.
    /// Used after bootstrap or module installation.
    /// Thread-safe: uses lock to prevent concurrent reloads.
    /// </summary>
    /// <remarks>
    /// This method uses sync-over-async (Task.Run + GetResult) which can cause thread pool starvation
    /// under high load. Prefer <see cref="ReloadAsync"/> in async contexts.
    /// </remarks>
    [Obsolete("Use ReloadAsync instead. This sync-over-async wrapper risks thread pool starvation.")]
    public MetaModelCache Reload()
    {
        // Use Task.Run to avoid deadlock from sync-over-async
        // Task.Run schedules work on thread pool, avoiding sync context capture
        return Task.Run(() => ReloadAsync()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reload the cache from database asynchronously.
    /// Used after bootstrap or module installation.
    /// Thread-safe: uses lock to prevent concurrent reloads.
    /// </summary>
    public async Task<MetaModelCache> ReloadAsync(CancellationToken ct = default)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            _logger.LogInformation("Reloading MetaModel cache from database...");

            // Clear any in-flight loading task to prevent double-load
            _loadingTask = null;

            var newCache = await LoadCacheAsync(ct);

            // Atomically replace the cache
            _cache = newCache;
            Interlocked.Increment(ref _version);

            _logger.LogInformation("MetaModel cache reloaded with {EntityCount} entities", newCache.Model.Entities.Count);
            return newCache;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<MetaModelCache> LoadCacheAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Loading MetaModel cache from database...");

        var model = await LoadModelFromRegistryAsync(ct);

        if (model.Entities.Count == 0 && _featureRegistry is not null)
        {
            _logger.LogWarning(
                "Registry database returned 0 entities. Loading fallback entities from IPlatformEntityProvider implementations.");

            var fallbackEntities = _featureRegistry.GetAllPlatformEntities();
            foreach (var entity in fallbackEntities)
            {
                model.Entities.Add(entity);
            }

            _logger.LogInformation("Loaded {Count} fallback entities from platform features", fallbackEntities.Count);
        }

        _logger.LogInformation("Loaded meta-model with {EntityCount} entities from database", model.Entities.Count);

        ApplyTenantScopingFixup(model);

        return new MetaModelCache(model);
    }

    /// <summary>
    /// Applies tenant scoping rules based on whether the MultiTenancy plugin is active.
    /// - Active: all entities default to tenant-scoped (except @SystemScoped/@System/@Global)
    /// - Inactive: all entities forced to system-scoped (warn if any were @TenantScoped)
    /// Must be called BEFORE MetaModelCache construction (entities are mutable at this point).
    /// </summary>
    private void ApplyTenantScopingFixup(BmModel model)
    {
        var isMultiTenantActive = _featureRegistry?.IsFeatureGloballyActive("TenantIsolation") ?? false;
        _logger.LogInformation("Tenant scoping fixup: MultiTenancy active={IsActive}, entities={Count}, registry={HasRegistry}",
            isMultiTenantActive, model.Entities.Count, _featureRegistry != null);

        if (isMultiTenantActive)
        {
            var autoScoped = 0;
            foreach (var entity in model.Entities)
            {
                // Skip entities already marked tenant-scoped
                if (entity.TenantScoped) continue;

                // Check if entity has a tenantId field (added by DDL or defined in BMMDL source).
                // This is the reliable indicator: if the compiled DDL has tenant_id column,
                // the entity should be tenant-scoped for query filtering.
                // Entities with @SystemScoped/@system annotation won't have this field.
                var hasTenantField = entity.Fields.Any(f =>
                    f.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ||
                    f.Name.Equals("tenant_id", StringComparison.OrdinalIgnoreCase));

                if (hasTenantField)
                {
                    entity.TenantScoped = true;
                    autoScoped++;
                }
            }

            if (autoScoped > 0)
                _logger.LogInformation(
                    "Tenant scoping fixup: auto-scoped {Count} entities as tenant-scoped (MultiTenancy active, detected tenant_id field)",
                    autoScoped);
        }
        else
        {
            foreach (var entity in model.Entities)
            {
                if (entity.TenantScoped)
                {
                    _logger.LogWarning(
                        "Entity '{Name}' is marked @TenantScoped but MultiTenancy plugin is not active. " +
                        "Forcing system-scoped. Install and enable the TenantIsolation plugin for tenant isolation.",
                        entity.QualifiedName);
                    entity.TenantScoped = false;
                }
            }
        }
    }

    /// <summary>
    /// Loads the BmModel from the registry storage backend.
    /// If an <see cref="IMetaModelRepository"/> was provided at construction, delegates to it.
    /// Otherwise, falls back to creating a temporary EfCoreMetaModelRepository from the connection string.
    /// Protected virtual to allow test subclasses to provide a model without requiring a real database.
    /// </summary>
    protected virtual async Task<BmModel> LoadModelFromRegistryAsync(CancellationToken ct = default)
    {
        // Pluggable path: use injected repository
        if (_repository is not null)
        {
            return await _repository.LoadModelAsync(ct);
        }

        // Legacy path: create EfCoreMetaModelRepository from connection string
        var optionsBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BMMDL.Registry.Data.RegistryDbContext>();
        optionsBuilder.UseNpgsql(_connectionString);

        await using var dbContext = new BMMDL.Registry.Data.RegistryDbContext(optionsBuilder.Options);

        // Use System Tenant ID (00000000-0000-0000-0000-000000000000) to load all platform entities
        var systemTenantId = Guid.Empty;
        var repository = new BMMDL.Registry.Repositories.EfCoreMetaModelRepository(dbContext, systemTenantId, null);

        return await repository.LoadModelAsync(ct);
    }

    /// <summary>
    /// Dispose the semaphore used for load synchronization.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern for derived classes.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _loadLock.Dispose();
        }
        _disposed = true;
    }
}
