using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Registry.Services;

/// <summary>
/// Detects changes between two BmModel versions.
/// Classifies changes as PATCH, MINOR, or MAJOR (breaking).
/// </summary>
public class ChangeDetector
{
    private readonly DefinitionHasher _hasher;

    public ChangeDetector(DefinitionHasher? hasher = null)
    {
        _hasher = hasher ?? new DefinitionHasher();
    }

    /// <summary>
    /// Detect all changes between existing and incoming model.
    /// </summary>
    public ChangeDetectionResult DetectChanges(BmModel? existing, BmModel incoming)
    {
        var result = new ChangeDetectionResult();

        if (existing == null)
        {
            // First installation - all entities are new
            foreach (var entity in incoming.Entities)
            {
                result.EntityChanges.Add(new EntityChange
                {
                    EntityName = entity.QualifiedName,
                    ChangeType = ObjectChangeType.Add,
                    Category = ChangeCategory.Minor,
                    IsBreaking = false,
                    Description = $"New entity: {entity.QualifiedName}"
                });
            }

            foreach (var enumDef in incoming.Enums)
            {
                result.EnumChanges.Add(new EnumChange
                {
                    EnumName = enumDef.QualifiedName,
                    ChangeType = ObjectChangeType.Add,
                    Category = ChangeCategory.Minor,
                    IsBreaking = false,
                    Description = $"New enum: {enumDef.QualifiedName}"
                });
            }

            return result;
        }

        // Compare entities
        DetectEntityChanges(existing, incoming, result);

        // Compare enums
        DetectEnumChanges(existing, incoming, result);

        // Compare types
        DetectTypeChanges(existing, incoming, result);

        return result;
    }

    private void DetectEntityChanges(BmModel existing, BmModel incoming, ChangeDetectionResult result)
    {
        var existingEntities = existing.Entities.ToDictionary(e => e.QualifiedName);
        var incomingEntities = incoming.Entities.ToDictionary(e => e.QualifiedName);

        // New entities
        foreach (var entity in incoming.Entities)
        {
            if (!existingEntities.ContainsKey(entity.QualifiedName))
            {
                result.EntityChanges.Add(new EntityChange
                {
                    EntityName = entity.QualifiedName,
                    ChangeType = ObjectChangeType.Add,
                    Category = ChangeCategory.Minor,
                    IsBreaking = false,
                    Description = $"New entity: {entity.QualifiedName}"
                });
            }
        }

        // Removed entities
        foreach (var entity in existing.Entities)
        {
            if (!incomingEntities.ContainsKey(entity.QualifiedName))
            {
                result.EntityChanges.Add(new EntityChange
                {
                    EntityName = entity.QualifiedName,
                    ChangeType = ObjectChangeType.Remove,
                    Category = ChangeCategory.Major,
                    IsBreaking = true,
                    Description = $"Removed entity: {entity.QualifiedName}"
                });
            }
        }

        // Modified entities
        foreach (var entity in incoming.Entities)
        {
            if (existingEntities.TryGetValue(entity.QualifiedName, out var existingEntity))
            {
                var entityChange = DetectEntityModifications(existingEntity, entity, result);
                if (entityChange != null)
                {
                    result.EntityChanges.Add(entityChange);
                }
            }
        }
    }

    private EntityChange? DetectEntityModifications(BmEntity existing, BmEntity incoming, ChangeDetectionResult result)
    {
        var existingHash = _hasher.HashEntity(existing);
        var incomingHash = _hasher.HashEntity(incoming);

        if (existingHash == incomingHash)
        {
            return null; // No changes
        }

        // Detect field-level changes
        DetectFieldChanges(existing, incoming, result);

        // Determine overall entity change category based on field changes
        var entityFieldChanges = result.FieldChanges
            .Where(f => f.EntityName == existing.QualifiedName)
            .ToList();

        var hasBreaking = entityFieldChanges.Any(f => f.IsBreaking);
        var category = hasBreaking ? ChangeCategory.Major : ChangeCategory.Minor;

        return new EntityChange
        {
            EntityName = existing.QualifiedName,
            ChangeType = ObjectChangeType.Modify,
            Category = category,
            IsBreaking = hasBreaking,
            OldHash = existingHash,
            NewHash = incomingHash,
            Description = $"Modified entity: {existing.QualifiedName} ({entityFieldChanges.Count} field changes)"
        };
    }

    private void DetectFieldChanges(BmEntity existing, BmEntity incoming, ChangeDetectionResult result)
    {
        var existingFields = existing.Fields.ToDictionary(f => f.Name);
        var incomingFields = incoming.Fields.ToDictionary(f => f.Name);

        // New fields
        foreach (var field in incoming.Fields)
        {
            if (!existingFields.ContainsKey(field.Name))
            {
                // Adding nullable field = MINOR, adding required field = MAJOR
                var isBreaking = !field.IsNullable;
                result.FieldChanges.Add(new FieldChange
                {
                    EntityName = existing.QualifiedName,
                    FieldName = field.Name,
                    ChangeType = ObjectChangeType.Add,
                    Category = isBreaking ? ChangeCategory.Major : ChangeCategory.Minor,
                    IsBreaking = isBreaking,
                    Description = isBreaking 
                        ? $"Added required field: {field.Name}" 
                        : $"Added optional field: {field.Name}"
                });
            }
        }

        // Removed fields
        foreach (var field in existing.Fields)
        {
            if (!incomingFields.ContainsKey(field.Name))
            {
                result.FieldChanges.Add(new FieldChange
                {
                    EntityName = existing.QualifiedName,
                    FieldName = field.Name,
                    ChangeType = ObjectChangeType.Remove,
                    Category = ChangeCategory.Major,
                    IsBreaking = true,
                    Description = $"Removed field: {field.Name}"
                });
            }
        }

        // Modified fields
        foreach (var field in incoming.Fields)
        {
            if (existingFields.TryGetValue(field.Name, out var existingField))
            {
                var changes = DetectFieldModifications(existing.QualifiedName, existingField, field);
                result.FieldChanges.AddRange(changes);
            }
        }
    }

    private List<FieldChange> DetectFieldModifications(string entityName, BmField existing, BmField incoming)
    {
        var changes = new List<FieldChange>();

        var existingHash = _hasher.HashField(existing);
        var incomingHash = _hasher.HashField(incoming);

        if (existingHash == incomingHash)
        {
            return changes; // No changes
        }

        // Type change
        var existingType = existing.TypeRef?.ToTypeString() ?? existing.TypeString;
        var incomingType = incoming.TypeRef?.ToTypeString() ?? incoming.TypeString;
        if (existingType != incomingType)
        {
            var isWideningChange = IsWideningTypeChange(existingType, incomingType);
            changes.Add(new FieldChange
            {
                EntityName = entityName,
                FieldName = existing.Name,
                ChangeType = ObjectChangeType.Modify,
                Category = isWideningChange ? ChangeCategory.Minor : ChangeCategory.Major,
                IsBreaking = !isWideningChange,
                OldValue = existingType,
                NewValue = incomingType,
                Description = isWideningChange 
                    ? $"Widened type: {existingType} → {incomingType}"
                    : $"Changed type: {existingType} → {incomingType} (BREAKING)"
            });
        }

        // Nullability change
        if (existing.IsNullable != incoming.IsNullable)
        {
            var madeRequired = existing.IsNullable && !incoming.IsNullable;
            changes.Add(new FieldChange
            {
                EntityName = entityName,
                FieldName = existing.Name,
                ChangeType = ObjectChangeType.Modify,
                Category = madeRequired ? ChangeCategory.Major : ChangeCategory.Minor,
                IsBreaking = madeRequired,
                OldValue = existing.IsNullable ? "nullable" : "required",
                NewValue = incoming.IsNullable ? "nullable" : "required",
                Description = madeRequired 
                    ? $"Made field required (BREAKING)" 
                    : $"Made field nullable"
            });
        }

        // Computed expression change
        if (existing.IsComputed && incoming.IsComputed)
        {
            var existingExprHash = existing.ComputedExpr != null ? _hasher.HashExpression(existing.ComputedExpr) : null;
            var incomingExprHash = incoming.ComputedExpr != null ? _hasher.HashExpression(incoming.ComputedExpr) : null;
            
            if (existingExprHash != incomingExprHash)
            {
                changes.Add(new FieldChange
                {
                    EntityName = entityName,
                    FieldName = existing.Name,
                    ChangeType = ObjectChangeType.Modify,
                    Category = ChangeCategory.Minor, // Computed field changes are behavioral, not schema-breaking
                    IsBreaking = false,
                    Description = $"Changed computed expression"
                });
            }
        }

        return changes;
    }

    private void DetectEnumChanges(BmModel existing, BmModel incoming, ChangeDetectionResult result)
    {
        var existingEnums = existing.Enums.ToDictionary(e => e.QualifiedName);
        var incomingEnums = incoming.Enums.ToDictionary(e => e.QualifiedName);

        // New enums
        foreach (var enumDef in incoming.Enums)
        {
            if (!existingEnums.ContainsKey(enumDef.QualifiedName))
            {
                result.EnumChanges.Add(new EnumChange
                {
                    EnumName = enumDef.QualifiedName,
                    ChangeType = ObjectChangeType.Add,
                    Category = ChangeCategory.Minor,
                    IsBreaking = false,
                    Description = $"New enum: {enumDef.QualifiedName}"
                });
            }
        }

        // Removed enums
        foreach (var enumDef in existing.Enums)
        {
            if (!incomingEnums.ContainsKey(enumDef.QualifiedName))
            {
                result.EnumChanges.Add(new EnumChange
                {
                    EnumName = enumDef.QualifiedName,
                    ChangeType = ObjectChangeType.Remove,
                    Category = ChangeCategory.Major,
                    IsBreaking = true,
                    Description = $"Removed enum: {enumDef.QualifiedName}"
                });
            }
        }

        // Modified enums
        foreach (var enumDef in incoming.Enums)
        {
            if (existingEnums.TryGetValue(enumDef.QualifiedName, out var existingEnum))
            {
                var existingHash = _hasher.HashEnum(existingEnum);
                var incomingHash = _hasher.HashEnum(enumDef);

                if (existingHash != incomingHash)
                {
                    var existingValues = existingEnum.Values.Select(v => v.Name).ToHashSet();
                    var incomingValues = enumDef.Values.Select(v => v.Name).ToHashSet();
                    
                    var removed = existingValues.Except(incomingValues).Any();
                    
                    result.EnumChanges.Add(new EnumChange
                    {
                        EnumName = enumDef.QualifiedName,
                        ChangeType = ObjectChangeType.Modify,
                        Category = removed ? ChangeCategory.Major : ChangeCategory.Minor,
                        IsBreaking = removed,
                        OldHash = existingHash,
                        NewHash = incomingHash,
                        Description = removed 
                            ? $"Modified enum with removed values (BREAKING)"
                            : $"Modified enum (added values)"
                    });
                }
            }
        }
    }

    private void DetectTypeChanges(BmModel existing, BmModel incoming, ChangeDetectionResult result)
    {
        var existingTypes = existing.Types.ToDictionary(t => t.QualifiedName);
        var incomingTypes = incoming.Types.ToDictionary(t => t.QualifiedName);

        // New types
        foreach (var typeDef in incoming.Types)
        {
            if (!existingTypes.ContainsKey(typeDef.QualifiedName))
            {
                result.TypeChanges.Add(new TypeChange
                {
                    TypeName = typeDef.QualifiedName,
                    ChangeType = ObjectChangeType.Add,
                    Category = ChangeCategory.Minor,
                    IsBreaking = false,
                    Description = $"New type: {typeDef.QualifiedName}"
                });
            }
        }

        // Removed types
        foreach (var typeDef in existing.Types)
        {
            if (!incomingTypes.ContainsKey(typeDef.QualifiedName))
            {
                result.TypeChanges.Add(new TypeChange
                {
                    TypeName = typeDef.QualifiedName,
                    ChangeType = ObjectChangeType.Remove,
                    Category = ChangeCategory.Major,
                    IsBreaking = true,
                    Description = $"Removed type: {typeDef.QualifiedName}"
                });
            }
        }

        // Modified types
        foreach (var typeDef in incoming.Types)
        {
            if (existingTypes.TryGetValue(typeDef.QualifiedName, out var existingType))
            {
                var existingHash = _hasher.HashType(existingType);
                var incomingHash = _hasher.HashType(typeDef);

                if (existingHash != incomingHash)
                {
                    result.TypeChanges.Add(new TypeChange
                    {
                        TypeName = typeDef.QualifiedName,
                        ChangeType = ObjectChangeType.Modify,
                        Category = ChangeCategory.Major, // Type changes are usually breaking
                        IsBreaking = true,
                        OldHash = existingHash,
                        NewHash = incomingHash,
                        Description = $"Modified type: {typeDef.QualifiedName}"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Check if type change is widening (safe) or narrowing (breaking).
    /// </summary>
    private bool IsWideningTypeChange(string from, string to)
    {
        // String length widening
        if (from.StartsWith("String(") && to.StartsWith("String("))
        {
            var fromLen = int.Parse(from.Replace("String(", "").Replace(")", ""));
            var toLen = int.Parse(to.Replace("String(", "").Replace(")", ""));
            return toLen > fromLen;
        }

        // Int to Long widening
        if (from == "Int" && to == "Long") return true;
        if (from == "Integer" && to == "Long") return true;
        
        // Float to Double widening
        if (from == "Float" && to == "Double") return true;

        // Decimal precision widening (simplified)
        if (from.StartsWith("Decimal(") && to.StartsWith("Decimal("))
        {
            // For simplicity, assume any decimal change could be breaking
            return false;
        }

        return false;
    }
}

#region Result Types

public class ChangeDetectionResult
{
    public List<EntityChange> EntityChanges { get; } = new();
    public List<FieldChange> FieldChanges { get; } = new();
    public List<EnumChange> EnumChanges { get; } = new();
    public List<TypeChange> TypeChanges { get; } = new();

    public bool HasBreakingChanges => 
        EntityChanges.Any(c => c.IsBreaking) ||
        FieldChanges.Any(c => c.IsBreaking) ||
        EnumChanges.Any(c => c.IsBreaking) ||
        TypeChanges.Any(c => c.IsBreaking);

    public int TotalChanges => 
        EntityChanges.Count + FieldChanges.Count + EnumChanges.Count + TypeChanges.Count;

    public ChangeCategory OverallCategory => 
        HasBreakingChanges ? ChangeCategory.Major : 
        TotalChanges > 0 ? ChangeCategory.Minor : 
        ChangeCategory.Patch;

    /// <summary>
    /// Get all breaking changes for approval.
    /// </summary>
    public List<IObjectChange> GetBreakingChanges()
    {
        var result = new List<IObjectChange>();
        result.AddRange(EntityChanges.Where(c => c.IsBreaking));
        result.AddRange(FieldChanges.Where(c => c.IsBreaking));
        result.AddRange(EnumChanges.Where(c => c.IsBreaking));
        result.AddRange(TypeChanges.Where(c => c.IsBreaking));
        return result;
    }
}

public interface IObjectChange
{
    ObjectChangeType ChangeType { get; }
    ChangeCategory Category { get; }
    bool IsBreaking { get; }
    string Description { get; }
}

public enum ObjectChangeType { Add, Modify, Remove }

public class EntityChange : IObjectChange
{
    public string EntityName { get; set; } = "";
    public ObjectChangeType ChangeType { get; set; }
    public ChangeCategory Category { get; set; }
    public bool IsBreaking { get; set; }
    public string? OldHash { get; set; }
    public string? NewHash { get; set; }
    public string Description { get; set; } = "";
}

public class FieldChange : IObjectChange
{
    public string EntityName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public ObjectChangeType ChangeType { get; set; }
    public ChangeCategory Category { get; set; }
    public bool IsBreaking { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Description { get; set; } = "";
}

public class EnumChange : IObjectChange
{
    public string EnumName { get; set; } = "";
    public ObjectChangeType ChangeType { get; set; }
    public ChangeCategory Category { get; set; }
    public bool IsBreaking { get; set; }
    public string? OldHash { get; set; }
    public string? NewHash { get; set; }
    public string Description { get; set; } = "";
}

public class TypeChange : IObjectChange
{
    public string TypeName { get; set; } = "";
    public ObjectChangeType ChangeType { get; set; }
    public ChangeCategory Category { get; set; }
    public bool IsBreaking { get; set; }
    public string? OldHash { get; set; }
    public string? NewHash { get; set; }
    public string Description { get; set; } = "";
}

#endregion
