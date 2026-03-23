using System.Collections.Concurrent;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Central registry for all platform features. Resolves dependencies via topological sort
/// (Kahn's algorithm) with Stage as tiebreaker, and pre-caches typed sorted lists for each
/// capability interface.
///
/// Supports atomic runtime rebuild via <see cref="Rebuild"/> — used by
/// <see cref="Loading.PluginDirectoryLoader"/> when external plugins are loaded/unloaded.
/// All public properties read from a volatile snapshot, so consumers see a consistent
/// view even during concurrent rebuilds.
///
/// Lives in the Runtime layer — does NOT include API-level behaviors
/// (see ApiFeatureRegistry in Runtime.Api).
/// </summary>
public sealed class PlatformFeatureRegistry
{
    /// <summary>
    /// Immutable snapshot of all pre-cached capability lists.
    /// Swapped atomically via <see cref="Rebuild"/>.
    /// </summary>
    private volatile RegistrySnapshot _snapshot;

    /// <summary>
    /// Set of feature names that are globally active — they apply to ALL entities
    /// regardless of the entity-level AppliesTo check.
    /// Managed by PluginManager via ActivateGlobalFeature/DeactivateGlobalFeature.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _globalFeatures = new(StringComparer.OrdinalIgnoreCase);

    // Pre-cached, typed, sorted lists — DML capability interfaces
    public IReadOnlyList<IFeatureMetadataContributor> MetadataContributors => _snapshot.MetadataContributors;
    public IReadOnlyList<IFeatureQueryFilter> QueryFilters => _snapshot.QueryFilters;
    public IReadOnlyList<IFeatureInsertContributor> InsertContributors => _snapshot.InsertContributors;
    public IReadOnlyList<IFeatureUpdateContributor> UpdateContributors => _snapshot.UpdateContributors;
    public IReadOnlyList<IFeatureDeleteStrategy> DeleteStrategies => _snapshot.DeleteStrategies;
    public IReadOnlyList<IFeatureWriteHook> WriteHooks => _snapshot.WriteHooks;

    // Pre-cached, typed, sorted lists — annotation schema
    public IReadOnlyList<IAnnotationSchemaProvider> AnnotationSchemaProviders => _snapshot.AnnotationSchemaProviders;

    // Pre-cached, typed, sorted lists — full-stack capability interfaces
    public IReadOnlyList<IPlatformEntityProvider> EntityProviders => _snapshot.EntityProviders;
    public IReadOnlyList<IAdminPageProvider> PageProviders => _snapshot.PageProviders;
    public IReadOnlyList<IMenuContributor> MenuContributors => _snapshot.MenuContributors;
    public IReadOnlyList<ISettingsProvider> SettingsProviders => _snapshot.SettingsProviders;
    public IReadOnlyList<IMigrationProvider> MigrationProviders => _snapshot.MigrationProviders;
    public IReadOnlyList<IPluginLifecycle> LifecycleHooks => _snapshot.LifecycleHooks;

    /// <summary>
    /// All registered features, in dependency-resolved order.
    /// </summary>
    public IReadOnlyList<IPlatformFeature> AllFeatures => _snapshot.Features;

    /// <summary>
    /// Names of all registered features, in dependency-resolved order.
    /// </summary>
    public IReadOnlyList<string> AllFeatureNames => _snapshot.FeatureNames;

    public PlatformFeatureRegistry(IEnumerable<IPlatformFeature> features)
    {
        _snapshot = new RegistrySnapshot(TopologicalSort(features.ToList()));
    }

    /// <summary>
    /// Atomically rebuilds all cached capability lists from the given features.
    /// Called by <see cref="Loading.PluginDirectoryLoader"/> when external plugins change.
    /// All readers will see either the old snapshot or the new one — never a partial state.
    /// </summary>
    public void Rebuild(IEnumerable<IPlatformFeature> allFeatures)
    {
        _snapshot = new RegistrySnapshot(TopologicalSort(allFeatures.ToList()));
    }

    // -- Global feature management --

    /// <summary>
    /// Marks a feature as globally active. When globally active, the feature applies to
    /// ALL entities regardless of AppliesTo — unless the feature excludes the entity
    /// via <see cref="IGlobalFeature.ShouldExcludeFromGlobal"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the feature is not registered or does not implement <see cref="IGlobalFeature"/>.
    /// </exception>
    public void ActivateGlobalFeature(string featureName)
    {
        var feature = GetFeature(featureName)
            ?? throw new InvalidOperationException(
                $"Cannot activate global feature '{featureName}': not registered in the registry");

        if (feature is not IGlobalFeature)
            throw new InvalidOperationException(
                $"Cannot activate global feature '{featureName}': it does not implement IGlobalFeature");

        _globalFeatures.TryAdd(featureName, 0);
    }

    /// <summary>
    /// Deactivates global mode for a feature. The feature reverts to entity-level
    /// AppliesTo checks.
    /// </summary>
    public void DeactivateGlobalFeature(string featureName)
    {
        _globalFeatures.TryRemove(featureName, out _);
    }

    /// <summary>
    /// Returns whether the given feature is currently in global mode.
    /// </summary>
    public bool IsFeatureGloballyActive(string featureName)
        => _globalFeatures.ContainsKey(featureName);

    /// <summary>
    /// Returns all globally-activated feature names.
    /// </summary>
    public IReadOnlyCollection<string> GlobalFeatureNames
        => _globalFeatures.Keys.ToList();

    // -- Entity-filtered accessors (with global feature support) --

    public IEnumerable<IFeatureQueryFilter> GetFiltersFor(BmEntity entity)
        => QueryFilters.Where(f => ShouldApply(f, entity));

    public IEnumerable<IFeatureDeleteStrategy> GetDeleteStrategiesFor(BmEntity entity)
        => DeleteStrategies.Where(f => ShouldApply(f, entity));

    public IEnumerable<IFeatureWriteHook> GetWriteHooksFor(BmEntity entity)
        => WriteHooks.Where(f => ShouldApply(f, entity));

    public IEnumerable<IFeatureInsertContributor> GetInsertContributorsFor(BmEntity entity)
        => InsertContributors.Where(f => ShouldApply(f, entity));

    public IEnumerable<IFeatureUpdateContributor> GetUpdateContributorsFor(BmEntity entity)
        => UpdateContributors.Where(f => ShouldApply(f, entity));

    /// <summary>
    /// Determines whether a feature should apply to a given entity.
    /// Checks entity-level AppliesTo first, then falls back to global feature activation.
    /// </summary>
    private bool ShouldApply(IPlatformFeature feature, BmEntity entity)
    {
        // Entity-level check first (most common path)
        if (feature.AppliesTo(entity))
            return true;

        // Global feature check: feature must be globally active AND not excluded for this entity
        if (!_globalFeatures.ContainsKey(feature.Name))
            return false;

        // Check if the feature excludes this specific entity
        if (feature is IGlobalFeature global && global.ShouldExcludeFromGlobal(entity))
            return false;

        return true;
    }

    // -- Full-stack aggregate accessors --

    /// <summary>
    /// Get all menu items from plugins, sorted by order.
    /// When <paramref name="enabledPluginNames"/> is provided, only plugins whose names
    /// are in the set contribute menu items. When null, all registered plugins contribute
    /// (backward compatible).
    /// </summary>
    public IReadOnlyList<PluginMenuItem> GetAggregatedMenuItems(IReadOnlySet<string>? enabledPluginNames = null)
    {
        var snap = _snapshot;
        var contributors = enabledPluginNames != null
            ? snap.MenuContributors.Where(c => enabledPluginNames.Contains(c.Name))
            : snap.MenuContributors;
        return contributors
            .SelectMany(c => c.GetMenuItems())
            .OrderBy(m => m.Order)
            .ToList();
    }

    /// <summary>
    /// Get all page definitions from plugins.
    /// When <paramref name="enabledPluginNames"/> is provided, only plugins whose names
    /// are in the set contribute pages. When null, all registered plugins contribute
    /// (backward compatible).
    /// </summary>
    public IReadOnlyList<PluginPageDefinition> GetAggregatedPages(IReadOnlySet<string>? enabledPluginNames = null)
    {
        var snap = _snapshot;
        var providers = enabledPluginNames != null
            ? snap.PageProviders.Where(p => enabledPluginNames.Contains(p.Name))
            : snap.PageProviders;
        return providers
            .SelectMany(p => p.GetPages())
            .ToList();
    }

    /// <summary>
    /// Get all platform entities from all plugins.
    /// </summary>
    public IReadOnlyList<BmEntity> GetAllPlatformEntities()
        => EntityProviders
            .SelectMany(p => p.GetPlatformEntities())
            .ToList();

    /// <summary>
    /// Get a feature by name, or null if not found.
    /// </summary>
    public IPlatformFeature? GetFeature(string name)
        => _snapshot.ByName.TryGetValue(name, out var f) ? f : null;

    /// <summary>
    /// Get the settings provider for a specific plugin by name.
    /// </summary>
    public ISettingsProvider? GetSettingsProvider(string pluginName)
        => SettingsProviders.FirstOrDefault(s => string.Equals(s.Name, pluginName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Get the migration provider for a specific plugin by name.
    /// </summary>
    public IMigrationProvider? GetMigrationProvider(string pluginName)
        => MigrationProviders.FirstOrDefault(m => string.Equals(m.Name, pluginName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Get the lifecycle hook for a specific plugin by name.
    /// </summary>
    public IPluginLifecycle? GetLifecycleHook(string pluginName)
        => LifecycleHooks.FirstOrDefault(l => string.Equals(l.Name, pluginName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Topological sort using Kahn's algorithm with Stage as tiebreaker via PriorityQueue.
    /// Detects cycles and reports them with a clear error listing the cyclic features.
    /// </summary>
    private static List<IPlatformFeature> TopologicalSort(List<IPlatformFeature> features)
    {
        if (features.Count == 0)
            return [];

        var byName = new Dictionary<string, IPlatformFeature>(features.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var f in features)
        {
            if (!byName.TryAdd(f.Name, f))
                throw new InvalidOperationException(
                    $"Duplicate platform feature name: '{f.Name}'");
        }

        // Build adjacency list and in-degree map
        var inDegree = new Dictionary<string, int>(features.Count, StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(features.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var f in features)
        {
            inDegree[f.Name] = 0;
            dependents[f.Name] = [];
        }

        foreach (var f in features)
        {
            foreach (var dep in f.DependsOn)
            {
                if (!byName.ContainsKey(dep))
                    throw new InvalidOperationException(
                        $"Feature '{f.Name}' depends on unknown feature '{dep}'");

                dependents[dep].Add(f.Name);
                inDegree[f.Name]++;
            }
        }

        // Kahn's algorithm with PriorityQueue (Stage as primary, Name as tiebreaker for stable sort)
        var queue = new PriorityQueue<string, (int Stage, string Name)>();
        foreach (var (name, deg) in inDegree)
        {
            if (deg == 0)
                queue.Enqueue(name, (byName[name].Stage, name));
        }

        var sorted = new List<IPlatformFeature>(features.Count);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(byName[name]);

            foreach (var dependent in dependents[name])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent, (byName[dependent].Stage, dependent));
            }
        }

        if (sorted.Count != features.Count)
        {
            var cyclic = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .OrderBy(n => n);

            throw new InvalidOperationException(
                "Circular dependency detected among platform features: " +
                string.Join(", ", cyclic));
        }

        return sorted;
    }

    /// <summary>
    /// Immutable, internally consistent view of all registry data.
    /// </summary>
    private sealed class RegistrySnapshot
    {
        public IReadOnlyList<IPlatformFeature> Features { get; }
        public IReadOnlyList<string> FeatureNames { get; }
        public IReadOnlyDictionary<string, IPlatformFeature> ByName { get; }

        public IReadOnlyList<IFeatureMetadataContributor> MetadataContributors { get; }
        public IReadOnlyList<IFeatureQueryFilter> QueryFilters { get; }
        public IReadOnlyList<IFeatureInsertContributor> InsertContributors { get; }
        public IReadOnlyList<IFeatureUpdateContributor> UpdateContributors { get; }
        public IReadOnlyList<IFeatureDeleteStrategy> DeleteStrategies { get; }
        public IReadOnlyList<IFeatureWriteHook> WriteHooks { get; }

        public IReadOnlyList<IAnnotationSchemaProvider> AnnotationSchemaProviders { get; }
        public IReadOnlyList<IPlatformEntityProvider> EntityProviders { get; }
        public IReadOnlyList<IAdminPageProvider> PageProviders { get; }
        public IReadOnlyList<IMenuContributor> MenuContributors { get; }
        public IReadOnlyList<ISettingsProvider> SettingsProviders { get; }
        public IReadOnlyList<IMigrationProvider> MigrationProviders { get; }
        public IReadOnlyList<IPluginLifecycle> LifecycleHooks { get; }

        public RegistrySnapshot(List<IPlatformFeature> sorted)
        {
            Features = sorted;
            FeatureNames = sorted.Select(f => f.Name).ToList();
            ByName = sorted.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

            MetadataContributors = sorted.OfType<IFeatureMetadataContributor>().ToList();
            QueryFilters = sorted.OfType<IFeatureQueryFilter>().ToList();
            InsertContributors = sorted.OfType<IFeatureInsertContributor>().ToList();
            UpdateContributors = sorted.OfType<IFeatureUpdateContributor>().ToList();
            DeleteStrategies = sorted.OfType<IFeatureDeleteStrategy>().ToList();
            WriteHooks = sorted.OfType<IFeatureWriteHook>().ToList();

            AnnotationSchemaProviders = sorted.OfType<IAnnotationSchemaProvider>().ToList();
            EntityProviders = sorted.OfType<IPlatformEntityProvider>().ToList();
            PageProviders = sorted.OfType<IAdminPageProvider>().ToList();
            MenuContributors = sorted.OfType<IMenuContributor>().ToList();
            SettingsProviders = sorted.OfType<ISettingsProvider>().ToList();
            MigrationProviders = sorted.OfType<IMigrationProvider>().ToList();
            LifecycleHooks = sorted.OfType<IPluginLifecycle>().ToList();
        }
    }
}
