using Npgsql;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Accumulated state for INSERT query building.
/// Passed through the waterfall chain of <see cref="IFeatureInsertContributor"/> plugins.
/// Each plugin may add columns, value placeholders, and parameters.
/// </summary>
public class InsertContext
{
    /// <summary>
    /// Column names to include in the INSERT statement.
    /// </summary>
    public List<string> Columns { get; } = [];

    /// <summary>
    /// Value placeholders (e.g., "@p0", "@p1") corresponding to Columns.
    /// </summary>
    public List<string> ValuePlaceholders { get; } = [];

    /// <summary>
    /// Query parameters accumulated by all contributors.
    /// </summary>
    public List<NpgsqlParameter> Parameters { get; } = [];

    /// <summary>
    /// The entity data being inserted.
    /// </summary>
    public Dictionary<string, object?> Data { get; init; } = new();

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
