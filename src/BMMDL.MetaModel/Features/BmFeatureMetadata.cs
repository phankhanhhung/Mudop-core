namespace BMMDL.MetaModel.Features;

/// <summary>
/// Metadata contributed by a single platform feature for a single entity.
/// Populated at compile time by IFeatureMetadataContributor.
/// Consumed at DDL generation and runtime.
/// </summary>
public sealed class BmFeatureMetadata
{
    public required string FeatureName { get; init; }

    // -- DDL contributions --

    /// <summary>
    /// Additional columns to add to the entity's table.
    /// </summary>
    public List<FeatureColumn> Columns { get; } = new();

    /// <summary>
    /// Additional constraints to add to the entity's table.
    /// </summary>
    public List<FeatureConstraint> Constraints { get; } = new();

    /// <summary>
    /// Additional indexes to create for the entity's table.
    /// </summary>
    public List<FeatureIndex> Indexes { get; } = new();

    /// <summary>
    /// Raw SQL statements to execute after table creation (e.g., triggers, RLS policies).
    /// </summary>
    public List<string> PostTableStatements { get; } = new();

    // -- DML contributions (declarative, read at runtime) --

    /// <summary>
    /// Automatic WHERE filters applied to all queries against this entity.
    /// </summary>
    public List<FeatureQueryFilter> QueryFilters { get; } = new();

    /// <summary>
    /// Default column values injected into INSERT statements.
    /// </summary>
    public List<FeatureColumnValue> InsertDefaults { get; } = new();

    /// <summary>
    /// Column values injected into UPDATE SET clauses.
    /// </summary>
    public List<FeatureColumnValue> UpdateSets { get; } = new();

    /// <summary>
    /// Override delete behavior (e.g., soft-delete instead of hard-delete).
    /// Null means use default hard-delete behavior.
    /// </summary>
    public FeatureDeleteStrategyKind? DeleteStrategy { get; set; }

    // -- API contributions --

    /// <summary>
    /// Field names to strip from API input (e.g., system-managed columns).
    /// </summary>
    public List<string> StrippedInputFields { get; } = new();

    /// <summary>
    /// OData annotations to include in API responses (e.g., "@odata.etag").
    /// </summary>
    public List<string> ResponseAnnotations { get; } = new();
}
