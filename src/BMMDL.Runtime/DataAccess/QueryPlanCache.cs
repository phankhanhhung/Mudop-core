namespace BMMDL.Runtime.DataAccess;

using Npgsql;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// LRU cache for pre-built SQL query plans.
/// Caches generated SQL to avoid repeated parsing and building.
/// </summary>
public class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, CachedPlan> _cache;
    private readonly int _maxSize;
    private readonly object _evictionLock = new();

    /// <summary>
    /// Create a new query plan cache.
    /// </summary>
    /// <param name="maxSize">Maximum number of cached plans.</param>
    public QueryPlanCache(int maxSize = 1000)
    {
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Max size must be positive.");
            
        _maxSize = maxSize;
        _cache = new ConcurrentDictionary<string, CachedPlan>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Try to get a cached query plan.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <returns>Cached SQL and parameters, or null if not found.</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters)? TryGet(string key)
    {
        if (_cache.TryGetValue(key, out var plan))
        {
            plan.LastAccessed = DateTime.UtcNow;
            plan.HitCount++;
            return (plan.Sql, CloneParameters(plan.Parameters));
        }
        
        return null;
    }

    /// <summary>
    /// Cache a query plan.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="sql">SQL query.</param>
    /// <param name="parameters">Query parameters (will be cloned).</param>
    public void Set(string key, string sql, IReadOnlyList<NpgsqlParameter> parameters)
    {
        var plan = new CachedPlan
        {
            Sql = sql,
            Parameters = CloneParameters(parameters),
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow
        };
        
        // Use lock to make check-evict-insert atomic
        lock (_evictionLock)
        {
            // Evict if at capacity
            if (_cache.Count >= _maxSize)
            {
                EvictLeastRecentlyUsedUnsafe();
            }
            
            _cache[key] = plan;
        }
    }

    /// <summary>
    /// Invalidate all cached plans for an entity.
    /// </summary>
    /// <param name="entityName">Entity name (simple or qualified).</param>
    public void Invalidate(string entityName)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(entityName + ":", StringComparison.OrdinalIgnoreCase))
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clear all cached plans.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        var plans = _cache.Values.ToList();
        return new CacheStatistics
        {
            TotalEntries = plans.Count,
            TotalHits = plans.Sum(p => p.HitCount),
            OldestEntry = plans.Count > 0 ? plans.Min(p => p.CreatedAt) : null,
            NewestEntry = plans.Count > 0 ? plans.Max(p => p.CreatedAt) : null
        };
    }

    /// <summary>
    /// Create a cache key for a query.
    /// </summary>
    /// <param name="entityName">Entity name.</param>
    /// <param name="operation">Operation type (Select, Insert, Update, Delete).</param>
    /// <param name="options">Query options (optional).</param>
    /// <returns>Unique cache key.</returns>
    public static string CreateKey(string entityName, string operation, QueryOptions? options = null)
    {
        if (options == null)
        {
            return $"{entityName}:{operation}";
        }

        // Create hash of options for uniqueness
        var optionsHash = ComputeOptionsHash(options);
        return $"{entityName}:{operation}:{optionsHash}";
    }

    /// <summary>
    /// Compute a hash of query options for cache key.
    /// </summary>
    private static string ComputeOptionsHash(QueryOptions options)
    {
        var sb = new StringBuilder();
        
        if (!string.IsNullOrEmpty(options.Filter))
            sb.Append("f:").Append(options.Filter).Append(';');
        if (!string.IsNullOrEmpty(options.OrderBy))
            sb.Append("o:").Append(options.OrderBy).Append(';');
        if (!string.IsNullOrEmpty(options.Select))
            sb.Append("s:").Append(options.Select).Append(';');
        if (options.Top.HasValue)
            sb.Append("t:").Append(options.Top.Value).Append(';');
        if (options.Skip.HasValue)
            sb.Append("k:").Append(options.Skip.Value).Append(';');
        if (options.IncludeDeleted)
            sb.Append("d:1;");
        // Note: TenantId is not included in key as it changes per request
        
        if (sb.Length == 0)
            return "default";
            
        // Use MD5 for short, consistent hash (not for security)
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Evict least recently used entries.
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        lock (_evictionLock)
        {
            EvictLeastRecentlyUsedUnsafe();
        }
    }
    
    /// <summary>
    /// Evict least recently used entries. Caller must hold _evictionLock.
    /// </summary>
    private void EvictLeastRecentlyUsedUnsafe()
    {
        // Double-check count
        if (_cache.Count < _maxSize)
            return;

        // Find LRU entries (evict 10% at a time)
        var evictCount = Math.Max(1, _maxSize / 10);
        var keysToEvict = _cache
            .OrderBy(kvp => kvp.Value.LastAccessed)
            .Take(evictCount)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in keysToEvict)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clone parameters to avoid sharing mutable state.
    /// </summary>
    private static List<NpgsqlParameter> CloneParameters(IReadOnlyList<NpgsqlParameter> parameters)
    {
        return parameters
            .Select(p => new NpgsqlParameter { ParameterName = p.ParameterName, Value = p.Value, NpgsqlDbType = p.NpgsqlDbType })
            .ToList();
    }

    /// <summary>
    /// Cached query plan.
    /// </summary>
    private class CachedPlan
    {
        public required string Sql { get; init; }
        public required List<NpgsqlParameter> Parameters { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime LastAccessed { get; set; }
        public long HitCount { get; set; }
    }
}

/// <summary>
/// Cache statistics for monitoring.
/// </summary>
public record CacheStatistics
{
    public int TotalEntries { get; init; }
    public long TotalHits { get; init; }
    public DateTime? OldestEntry { get; init; }
    public DateTime? NewestEntry { get; init; }
}
