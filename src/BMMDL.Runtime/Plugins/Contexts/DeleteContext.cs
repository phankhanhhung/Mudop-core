namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Context for DELETE operations, passed to <see cref="IFeatureDeleteStrategy"/>.
/// Contains the table name, primary key condition, and existing entity data.
/// </summary>
public class DeleteContext
{
    /// <summary>
    /// Fully-qualified table name (e.g., "module_schema.table_name").
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// SQL condition identifying the row to delete (e.g., "id = @pk").
    /// </summary>
    public required string PkCondition { get; init; }

    // -- Runtime context --

    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>
    /// The existing entity data before deletion.
    /// </summary>
    public Dictionary<string, object?> ExistingData { get; init; } = new();
}
