using Npgsql;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Accumulated state for SELECT query building.
/// Passed through the waterfall chain of <see cref="IFeatureQueryFilter"/> plugins.
/// Each plugin may add WHERE clauses, JOIN clauses, parameters, or override the FROM source.
/// </summary>
public class QueryFilterContext
{
    /// <summary>
    /// WHERE clause expressions to be ANDed together.
    /// </summary>
    public List<WhereClause> WhereClauses { get; } = [];

    /// <summary>
    /// Query parameters accumulated by all filters.
    /// </summary>
    public List<NpgsqlParameter> Parameters { get; } = [];

    /// <summary>
    /// Optional FROM override (e.g., Temporal UNION ALL of main + history table).
    /// </summary>
    public string? FromOverride { get; set; }

    /// <summary>
    /// Additional JOIN clauses contributed by features.
    /// </summary>
    public List<string> JoinClauses { get; } = [];

    // -- Runtime context (read-only for plugins) --

    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? Locale { get; init; }
    public DateTimeOffset? AsOf { get; init; }
    public DateTime? ValidAt { get; init; }
    public IFeatureFilterState FilterState { get; init; } = null!;

    /// <summary>
    /// The alias used for the main table in the query (default "m").
    /// Features should reference this instead of hardcoding "m", since the
    /// FROM source may be overridden (e.g., temporal UNION ALL subquery).
    /// </summary>
    public string TableAlias { get; set; } = "m";

    /// <summary>
    /// Returns the next available parameter index (for generating unique placeholder names).
    /// </summary>
    public int NextParamIndex() => Parameters.Count;
}
