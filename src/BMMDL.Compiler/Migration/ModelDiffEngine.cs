using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Abstractions;

namespace BMMDL.Compiler.Migration;

/// <summary>
/// Computes the difference between two BMMDL model versions.
/// </summary>
public class ModelDiffEngine
{
    /// <summary>
    /// Compute diff between old and new model.
    /// </summary>
    public ModelDiff ComputeDiff(BmModel oldModel, BmModel newModel, string fromVersion = "", string toVersion = "")
    {
        // Extract version from @Version annotation if not provided
        var oldVersion = fromVersion;
        var newVersion = toVersion;
        if (string.IsNullOrEmpty(oldVersion))
        {
            oldVersion = oldModel.Namespace ?? "1.0.0";
        }
        if (string.IsNullOrEmpty(newVersion))
        {
            newVersion = newModel.Namespace ?? "1.1.0";
        }
        
        var diff = new ModelDiff
        {
            FromVersion = oldVersion,
            ToVersion = newVersion,
            Namespace = newModel.Namespace ?? ""
        };
        
        // Compare entities
        ComputeEntityDiffs(oldModel, newModel, diff);
        
        // Compare types
        ComputeTypeDiffs(oldModel, newModel, diff);
        
        // Compare enums
        ComputeEnumDiffs(oldModel, newModel, diff);
        
        // Compute overall change type
        diff.ComputeOverallChangeType();
        
        return diff;
    }
    
    private void ComputeEntityDiffs(BmModel oldModel, BmModel newModel, ModelDiff diff)
    {
        var oldEntities = oldModel.Entities.ToDictionary(e => e.QualifiedName);
        var newEntities = newModel.Entities.ToDictionary(e => e.QualifiedName);
        
        // Find added and modified entities
        foreach (var (name, newEntity) in newEntities)
        {
            if (!oldEntities.TryGetValue(name, out var oldEntity))
            {
                // New entity - store full entity for DSL generation
                diff.EntityChanges.Add(new EntityDiff
                {
                    EntityName = name,
                    ChangeKind = DiffKind.Added,
                    AddedEntity = newEntity
                });
            }
            else
            {
                // Compare existing entity
                var entityDiff = ComputeEntityDiff(oldEntity, newEntity);
                if (entityDiff.ChangeKind != DiffKind.Unchanged)
                {
                    diff.EntityChanges.Add(entityDiff);
                }
            }
        }
        
        // Find removed entities
        foreach (var (name, _) in oldEntities)
        {
            if (!newEntities.ContainsKey(name))
            {
                diff.EntityChanges.Add(new EntityDiff
                {
                    EntityName = name,
                    ChangeKind = DiffKind.Removed
                });
            }
        }
    }
    
    private EntityDiff ComputeEntityDiff(BmEntity oldEntity, BmEntity newEntity)
    {
        var entityDiff = new EntityDiff
        {
            EntityName = newEntity.QualifiedName,
            ChangeKind = DiffKind.Unchanged
        };
        
        // Compare fields
        ComputeFieldDiffs(oldEntity.Fields, newEntity.Fields, entityDiff.FieldChanges);
        
        // Compare associations
        ComputeAssociationDiffs(oldEntity.Associations, newEntity.Associations, entityDiff.AssociationChanges);
        
        // Compare indexes
        ComputeIndexDiffs(oldEntity.Indexes, newEntity.Indexes, entityDiff.IndexChanges);
        
        // Mark as modified if any changes
        if (entityDiff.FieldChanges.Count > 0 || 
            entityDiff.AssociationChanges.Count > 0 ||
            entityDiff.IndexChanges.Count > 0)
        {
            entityDiff.ChangeKind = DiffKind.Modified;
        }
        
        return entityDiff;
    }
    
    private void ComputeFieldDiffs(List<BmField> oldFields, List<BmField> newFields, List<FieldDiff> result)
    {
        var oldByName = oldFields.ToDictionary(f => f.Name);
        var newByName = newFields.ToDictionary(f => f.Name);
        
        // Check for renames via @Migration.RenamedFrom annotation
        var renamedFields = new HashSet<string>();
        foreach (var newField in newFields)
        {
            var renamedFrom = newField.GetAnnotation("Migration.RenamedFrom");
            if (renamedFrom?.Value is string oldName && oldByName.ContainsKey(oldName))
            {
                result.Add(new FieldDiff
                {
                    FieldName = newField.Name,
                    OldFieldName = oldName,
                    ChangeKind = DiffKind.Renamed,
                    OldType = oldByName[oldName].TypeString,
                    NewType = newField.TypeString,
                    TransformExpression = GetTransformExpression(newField)
                });
                renamedFields.Add(oldName);
                renamedFields.Add(newField.Name);
            }
        }
        
        // Find added and modified fields
        foreach (var (name, newField) in newByName)
        {
            if (renamedFields.Contains(name)) continue;
            
            if (!oldByName.TryGetValue(name, out var oldField))
            {
                result.Add(new FieldDiff
                {
                    FieldName = name,
                    ChangeKind = DiffKind.Added,
                    NewType = newField.TypeString,
                    NewNullable = newField.IsNullable
                });
            }
            else
            {
                // Check for modifications
                var fieldDiff = CompareFields(oldField, newField);
                if (fieldDiff != null)
                {
                    result.Add(fieldDiff);
                }
            }
        }
        
        // Find removed fields
        foreach (var (name, oldField) in oldByName)
        {
            if (renamedFields.Contains(name)) continue;
            
            if (!newByName.ContainsKey(name))
            {
                result.Add(new FieldDiff
                {
                    FieldName = name,
                    ChangeKind = DiffKind.Removed,
                    OldType = oldField.TypeString,
                    OldNullable = oldField.IsNullable
                });
            }
        }
    }
    
    private FieldDiff? CompareFields(BmField oldField, BmField newField)
    {
        bool typeChanged = oldField.TypeString != newField.TypeString;
        bool nullableChanged = oldField.IsNullable != newField.IsNullable;
        
        if (!typeChanged && !nullableChanged)
            return null;
        
        return new FieldDiff
        {
            FieldName = newField.Name,
            ChangeKind = DiffKind.Modified,
            OldType = oldField.TypeString,
            NewType = newField.TypeString,
            OldNullable = oldField.IsNullable,
            NewNullable = newField.IsNullable,
            TransformExpression = GetTransformExpression(newField)
        };
    }
    
    private string? GetTransformExpression(BmField field)
    {
        var transform = field.GetAnnotation("Migration.Transform");
        return transform?.Value as string;
    }
    
    private void ComputeAssociationDiffs(List<BmAssociation> oldAssocs, List<BmAssociation> newAssocs, List<AssociationDiff> result)
    {
        var oldByName = oldAssocs.ToDictionary(a => a.Name);
        var newByName = newAssocs.ToDictionary(a => a.Name);
        
        foreach (var (name, newAssoc) in newByName)
        {
            if (!oldByName.TryGetValue(name, out var oldAssoc))
            {
                result.Add(new AssociationDiff { Name = name, ChangeKind = DiffKind.Added, NewTarget = newAssoc.TargetEntity });
            }
            else if (oldAssoc.TargetEntity != newAssoc.TargetEntity)
            {
                result.Add(new AssociationDiff { Name = name, ChangeKind = DiffKind.Modified, OldTarget = oldAssoc.TargetEntity, NewTarget = newAssoc.TargetEntity });
            }
        }
        
        foreach (var (name, oldAssoc) in oldByName)
        {
            if (!newByName.ContainsKey(name))
            {
                result.Add(new AssociationDiff { Name = name, ChangeKind = DiffKind.Removed, OldTarget = oldAssoc.TargetEntity });
            }
        }
    }
    
    private void ComputeIndexDiffs(List<BmIndex> oldIndexes, List<BmIndex> newIndexes, List<IndexDiff> result)
    {
        var oldByName = oldIndexes.ToDictionary(i => i.Name);
        var newByName = newIndexes.ToDictionary(i => i.Name);
        
        foreach (var (name, newIndex) in newByName)
        {
            if (!oldByName.TryGetValue(name, out var oldIndex))
            {
                var indexDiff = new IndexDiff { Name = name, ChangeKind = DiffKind.Added };
                indexDiff.NewFields.AddRange(newIndex.Fields);
                result.Add(indexDiff);
            }
            else if (!oldIndex.Fields.SequenceEqual(newIndex.Fields) || oldIndex.IsUnique != newIndex.IsUnique)
            {
                var indexDiff = new IndexDiff { Name = name, ChangeKind = DiffKind.Modified };
                indexDiff.OldFields.AddRange(oldIndex.Fields);
                indexDiff.NewFields.AddRange(newIndex.Fields);
                result.Add(indexDiff);
            }
        }
        
        foreach (var (name, oldIndex) in oldByName)
        {
            if (!newByName.ContainsKey(name))
            {
                var indexDiff = new IndexDiff { Name = name, ChangeKind = DiffKind.Removed };
                indexDiff.OldFields.AddRange(oldIndex.Fields);
                result.Add(indexDiff);
            }
        }
    }
    
    private void ComputeTypeDiffs(BmModel oldModel, BmModel newModel, ModelDiff diff)
    {
        var oldTypes = oldModel.Types.ToDictionary(t => t.QualifiedName);
        var newTypes = newModel.Types.ToDictionary(t => t.QualifiedName);
        
        foreach (var (name, newType) in newTypes)
        {
            if (!oldTypes.TryGetValue(name, out var oldType))
            {
                diff.TypeChanges.Add(new TypeDiff { TypeName = name, ChangeKind = DiffKind.Added });
            }
            else if (oldType.BaseType != newType.BaseType)
            {
                diff.TypeChanges.Add(new TypeDiff 
                { 
                    TypeName = name, 
                    ChangeKind = DiffKind.Modified,
                    OldBaseType = oldType.BaseType,
                    NewBaseType = newType.BaseType
                });
            }
        }
        
        foreach (var (name, _) in oldTypes)
        {
            if (!newTypes.ContainsKey(name))
            {
                diff.TypeChanges.Add(new TypeDiff { TypeName = name, ChangeKind = DiffKind.Removed });
            }
        }
    }
    
    private void ComputeEnumDiffs(BmModel oldModel, BmModel newModel, ModelDiff diff)
    {
        var oldEnums = oldModel.Enums.ToDictionary(e => e.QualifiedName);
        var newEnums = newModel.Enums.ToDictionary(e => e.QualifiedName);
        
        foreach (var (name, newEnum) in newEnums)
        {
            if (!oldEnums.TryGetValue(name, out var oldEnum))
            {
                diff.EnumChanges.Add(new EnumDiff
                {
                    EnumName = name,
                    ChangeKind = DiffKind.Added,
                    AddedEnum = newEnum
                });
            }
            else
            {
                var enumDiff = ComputeEnumValueDiffs(oldEnum, newEnum);
                if (enumDiff.ChangeKind != DiffKind.Unchanged)
                {
                    diff.EnumChanges.Add(enumDiff);
                }
            }
        }
        
        foreach (var (name, _) in oldEnums)
        {
            if (!newEnums.ContainsKey(name))
            {
                diff.EnumChanges.Add(new EnumDiff { EnumName = name, ChangeKind = DiffKind.Removed });
            }
        }
    }
    
    private EnumDiff ComputeEnumValueDiffs(BmEnum oldEnum, BmEnum newEnum)
    {
        var enumDiff = new EnumDiff { EnumName = newEnum.QualifiedName, ChangeKind = DiffKind.Unchanged };
        
        var oldValues = oldEnum.Values.ToDictionary(v => v.Name);
        var newValues = newEnum.Values.ToDictionary(v => v.Name);
        
        foreach (var (name, newVal) in newValues)
        {
            if (!oldValues.TryGetValue(name, out var oldVal))
            {
                enumDiff.ValueChanges.Add(new EnumValueDiff { Name = name, ChangeKind = DiffKind.Added, NewValue = newVal.Value });
            }
            else if (!Equals(oldVal.Value, newVal.Value))
            {
                enumDiff.ValueChanges.Add(new EnumValueDiff { Name = name, ChangeKind = DiffKind.Modified, OldValue = oldVal.Value, NewValue = newVal.Value });
            }
        }
        
        foreach (var (name, oldVal) in oldValues)
        {
            if (!newValues.ContainsKey(name))
            {
                enumDiff.ValueChanges.Add(new EnumValueDiff { Name = name, ChangeKind = DiffKind.Removed, OldValue = oldVal.Value });
            }
        }
        
        if (enumDiff.ValueChanges.Count > 0)
            enumDiff.ChangeKind = DiffKind.Modified;
        
        return enumDiff;
    }
}
