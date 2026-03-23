using System.Collections.Generic;
using System.Linq;
using BMMDL.CodeGen.Schema;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Represents differences between two schema snapshots.
/// </summary>
public class SchemaDiff
{
    public List<TableInfo> TablesToAdd { get; set; } = new();
    public List<string> TablesToDrop { get; set; } = new();
    public List<TableChange> TablesToModify { get; set; } = new();

    /// <summary>
    /// Full table information for dropped tables (for rollback script generation).
    /// </summary>
    public List<TableInfo> DroppedTableInfo { get; set; } = new();

    public bool HasChanges =>
        TablesToAdd.Any() ||
        TablesToDrop.Any() ||
        TablesToModify.Any(t => t.HasChanges);
}

/// <summary>
/// Represents changes to a single table.
/// </summary>
public class TableChange
{
    public string TableName { get; set; } = "";
    public string Schema { get; set; } = "public";

    public List<ColumnInfo> ColumnsToAdd { get; set; } = new();
    public List<string> ColumnsToDrop { get; set; } = new();
    public List<ColumnModification> ColumnsToModify { get; set; } = new();

    /// <summary>
    /// Column renames (old_name → new_name). Populated from model.Modifications rename hints
    /// so that field renames generate RENAME COLUMN instead of destructive DROP+ADD.
    /// </summary>
    public List<(string OldName, string NewName)> ColumnRenames { get; set; } = new();

    /// <summary>
    /// Full column information for dropped columns (for rollback script generation).
    /// </summary>
    public List<ColumnInfo> DroppedColumnInfo { get; set; } = new();

    public List<IndexInfo> IndexesToAdd { get; set; } = new();
    public List<string> IndexesToDrop { get; set; } = new();

    public List<ConstraintInfo> ConstraintsToAdd { get; set; } = new();
    public List<string> ConstraintsToDrop { get; set; } = new();

    public bool HasChanges =>
        ColumnsToAdd.Any() || ColumnsToDrop.Any() || ColumnsToModify.Any() ||
        ColumnRenames.Any() ||
        IndexesToAdd.Any() || IndexesToDrop.Any() ||
        ConstraintsToAdd.Any() || ConstraintsToDrop.Any();
}

/// <summary>
/// Represents a modification to a column.
/// </summary>
public class ColumnModification
{
    public string ColumnName { get; set; } = "";
    public ColumnInfo OldDefinition { get; set; } = null!;
    public ColumnInfo NewDefinition { get; set; } = null!;
    public List<ChangeType> Changes { get; set; } = new();
}

/// <summary>
/// Types of changes that can occur to a column.
/// </summary>
public enum ChangeType
{
    DataTypeChange,
    NullabilityChange,
    DefaultValueChange,
    ComputedExpressionChange
}
