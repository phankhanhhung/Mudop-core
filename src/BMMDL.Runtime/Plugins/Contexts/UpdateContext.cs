using Npgsql;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Accumulated state for UPDATE query building.
/// Passed through the waterfall chain of <see cref="IFeatureUpdateContributor"/> plugins.
/// Each plugin may add SET clauses and parameters.
/// </summary>
public class UpdateContext
{
    /// <summary>
    /// SET clause expressions (e.g., "updated_at = @p3").
    /// </summary>
    public List<string> SetClauses { get; } = [];

    /// <summary>
    /// Query parameters accumulated by all contributors.
    /// </summary>
    public List<NpgsqlParameter> Parameters { get; } = [];

    /// <summary>
    /// The new entity data being applied.
    /// </summary>
    public Dictionary<string, object?> Data { get; init; } = new();

    /// <summary>
    /// The existing entity data before the update (may be null if not loaded).
    /// </summary>
    public Dictionary<string, object?>? OldData { get; init; }

    // -- Runtime context (read-only for plugins) --

    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>
    /// Returns the next available parameter index (for generating unique placeholder names).
    /// Avoids collisions when multiple features chain and add parameters.
    /// </summary>
    private int _paramIndex = 0;
    public int NextParamIndex() => _paramIndex++;
}
