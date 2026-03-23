using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Migration;

/// <summary>
/// Represents a complete diff between two model versions.
/// </summary>
public class ModelDiff
{
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Namespace { get; set; } = "";
    public ChangeType OverallChangeType { get; set; } = ChangeType.Patch;
    
    public List<EntityDiff> EntityChanges { get; } = new();
    public List<TypeDiff> TypeChanges { get; } = new();
    public List<EnumDiff> EnumChanges { get; } = new();
    
    /// <summary>
    /// Check if there are any changes.
    /// </summary>
    public bool HasChanges => EntityChanges.Count > 0 || TypeChanges.Count > 0 || EnumChanges.Count > 0;
    
    /// <summary>
    /// Check if there are any breaking changes.
    /// </summary>
    public bool HasBreakingChanges => OverallChangeType == ChangeType.Breaking;
    
    /// <summary>
    /// Compute overall change type based on individual changes.
    /// </summary>
    public void ComputeOverallChangeType()
    {
        if (EntityChanges.Any(e => e.HasBreakingChanges) ||
            TypeChanges.Any(t => t.ChangeKind == DiffKind.Removed) ||
            EnumChanges.Any(e => e.ChangeKind == DiffKind.Removed))
        {
            OverallChangeType = ChangeType.Breaking;
        }
        else if (EntityChanges.Any(e => e.ChangeKind != DiffKind.Unchanged) ||
                 TypeChanges.Any() || EnumChanges.Any())
        {
            OverallChangeType = ChangeType.Compatible;
        }
        else
        {
            OverallChangeType = ChangeType.Patch;
        }
    }
}

/// <summary>
/// Change type for versioning.
/// </summary>
public enum ChangeType
{
    Patch,      // No schema changes
    Compatible, // Non-breaking additions
    Breaking    // Breaking changes (removals, type changes)
}

/// <summary>
/// Kind of diff operation.
/// </summary>
public enum DiffKind
{
    Unchanged,
    Added,
    Modified,
    Removed,
    Renamed
}

/// <summary>
/// Diff for an entity.
/// </summary>
public class EntityDiff
{
    public string EntityName { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public List<FieldDiff> FieldChanges { get; } = new();
    public List<AssociationDiff> AssociationChanges { get; } = new();
    public List<IndexDiff> IndexChanges { get; } = new();
    
    /// <summary>
    /// For added entities, stores the full entity definition for DSL generation.
    /// </summary>
    public BmEntity? AddedEntity { get; set; }
    
    public bool HasBreakingChanges => 
        ChangeKind == DiffKind.Removed ||
        FieldChanges.Any(f => f.IsBreaking) ||
        AssociationChanges.Any(a => a.ChangeKind == DiffKind.Removed);
}

/// <summary>
/// Diff for a field.
/// </summary>
public class FieldDiff
{
    public string FieldName { get; set; } = "";
    public string? OldFieldName { get; set; }  // For renames
    public DiffKind ChangeKind { get; set; }
    
    // Type changes
    public string? OldType { get; set; }
    public string? NewType { get; set; }
    
    // Nullability changes
    public bool? OldNullable { get; set; }
    public bool? NewNullable { get; set; }
    
    // From annotation @Migration.Transform
    public string? TransformExpression { get; set; }
    
    /// <summary>
    /// Whether this change is breaking.
    /// </summary>
    public bool IsBreaking => ChangeKind == DiffKind.Removed ||
                              (ChangeKind == DiffKind.Modified && 
                               (OldType != NewType || 
                                (OldNullable == true && NewNullable == false)));
}

/// <summary>
/// Diff for an association.
/// </summary>
public class AssociationDiff
{
    public string Name { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public string? OldTarget { get; set; }
    public string? NewTarget { get; set; }
}

/// <summary>
/// Diff for an index.
/// </summary>
public class IndexDiff
{
    public string Name { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public List<string> OldFields { get; } = new();
    public List<string> NewFields { get; } = new();
}

/// <summary>
/// Diff for a type definition.
/// </summary>
public class TypeDiff
{
    public string TypeName { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public string? OldBaseType { get; set; }
    public string? NewBaseType { get; set; }
    public List<FieldDiff> FieldChanges { get; } = new();
}

/// <summary>
/// Diff for an enum.
/// </summary>
public class EnumDiff
{
    public string EnumName { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public List<EnumValueDiff> ValueChanges { get; } = new();

    /// <summary>
    /// For added enums, stores the full enum definition for SQL generation.
    /// </summary>
    public BmEnum? AddedEnum { get; set; }

    public bool HasBreakingChanges =>
        ChangeKind == DiffKind.Removed ||
        ValueChanges.Any(v => v.ChangeKind == DiffKind.Removed);
}

/// <summary>
/// Diff for an enum value.
/// </summary>
public class EnumValueDiff
{
    public string Name { get; set; } = "";
    public DiffKind ChangeKind { get; set; }
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}
