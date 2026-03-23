namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Service;

/// <summary>
/// Resolves call statement targets to action/function definitions.
/// Extracted from RuleStatementExecutor, InterpretedActionExecutor, and ServiceEventHandler
/// which all had duplicated resolution logic.
///
/// Uses lazily-built indexes for O(1) lookup instead of linear scans.
/// When constructed with a MetaModelCacheManager, the index is automatically invalidated
/// and rebuilt whenever the underlying meta-model cache changes (version check).
/// </summary>
public class CallTargetResolver : ICallTargetResolver
{
    private readonly IMetaModelCache _cache;
    private readonly MetaModelCacheManager? _cacheManager;

    // Lazily-built indexes for O(1) lookup of service actions/functions by bare name.
    // When multiple services define the same action/function name, first-wins (matching
    // the original linear scan behavior). Qualified "Service.Action" lookups use the
    // service index instead.
    private volatile CallTargetIndex? _index;

    /// <summary>
    /// The cache manager version at the time the index was last built.
    /// Used to detect when the meta-model has changed and the index needs rebuilding.
    /// </summary>
    private long _indexVersion;

    public CallTargetResolver(IMetaModelCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Constructs a resolver that tracks cache version changes via the MetaModelCacheManager.
    /// The index is automatically invalidated when the cache manager's version changes,
    /// ensuring stale indexes are rebuilt after module installations or cache reloads.
    /// </summary>
    public CallTargetResolver(IMetaModelCache cache, MetaModelCacheManager cacheManager)
        : this(cache)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    /// <summary>
    /// Resolve a call target string to a function/action definition.
    /// Supports formats: "EntityName.ActionName", "ServiceName.ActionName", or bare "ActionName".
    /// When serviceName is provided, that service's actions are preferred for disambiguation.
    /// </summary>
    public BmFunction? Resolve(string target, string? serviceName = null)
    {
        var index = EnsureIndex();

        var parts = target.Split('.');
        if (parts.Length == 2)
        {
            // Try entity bound actions/functions first
            var entity = _cache.GetEntity(parts[0]);
            if (entity != null)
            {
                // Entity bound actions/functions are typically small lists, linear scan is fine
                var action = entity.BoundActions.FirstOrDefault(
                    a => a.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                if (action != null) return action;

                var function = entity.BoundFunctions.FirstOrDefault(
                    f => f.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                if (function != null) return function;
            }

            // Try as Service.Action — O(1) lookup via qualified index
            if (index.QualifiedActions.TryGetValue(target, out var qualifiedAction))
                return qualifiedAction;
            if (index.QualifiedFunctions.TryGetValue(target, out var qualifiedFunction))
                return qualifiedFunction;
        }

        // Prefer the current service context for disambiguation — O(1) lookup
        if (!string.IsNullOrEmpty(serviceName))
        {
            var serviceKey = $"{serviceName}.{target}";
            if (index.QualifiedActions.TryGetValue(serviceKey, out var contextAction))
                return contextAction;
            if (index.QualifiedFunctions.TryGetValue(serviceKey, out var contextFunction))
                return contextFunction;
        }

        // Fall back to bare name index — O(1) lookup across all services (first-wins)
        if (index.BareActions.TryGetValue(target, out var bareAction))
            return bareAction;
        if (index.BareFunctions.TryGetValue(target, out var bareFunction))
            return bareFunction;

        return null;
    }

    /// <summary>
    /// Ensures the index is built and up-to-date. Uses double-checked locking pattern with volatile field.
    /// When a MetaModelCacheManager is available, the index is invalidated automatically if the
    /// cache version has changed (e.g. after module installation or cache reload).
    /// </summary>
    private CallTargetIndex EnsureIndex()
    {
        var existingIndex = _index;
        if (existingIndex != null && !IsIndexStale())
            return existingIndex;

        lock (this)
        {
            existingIndex = _index;
            if (existingIndex != null && !IsIndexStale())
                return existingIndex;

            _index = CallTargetIndex.Build(_cache);
            if (_cacheManager != null)
            {
                _indexVersion = _cacheManager.Version;
            }
            return _index;
        }
    }

    /// <summary>
    /// Check if the index is stale by comparing the stored version against the cache manager's current version.
    /// </summary>
    private bool IsIndexStale()
    {
        if (_cacheManager == null) return false;
        return _cacheManager.Version != Interlocked.Read(ref _indexVersion);
    }

    /// <summary>
    /// Immutable index structure for fast action/function lookups.
    /// Built once from a cache snapshot.
    /// </summary>
    private sealed class CallTargetIndex
    {
        /// <summary>
        /// Maps "ServiceName.ActionName" → BmAction (case-insensitive).
        /// </summary>
        public Dictionary<string, BmAction> QualifiedActions { get; }

        /// <summary>
        /// Maps "ServiceName.FunctionName" → BmFunction (case-insensitive).
        /// </summary>
        public Dictionary<string, BmFunction> QualifiedFunctions { get; }

        /// <summary>
        /// Maps bare "ActionName" → BmAction (case-insensitive, first-wins across services).
        /// </summary>
        public Dictionary<string, BmAction> BareActions { get; }

        /// <summary>
        /// Maps bare "FunctionName" → BmFunction (case-insensitive, first-wins across services).
        /// </summary>
        public Dictionary<string, BmFunction> BareFunctions { get; }

        private CallTargetIndex(
            Dictionary<string, BmAction> qualifiedActions,
            Dictionary<string, BmFunction> qualifiedFunctions,
            Dictionary<string, BmAction> bareActions,
            Dictionary<string, BmFunction> bareFunctions)
        {
            QualifiedActions = qualifiedActions;
            QualifiedFunctions = qualifiedFunctions;
            BareActions = bareActions;
            BareFunctions = bareFunctions;
        }

        public static CallTargetIndex Build(IMetaModelCache cache)
        {
            var qualifiedActions = new Dictionary<string, BmAction>(StringComparer.OrdinalIgnoreCase);
            var qualifiedFunctions = new Dictionary<string, BmFunction>(StringComparer.OrdinalIgnoreCase);
            var bareActions = new Dictionary<string, BmAction>(StringComparer.OrdinalIgnoreCase);
            var bareFunctions = new Dictionary<string, BmFunction>(StringComparer.OrdinalIgnoreCase);

            // Take a snapshot of services to avoid concurrent modification during iteration
            var services = cache.Services?.ToList() ?? [];

            foreach (var service in services)
            {
                foreach (var action in service.Actions)
                {
                    // Qualified key: "ServiceName.ActionName"
                    var qualifiedKey = $"{service.Name}.{action.Name}";
                    qualifiedActions.TryAdd(qualifiedKey, action);

                    // Bare key: first service wins (matches original linear scan behavior)
                    bareActions.TryAdd(action.Name, action);
                }

                foreach (var function in service.Functions)
                {
                    var qualifiedKey = $"{service.Name}.{function.Name}";
                    qualifiedFunctions.TryAdd(qualifiedKey, function);

                    bareFunctions.TryAdd(function.Name, function);
                }
            }

            return new CallTargetIndex(qualifiedActions, qualifiedFunctions, bareActions, bareFunctions);
        }
    }
}
