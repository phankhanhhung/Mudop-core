using System;
using System.Collections.Generic;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Represents a snapshot of database schema at a point in time.
/// Used for schema comparison and migration generation.
/// </summary>
public class SchemaSnapshot
{
    public List<TableInfo> Tables { get; set; } = new();
    public DateTime ReadAt { get; set; }
    public string DatabaseVersion { get; set; } = "";
    public string SchemaName { get; set; } = "public";
}

/// <summary>
/// Represents a database table with its structure.
/// </summary>
public class TableInfo
{
    public string Schema { get; set; } = "public";
    public string Name { get; set; } = "";
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<IndexInfo> Indexes { get; set; } = new();
    public List<ConstraintInfo> Constraints { get; set; } = new();
    
    public string FullyQualifiedName => string.IsNullOrEmpty(Schema) || Schema == "public" 
        ? Name 
        : $"{Schema}.{Name}";
    
    // Phase 8: Temporal properties
    /// <summary>
    /// Indicates if this table has temporal columns (system_start, system_end).
    /// </summary>
    public bool IsTemporal => Columns.Any(c => 
        c.Name.Equals("system_start", StringComparison.OrdinalIgnoreCase) ||
        c.Name.Equals("system_end", StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Get the history table name for this table (if it's a temporal table).
    /// </summary>
    public string HistoryTableName => $"{Name}_history";
}

/// <summary>
/// Represents a database column with its metadata.
/// </summary>
public class ColumnInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsGenerated { get; set; }
    public string? GenerationExpression { get; set; }
    public int OrdinalPosition { get; set; }
    public int? MaxLength { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
}

/// <summary>
/// Represents a database index.
/// </summary>
public class IndexInfo
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsPrimary { get; set; }
    public string? Definition { get; set; }
}

/// <summary>
/// Represents a database constraint.
/// </summary>
public class ConstraintInfo
{
    public string Name { get; set; } = "";
    public ConstraintType Type { get; set; }
    public List<string> Columns { get; set; } = new();
    public string? ReferencedTable { get; set; }
    public List<string>? ReferencedColumns { get; set; }
    public string? CheckClause { get; set; }
}

/// <summary>
/// Types of database constraints.
/// </summary>
public enum ConstraintType
{
    PrimaryKey,
    ForeignKey,
    Unique,
    Check,
    /// <summary>
    /// EXCLUDE constraint (used for temporal overlap prevention).
    /// </summary>
    Exclude
}
