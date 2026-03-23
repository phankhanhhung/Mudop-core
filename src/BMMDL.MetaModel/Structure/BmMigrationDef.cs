using BMMDL.MetaModel.Abstractions;

namespace BMMDL.MetaModel.Structure;

/// <summary>
/// Migration definition — explicit schema migration steps declared in BMMDL.
/// </summary>
public class BmMigrationDef : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public bool Breaking { get; set; }
    public List<string> Dependencies { get; } = new();

    public List<BmMigrationStep> UpSteps { get; } = new();
    public List<BmMigrationStep> DownSteps { get; } = new();

    public List<BmAnnotation> Annotations { get; } = new();

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

// ============================================================
// Migration Steps
// ============================================================

/// <summary>
/// Base class for all migration step types.
/// </summary>
public abstract class BmMigrationStep
{
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
}

/// <summary>
/// ALTER ENTITY step — modifies an existing entity's schema.
/// </summary>
public class BmAlterEntityStep : BmMigrationStep
{
    public string EntityName { get; set; } = "";
    public List<BmAlterAction> Actions { get; } = new();
}

/// <summary>
/// ADD ENTITY step — creates a new entity within a migration.
/// </summary>
public class BmAddEntityStep : BmMigrationStep
{
    public string EntityName { get; set; } = "";
    /// <summary>
    /// Raw text of the entity elements block (kept for backward compatibility / diagnostics).
    /// </summary>
    public string ElementsText { get; set; } = "";

    /// <summary>
    /// Structured field definitions parsed from the entity elements block.
    /// </summary>
    public List<BmField> Fields { get; } = new();

    /// <summary>
    /// Structured association definitions parsed from the entity elements block.
    /// </summary>
    public List<BmAssociation> Associations { get; } = new();

    /// <summary>
    /// Structured composition definitions parsed from the entity elements block.
    /// </summary>
    public List<BmComposition> Compositions { get; } = new();

    /// <summary>
    /// Index definitions parsed from the entity elements block.
    /// </summary>
    public List<BmIndex> Indexes { get; } = new();

    /// <summary>
    /// Constraint definitions parsed from the entity elements block.
    /// </summary>
    public List<BmConstraint> Constraints { get; } = new();
}

/// <summary>
/// DROP ENTITY step — removes an entity within a migration.
/// </summary>
public class BmDropEntityStep : BmMigrationStep
{
    public string EntityName { get; set; } = "";
}

/// <summary>
/// TRANSFORM step — data transformation on an entity.
/// </summary>
public class BmTransformStep : BmMigrationStep
{
    public string EntityName { get; set; } = "";
    public List<BmTransformAction> Actions { get; } = new();
}

// ============================================================
// Alter Actions (within ALTER ENTITY)
// ============================================================

public abstract class BmAlterAction
{
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
}

public class BmAlterAddColumnAction : BmAlterAction
{
    public string FieldName { get; set; } = "";
    public string TypeString { get; set; } = "";
    public bool IsKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public string? DefaultValue { get; set; }
}

public class BmAlterDropColumnAction : BmAlterAction
{
    public string ColumnName { get; set; } = "";
}

public class BmAlterRenameColumnAction : BmAlterAction
{
    public string OldName { get; set; } = "";
    public string NewName { get; set; } = "";
}

public class BmAlterColumnAction : BmAlterAction
{
    public string ColumnName { get; set; } = "";
    public List<BmAlterColumnChange> Changes { get; } = new();
}

public class BmAlterAddIndexAction : BmAlterAction
{
    public string IndexName { get; set; } = "";
    public List<string> Columns { get; } = new();
    public bool IsUnique { get; set; }
}

public class BmAlterDropIndexAction : BmAlterAction
{
    public string IndexName { get; set; } = "";
}

public class BmAlterAddConstraintAction : BmAlterAction
{
    public string ConstraintName { get; set; } = "";
    public string ConstraintText { get; set; } = "";
}

public class BmAlterDropConstraintAction : BmAlterAction
{
    public string ConstraintName { get; set; } = "";
}

// ============================================================
// Alter Column Changes (within ALTER COLUMN)
// ============================================================

public abstract class BmAlterColumnChange { }

public class BmChangeTypeChange : BmAlterColumnChange
{
    public string NewTypeString { get; set; } = "";
}

public class BmSetDefaultChange : BmAlterColumnChange
{
    public string Expression { get; set; } = "";
}

public class BmDropDefaultChange : BmAlterColumnChange { }

public class BmSetNullableChange : BmAlterColumnChange
{
    public bool IsNullable { get; set; }
}

// ============================================================
// Transform Actions (within TRANSFORM)
// ============================================================

public abstract class BmTransformAction
{
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
}

public class BmTransformSetAction : BmTransformAction
{
    public string FieldName { get; set; } = "";
    public string Expression { get; set; } = "";
}

public class BmTransformUpdateAction : BmTransformAction
{
    public List<BmTransformAssignment> Assignments { get; } = new();
    public string WhereClause { get; set; } = "";
}

public class BmTransformAssignment
{
    public string FieldName { get; set; } = "";
    public string Expression { get; set; } = "";
}
