namespace BMMDL.Runtime.Api.Helpers;

/// <summary>
/// Detects circular dependencies using topological sort (Kahn's algorithm).
/// Extracted from BatchController for reuse.
/// </summary>
public static class DependencyGraphValidator
{
    /// <summary>
    /// Detect circular dependencies in a set of items with dependencies.
    /// Returns an error message if a cycle is detected, or null if the dependency graph is acyclic.
    /// </summary>
    /// <param name="items">Items with Id and optional DependsOn array.</param>
    /// <returns>Error message if cycle detected, null otherwise.</returns>
    public static string? DetectCycles(IReadOnlyList<(string Id, string[]? DependsOn)> items)
    {
        // Build adjacency list and in-degree map
        var itemIds = new HashSet<string>(items.Select(r => r.Id));
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (!inDegree.ContainsKey(item.Id))
                inDegree[item.Id] = 0;
            if (!adjacency.ContainsKey(item.Id))
                adjacency[item.Id] = new List<string>();

            if (item.DependsOn == null) continue;

            foreach (var dep in item.DependsOn)
            {
                // Only consider dependencies that reference other items in this set
                if (!itemIds.Contains(dep)) continue;

                if (!adjacency.ContainsKey(dep))
                    adjacency[dep] = new List<string>();
                adjacency[dep].Add(item.Id);

                inDegree[item.Id] = inDegree.GetValueOrDefault(item.Id, 0) + 1;
                if (!inDegree.ContainsKey(dep))
                    inDegree[dep] = 0;
            }
        }

        // Kahn's algorithm: process nodes with in-degree 0
        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var processedCount = 0;

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            processedCount++;

            if (!adjacency.TryGetValue(node, out var neighbors)) continue;
            foreach (var neighbor in neighbors)
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (processedCount < inDegree.Count)
        {
            // Nodes remaining with in-degree > 0 are part of cycles
            var cycleNodes = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            return $"Circular dependency detected among batch requests: {string.Join(", ", cycleNodes)}. " +
                   "Reorder requests or remove circular dependsOn references.";
        }

        return null; // No cycles
    }

    /// <summary>
    /// Check that the maximum dependency chain depth does not exceed a limit.
    /// Assumes the graph is acyclic (call DetectCycles first).
    /// </summary>
    public static string? CheckMaxDepth(IReadOnlyList<(string Id, string[]? DependsOn)> items, int maxDepth)
    {
        var depMap = items.ToDictionary(i => i.Id, i => i.DependsOn ?? Array.Empty<string>(), StringComparer.Ordinal);
        var depthCache = new Dictionary<string, int>(StringComparer.Ordinal);

        int GetDepth(string id)
        {
            if (depthCache.TryGetValue(id, out var cached)) return cached;
            depthCache[id] = 0; // Guard against unexpected cycles
            var deps = depMap.GetValueOrDefault(id, Array.Empty<string>());
            var depth = deps.Length == 0 ? 0 : deps.Where(depMap.ContainsKey).Max(d => GetDepth(d) + 1);
            depthCache[id] = depth;
            return depth;
        }

        foreach (var item in items)
        {
            var depth = GetDepth(item.Id);
            if (depth > maxDepth)
            {
                return $"Dependency chain for request '{item.Id}' exceeds maximum depth of {maxDepth}. " +
                       "Reduce the number of chained dependencies.";
            }
        }

        return null;
    }
}
