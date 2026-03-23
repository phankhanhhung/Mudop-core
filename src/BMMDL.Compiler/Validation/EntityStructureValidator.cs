using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates entity structure: key fields, field uniqueness, computed field expressions,
/// association targets, and entity-level constraints.
/// </summary>
public class EntityStructureValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            count++;

            // Must have at least one field or composition
            if (entity.Fields.Count == 0 && entity.Compositions.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_ENTITY_NO_FIELDS, $"Entity '{entity.Name}' has no fields", entity.SourceFile, entity.StartLine, PassName);
            }

            // Must have key field(s) — check own fields and inherited keys from parent chain
            var keyFields = entity.Fields.Where(f => f.IsKey).ToList();
            if (keyFields.Count == 0 && !HasKeyInParentChain(entity, model))
            {
                context.AddWarning(ErrorCodes.SEM_ENTITY_NO_KEY, $"Entity '{entity.Name}' has no key field", entity.SourceFile, entity.StartLine, PassName);
            }

            // Validate field names are unique
            var fieldNames = entity.Fields.Select(f => f.Name).ToList();
            var duplicates = fieldNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var dup in duplicates)
            {
                context.AddError(ErrorCodes.SEM_DUPLICATE_FIELD, $"Duplicate field '{dup}' in entity '{entity.Name}'", entity.SourceFile, entity.StartLine, PassName);
            }
            count++;

            // Validate computed fields have expressions
            foreach (var field in entity.Fields.Where(f => f.IsComputed))
            {
                if (string.IsNullOrEmpty(field.ComputedExprString) && field.ComputedExpr == null)
                {
                    context.AddError(ErrorCodes.SEM_COMPUTED_NO_EXPR, $"Computed field '{field.Name}' in '{entity.Name}' has no expression", entity.SourceFile, field.StartLine, PassName);
                }
                count++;
            }

            // Validate associations have targets
            foreach (var assoc in entity.Associations)
            {
                if (string.IsNullOrEmpty(assoc.TargetEntity))
                {
                    context.AddError(ErrorCodes.SEM_ASSOC_NO_TARGET, $"Association '{assoc.Name}' in '{entity.Name}' has no target", entity.SourceFile, assoc.StartLine, PassName);
                }
                count++;
            }

            // V5: Warn if child entity redefines fields from parent
            if (!string.IsNullOrEmpty(entity.ParentEntityName))
            {
                var parentFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectParentFields(entity, model, parentFields);
                foreach (var field in entity.Fields)
                {
                    if (parentFields.Contains(field.Name))
                    {
                        var parentName = entity.ParentEntityName ?? "parent";
                        context.AddWarning(ErrorCodes.SEM_DUPLICATE_FIELD,
                            $"Entity '{entity.Name}' redefines inherited field '{field.Name}' from parent '{parentName}'",
                            entity.SourceFile, field.StartLine, PassName);
                    }
                }
                count++;
            }
        }

        return count;
    }

    private static bool HasKeyInParentChain(BmEntity entity, BmModel model)
    {
        var current = entity;
        var visited = new HashSet<string>();
        while (current != null)
        {
            if (!visited.Add(current.QualifiedName)) break;
            if (current.Fields.Any(f => f.IsKey)) return true;
            if (string.IsNullOrEmpty(current.ParentEntityName)) break;
            current = model.FindEntity(current.ParentEntityName);
        }
        return false;
    }

    /// <summary>
    /// V5: Collect all field names from the parent chain (excluding the entity itself).
    /// Used to detect inherited field redefinition.
    /// </summary>
    private static void CollectParentFields(BmEntity entity, BmModel model, HashSet<string> parentFields)
    {
        var visited = new HashSet<string> { entity.QualifiedName };
        var current = string.IsNullOrEmpty(entity.ParentEntityName) ? null : model.FindEntity(entity.ParentEntityName);

        while (current != null)
        {
            if (!visited.Add(current.QualifiedName)) break; // Circular protection
            foreach (var field in current.Fields)
            {
                parentFields.Add(field.Name);
            }
            current = string.IsNullOrEmpty(current.ParentEntityName) ? null : model.FindEntity(current.ParentEntityName);
        }
    }
}
