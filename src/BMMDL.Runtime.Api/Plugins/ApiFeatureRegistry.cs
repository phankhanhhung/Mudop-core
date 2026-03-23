using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// API-level feature registry that wraps <see cref="PlatformFeatureRegistry"/>
/// and adds API-specific <see cref="IEntityOperationBehavior"/> support.
///
/// Behaviors are sorted using the same dependency/stage ordering as platform features
/// (topological sort via DependsOn, then Stage tiebreaker).
/// </summary>
public sealed class ApiFeatureRegistry
{
    private readonly PlatformFeatureRegistry _platformRegistry;
    private readonly IReadOnlyList<IEntityOperationBehavior> _behaviors;

    /// <summary>
    /// All middleware providers in dependency-resolved order.
    /// </summary>
    public IReadOnlyList<IMiddlewareProvider> MiddlewareProviders { get; }

    public ApiFeatureRegistry(
        PlatformFeatureRegistry platformRegistry,
        IEnumerable<IEntityOperationBehavior> behaviors)
    {
        _platformRegistry = platformRegistry;
        _behaviors = SortBehaviors(behaviors.ToList(), platformRegistry);

        // Pre-cache middleware providers from the platform registry's sorted feature list
        MiddlewareProviders = platformRegistry.AllFeatures
            .OfType<IMiddlewareProvider>()
            .ToList();
    }

    /// <summary>
    /// Access the underlying platform registry for DML-level features.
    /// </summary>
    public PlatformFeatureRegistry Platform => _platformRegistry;

    /// <summary>
    /// All registered API behaviors in dependency-resolved order.
    /// </summary>
    public IReadOnlyList<IEntityOperationBehavior> Behaviors => _behaviors;

    /// <summary>
    /// Build a MediatR-style nested pipeline for the given entity.
    /// Applicable behaviors wrap the core handler in reverse order (outermost first).
    /// </summary>
    /// <param name="entity">The entity to filter applicable behaviors for.</param>
    /// <param name="context">The operation context passed to each behavior.</param>
    /// <param name="coreHandler">The innermost handler that executes the actual operation.</param>
    /// <returns>A delegate representing the full pipeline. Invoke it to execute.</returns>
    public EntityOperationDelegate BuildPipeline(
        BmEntity entity,
        EntityOperationContext context,
        EntityOperationDelegate coreHandler)
    {
        var applicable = _behaviors
            .Where(b => b.AppliesTo(entity))
            .Reverse()  // Innermost first, then wrap outward
            .ToList();

        var pipeline = coreHandler;
        foreach (var behavior in applicable)
        {
            var next = pipeline; // Capture for closure
            pipeline = () => behavior.HandleAsync(context, next, CancellationToken.None);
        }

        return pipeline;
    }

    /// <summary>
    /// Sort behaviors by the same dependency/stage rules as platform features.
    /// Behaviors that are also IPlatformFeature (which they always are) inherit
    /// the DependsOn and Stage properties. We apply the same Kahn's algorithm.
    /// </summary>
    private static IReadOnlyList<IEntityOperationBehavior> SortBehaviors(
        List<IEntityOperationBehavior> behaviors,
        PlatformFeatureRegistry platformRegistry)
    {
        if (behaviors.Count == 0)
            return [];

        // Build name -> behavior map
        var byName = new Dictionary<string, IEntityOperationBehavior>(behaviors.Count);
        foreach (var b in behaviors)
        {
            if (!byName.TryAdd(b.Name, b))
                throw new InvalidOperationException(
                    $"Duplicate API behavior name: '{b.Name}'");
        }

        // Collect all known feature names (platform + API behaviors) for dependency validation
        var allKnownNames = new HashSet<string>(platformRegistry.AllFeatureNames);
        foreach (var b in behaviors)
            allKnownNames.Add(b.Name);

        // Build in-degree and adjacency (only among behaviors)
        var inDegree = new Dictionary<string, int>(behaviors.Count);
        var dependents = new Dictionary<string, List<string>>(behaviors.Count);

        foreach (var b in behaviors)
        {
            inDegree[b.Name] = 0;
            dependents[b.Name] = [];
        }

        foreach (var b in behaviors)
        {
            foreach (var dep in b.DependsOn)
            {
                if (!allKnownNames.Contains(dep))
                    throw new InvalidOperationException(
                        $"API behavior '{b.Name}' depends on unknown feature '{dep}'");

                // Only count in-degree for deps that are also behaviors
                if (byName.ContainsKey(dep))
                {
                    dependents[dep].Add(b.Name);
                    inDegree[b.Name]++;
                }
                // Dependencies on platform features are satisfied by definition
                // (platform features run before API behaviors)
            }
        }

        var queue = new PriorityQueue<string, int>();
        foreach (var (name, deg) in inDegree)
        {
            if (deg == 0)
                queue.Enqueue(name, byName[name].Stage);
        }

        var sorted = new List<IEntityOperationBehavior>(behaviors.Count);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(byName[name]);

            foreach (var dependent in dependents[name])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent, byName[dependent].Stage);
            }
        }

        if (sorted.Count != behaviors.Count)
        {
            var cyclic = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .OrderBy(n => n);

            throw new InvalidOperationException(
                "Circular dependency detected among API behaviors: " +
                string.Join(", ", cyclic));
        }

        return sorted;
    }
}
