using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 5.6: Modification
/// Applies "modify entity/type" definitions by executing field-level actions
/// (remove, rename, change type, add, modify) on the target entity.
/// </summary>
public class ModificationPass : ICompilerPass
{
    public string Name => "Modification";
    public string Description => "Apply modify definitions to target entities";
    public int Order => 56; // After ExtensionMerge (55), before Optimization (60)

    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.MOD_BUILD_ERROR, "No model available for modification", pass: Name);
            return false;
        }

        var model = context.Model;
        int applied = 0;

        foreach (var mod in model.Modifications)
        {
            switch (mod.TargetKind)
            {
                case "entity":
                    if (ApplyToEntity(mod, model, context))
                        applied++;
                    break;
                case "type":
                    if (ApplyToType(mod, model, context))
                        applied++;
                    break;
                case "aspect":
                    if (ApplyToAspect(mod, model, context))
                        applied++;
                    break;
                case "enum":
                    if (ApplyToEnum(mod, model, context))
                        applied++;
                    break;
                case "service":
                    if (ApplyToService(mod, model, context))
                        applied++;
                    break;
                default:
                    context.AddWarning(ErrorCodes.MOD_NO_ELEMENTS,
                        $"Modification target kind '{mod.TargetKind}' not yet supported", Name);
                    break;
            }
        }

        if (applied > 0)
            context.AddInfo(ErrorCodes.MOD_SUMMARY, $"Applied {applied} modification definitions", Name);

        return true;
    }

    private bool ApplyToEntity(BmModification mod, BmModel model, CompilationContext context)
    {
        var target = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, mod.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, mod.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.MOD_TARGET_NOT_FOUND,
                $"Modification target entity '{mod.TargetName}' not found", Name);
            return false;
        }

        foreach (var action in mod.Actions)
        {
            ApplyAction(action, target, model, context);
        }

        return true;
    }

    private bool ApplyToType(BmModification mod, BmModel model, CompilationContext context)
    {
        var target = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, mod.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.QualifiedName, mod.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.MOD_TARGET_NOT_FOUND,
                $"Modification target type '{mod.TargetName}' not found", Name);
            return false;
        }

        foreach (var action in mod.Actions)
        {
            // Types don't have associations, so pass null entity
            ApplyAction(action, target.Fields, null, model, mod.TargetName, context);
        }

        return true;
    }

    private bool ApplyToAspect(BmModification mod, BmModel model, CompilationContext context)
    {
        var target = model.Aspects.FirstOrDefault(a =>
            string.Equals(a.Name, mod.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.QualifiedName, mod.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.MOD_TARGET_NOT_FOUND,
                $"Modification target aspect '{mod.TargetName}' not found", Name);
            return false;
        }

        foreach (var action in mod.Actions)
        {
            // Aspects have fields and associations, pass null entity for non-entity context
            ApplyAction(action, target.Fields, null, model, mod.TargetName, context);
        }

        return true;
    }

    private bool ApplyToEnum(BmModification mod, BmModel model, CompilationContext context)
    {
        var target = model.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, mod.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, mod.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.MOD_TARGET_NOT_FOUND,
                $"Modification target enum '{mod.TargetName}' not found", Name);
            return false;
        }

        foreach (var action in mod.Actions)
        {
            ApplyEnumAction(action, target, context);
        }

        return true;
    }

    private void ApplyEnumAction(BmModifyAction action, BmEnum target, CompilationContext context)
    {
        switch (action)
        {
            case BmAddEnumMemberAction add:
            {
                if (target.Values.Any(v => string.Equals(v.Name, add.Member.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    context.AddWarning(ErrorCodes.MOD_ENUM_MEMBER_EXISTS,
                        $"Enum member '{add.Member.Name}' already exists in enum '{target.Name}', skipping add", Name);
                    return;
                }
                target.Values.Add(add.Member);
                break;
            }

            case BmRemoveEnumMemberAction remove:
            {
                var member = target.Values.FirstOrDefault(v =>
                    string.Equals(v.Name, remove.MemberName, StringComparison.OrdinalIgnoreCase));
                if (member == null)
                {
                    context.AddError(ErrorCodes.MOD_ENUM_MEMBER_NOT_FOUND,
                        $"Enum member '{remove.MemberName}' not found in enum '{target.Name}' for removal", Name);
                    return;
                }
                target.Values.Remove(member);
                break;
            }

            case BmRemoveFieldAction removeField:
            {
                // remove IDENTIFIER; in modify enum context — treat as enum member removal
                var member = target.Values.FirstOrDefault(v =>
                    string.Equals(v.Name, removeField.FieldName, StringComparison.OrdinalIgnoreCase));
                if (member == null)
                {
                    context.AddError(ErrorCodes.MOD_ENUM_MEMBER_NOT_FOUND,
                        $"Enum member '{removeField.FieldName}' not found in enum '{target.Name}' for removal", Name);
                    return;
                }
                target.Values.Remove(member);
                break;
            }

            default:
                context.AddWarning(ErrorCodes.MOD_UNSUPPORTED_ACTION,
                    $"Modify action type '{action.GetType().Name}' is not supported for enums", Name);
                break;
        }
    }

    private bool ApplyToService(BmModification mod, BmModel model, CompilationContext context)
    {
        var target = model.Services.FirstOrDefault(s =>
            string.Equals(s.Name, mod.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.QualifiedName, mod.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.MOD_TARGET_NOT_FOUND,
                $"Modification target service '{mod.TargetName}' not found", Name);
            return false;
        }

        foreach (var action in mod.Actions)
        {
            ApplyServiceAction(action, target, context);
        }

        return true;
    }

    private void ApplyServiceAction(BmModifyAction action, BmService target, CompilationContext context)
    {
        switch (action)
        {
            case BmRemoveFieldAction remove:
            {
                // Remove entity exposure, action, or function by name
                var entity = target.Entities.FirstOrDefault(e =>
                    string.Equals(e.Name, remove.FieldName, StringComparison.OrdinalIgnoreCase));
                if (entity != null)
                {
                    target.Entities.Remove(entity);
                    return;
                }

                var svcAction = target.Actions.FirstOrDefault(a =>
                    string.Equals(a.Name, remove.FieldName, StringComparison.OrdinalIgnoreCase));
                if (svcAction != null)
                {
                    target.Actions.Remove(svcAction);
                    return;
                }

                var function = target.Functions.FirstOrDefault(f =>
                    string.Equals(f.Name, remove.FieldName, StringComparison.OrdinalIgnoreCase));
                if (function != null)
                {
                    target.Functions.Remove(function);
                    return;
                }

                context.AddError(ErrorCodes.MOD_FIELD_NOT_FOUND,
                    $"Element '{remove.FieldName}' not found in service '{target.Name}' for removal", Name);
                break;
            }

            case BmRenameFieldAction rename:
            {
                // Rename entity exposure, action, or function
                var entity = target.Entities.FirstOrDefault(e =>
                    string.Equals(e.Name, rename.OldName, StringComparison.OrdinalIgnoreCase));
                if (entity != null)
                {
                    entity.Name = rename.NewName;
                    return;
                }

                var svcAction = target.Actions.FirstOrDefault(a =>
                    string.Equals(a.Name, rename.OldName, StringComparison.OrdinalIgnoreCase));
                if (svcAction != null)
                {
                    svcAction.Name = rename.NewName;
                    return;
                }

                var function = target.Functions.FirstOrDefault(f =>
                    string.Equals(f.Name, rename.OldName, StringComparison.OrdinalIgnoreCase));
                if (function != null)
                {
                    function.Name = rename.NewName;
                    return;
                }

                context.AddError(ErrorCodes.MOD_RENAME_NOT_FOUND,
                    $"Element '{rename.OldName}' not found in service '{target.Name}' for renaming", Name);
                break;
            }

            default:
                context.AddWarning(ErrorCodes.MOD_UNSUPPORTED_ACTION,
                    $"Modify action type '{action.GetType().Name}' is not supported for services", Name);
                break;
        }
    }

    private void ApplyAction(BmModifyAction action, BmEntity entity, BmModel model, CompilationContext context)
    {
        ApplyAction(action, entity.Fields, entity, model, entity.Name, context);
    }

    private void ApplyAction(BmModifyAction action, List<BmField> fields, BmEntity? entity, BmModel model, string targetName, CompilationContext context)
    {
        switch (action)
        {
            case BmRemoveFieldAction remove:
            {
                var field = fields.FirstOrDefault(f =>
                    string.Equals(f.Name, remove.FieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                {
                    context.AddError(ErrorCodes.MOD_FIELD_NOT_FOUND,
                        $"Field '{remove.FieldName}' not found in '{targetName}' for removal", Name);
                    return;
                }

                // Safety: cannot remove key fields
                if (field.IsKey)
                {
                    context.AddError(ErrorCodes.MOD_CANNOT_REMOVE_KEY,
                        $"Cannot remove key field '{field.Name}' from entity '{targetName}'", Name);
                    return;
                }

                // Safety: cannot remove fields referenced by associations
                if (entity != null)
                {
                    var referencingAssoc = FindReferencingAssociation(field.Name, entity, model);
                    if (referencingAssoc != null)
                    {
                        context.AddError(ErrorCodes.MOD_FIELD_IN_USE,
                            $"Cannot remove field '{field.Name}' — it is referenced by association '{referencingAssoc}'", Name);
                        return;
                    }
                }

                fields.Remove(field);
                break;
            }

            case BmRenameFieldAction rename:
            {
                var field = fields.FirstOrDefault(f =>
                    string.Equals(f.Name, rename.OldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                {
                    context.AddError(ErrorCodes.MOD_RENAME_NOT_FOUND,
                        $"Field '{rename.OldName}' not found in '{targetName}' for renaming", Name);
                    return;
                }
                if (fields.Any(f => string.Equals(f.Name, rename.NewName, StringComparison.OrdinalIgnoreCase)))
                {
                    context.AddError(ErrorCodes.MOD_RENAME_CONFLICT,
                        $"Cannot rename '{rename.OldName}' to '{rename.NewName}' in '{targetName}': name already exists", Name);
                    return;
                }

                var oldName = field.Name;
                field.Name = rename.NewName;

                // Update references in rules, access controls, and computed fields
                UpdateFieldReferences(model, targetName, oldName, rename.NewName);
                break;
            }

            case BmChangeTypeAction changeType:
            {
                var field = fields.FirstOrDefault(f =>
                    string.Equals(f.Name, changeType.FieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                {
                    context.AddError(ErrorCodes.MOD_CHANGE_TYPE_NOT_FOUND,
                        $"Field '{changeType.FieldName}' not found in '{targetName}' for type change", Name);
                    return;
                }

                // Check type compatibility and emit warning for unsafe changes
                if (!IsTypeChangeSafe(field.TypeString, changeType.NewTypeString))
                {
                    context.AddWarning(ErrorCodes.MOD_TYPE_INCOMPATIBLE,
                        $"Type change from '{field.TypeString}' to '{changeType.NewTypeString}' for field '{changeType.FieldName}' may cause data loss", Name);
                }

                field.TypeString = changeType.NewTypeString;
                break;
            }

            case BmAddFieldAction add:
            {
                if (fields.Any(f => string.Equals(f.Name, add.Field.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    context.AddWarning(ErrorCodes.MOD_FIELD_EXISTS,
                        $"Field '{add.Field.Name}' already exists in '{targetName}', skipping add", Name);
                    return;
                }
                fields.Add(add.Field);
                break;
            }

            case BmModifyFieldAction modify:
            {
                var field = fields.FirstOrDefault(f =>
                    string.Equals(f.Name, modify.FieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                {
                    context.AddError(ErrorCodes.MOD_MODIFY_NOT_FOUND,
                        $"Field '{modify.FieldName}' not found in '{targetName}' for modification", Name);
                    return;
                }
                if (modify.NewTypeString != null)
                    field.TypeString = modify.NewTypeString;
                if (modify.NewDefaultValueString != null)
                {
                    field.DefaultValueString = modify.NewDefaultValueString;
                    field.DefaultExpr = null; // Clear stale AST — will be re-parsed if needed
                }
                foreach (var ann in modify.Annotations)
                    field.Annotations.Add(ann);
                break;
            }
        }
    }

    /// <summary>
    /// Check if a field is referenced as an FK target by any association in the model.
    /// Returns the association name if found, null otherwise.
    /// </summary>
    private static string? FindReferencingAssociation(string fieldName, BmEntity entity, BmModel model)
    {
        // Check associations on other entities that target this entity
        // Convention: FK field name = TargetEntity + "Id" (e.g., Customer → CustomerId)
        // Also check explicit on-conditions that reference this field
        foreach (var otherEntity in model.Entities)
        {
            foreach (var assoc in otherEntity.Associations)
            {
                if (!string.Equals(assoc.TargetEntity, entity.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(assoc.TargetEntity, entity.QualifiedName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if the on-condition AST references this field
                if (assoc.OnConditionExpr != null && ExpressionReferencesField(assoc.OnConditionExpr, fieldName))
                    return assoc.Name;
            }
        }

        // Also check associations on this entity itself that reference own fields via on-condition
        foreach (var assoc in entity.Associations)
        {
            if (assoc.OnConditionExpr != null && ExpressionReferencesField(assoc.OnConditionExpr, fieldName))
                return assoc.Name;
        }

        return null;
    }

    /// <summary>
    /// Recursively check if an expression references a field name.
    /// </summary>
    private static bool ExpressionReferencesField(BmExpression expr, string fieldName)
    {
        return BmExpressionWalker.Any(expr, e =>
            e is BmIdentifierExpression id &&
            id.Path.Any(p => string.Equals(p, fieldName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Check if a type change is safe (widening) or potentially lossy (narrowing/incompatible).
    /// </summary>
    public static bool IsTypeChangeSafe(string oldType, string newType)
    {
        var oldNorm = NormalizeType(oldType);
        var newNorm = NormalizeType(newType);

        // Same base type — check size parameters
        if (oldNorm.BaseName == newNorm.BaseName)
        {
            // String: widening is safe (50 → 100), narrowing is not (100 → 50)
            if (oldNorm.BaseName == "string" && oldNorm.Param1.HasValue && newNorm.Param1.HasValue)
                return newNorm.Param1.Value >= oldNorm.Param1.Value;

            // Decimal: safe if both precision and scale are >= old
            if (oldNorm.BaseName == "decimal" && oldNorm.Param1.HasValue && newNorm.Param1.HasValue)
                return newNorm.Param1.Value >= oldNorm.Param1.Value &&
                       (newNorm.Param2 ?? 0) >= (oldNorm.Param2 ?? 0);

            // Same type without params — always safe
            return true;
        }

        // Compatible type promotions
        if (oldNorm.BaseName == "integer" && newNorm.BaseName == "decimal") return true;
        if (oldNorm.BaseName == "date" && newNorm.BaseName == "datetime") return true;
        if (oldNorm.BaseName == "date" && newNorm.BaseName == "timestamp") return true;
        if (oldNorm.BaseName == "datetime" && newNorm.BaseName == "timestamp") return true;

        // Everything else is potentially unsafe
        return false;
    }

    private static (string BaseName, int? Param1, int? Param2) NormalizeType(string typeStr)
    {
        var trimmed = typeStr.Trim();
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx < 0)
            return (trimmed.ToLowerInvariant(), null, null);

        var baseName = trimmed[..parenIdx].Trim().ToLowerInvariant();
        var paramsStr = trimmed[(parenIdx + 1)..].TrimEnd(')').Trim();
        var parts = paramsStr.Split(',');

        int? p1 = int.TryParse(parts[0].Trim(), out var v1) ? v1 : null;
        int? p2 = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var v2) ? v2 : null;

        return (baseName, p1, p2);
    }

    /// <summary>
    /// After renaming a field, update references in rules, access controls, and computed fields.
    /// </summary>
    private static void UpdateFieldReferences(BmModel model, string entityName, string oldName, string newName)
    {
        // Update rules targeting this entity
        foreach (var rule in model.Rules)
        {
            if (!string.Equals(rule.TargetEntity, entityName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var stmt in rule.Statements)
            {
                UpdateStatementFieldReferences(stmt, oldName, newName);
            }
        }

        // Update access controls targeting this entity
        foreach (var ac in model.AccessControls)
        {
            if (!string.Equals(ac.TargetEntity, entityName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var accessRule in ac.Rules)
            {
                if (accessRule.WhereConditionExpr != null)
                    UpdateExpressionFieldReferences(accessRule.WhereConditionExpr, oldName, newName);

                foreach (var fr in accessRule.FieldRestrictions)
                {
                    if (string.Equals(fr.FieldName, oldName, StringComparison.OrdinalIgnoreCase))
                        fr.FieldName = newName;
                    if (fr.ConditionExpr != null)
                        UpdateExpressionFieldReferences(fr.ConditionExpr, oldName, newName);
                }
            }
        }

        // Update computed field expressions on the entity itself
        var entity = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, entityName, StringComparison.OrdinalIgnoreCase));
        if (entity != null)
        {
            foreach (var field in entity.Fields)
            {
                if (field.ComputedExpr != null)
                    UpdateExpressionFieldReferences(field.ComputedExpr, oldName, newName);
            }
        }
    }

    private static void UpdateStatementFieldReferences(BmRuleStatement stmt, string oldName, string newName)
    {
        switch (stmt)
        {
            case BmValidateStatement validate:
                if (validate.ExpressionAst != null)
                    UpdateExpressionFieldReferences(validate.ExpressionAst, oldName, newName);
                break;
            case BmComputeStatement compute:
                if (string.Equals(compute.Target, oldName, StringComparison.OrdinalIgnoreCase))
                    compute.Target = newName;
                if (compute.ExpressionAst != null)
                    UpdateExpressionFieldReferences(compute.ExpressionAst, oldName, newName);
                break;
            case BmWhenStatement whenStmt:
                if (whenStmt.ConditionAst != null)
                    UpdateExpressionFieldReferences(whenStmt.ConditionAst, oldName, newName);
                foreach (var thenStmt in whenStmt.ThenStatements)
                    UpdateStatementFieldReferences(thenStmt, oldName, newName);
                foreach (var elseStmt in whenStmt.ElseStatements)
                    UpdateStatementFieldReferences(elseStmt, oldName, newName);
                break;
            case BmForeachStatement forEach:
                foreach (var bodyStmt in forEach.Body)
                    UpdateStatementFieldReferences(bodyStmt, oldName, newName);
                break;
            case BmLetStatement let:
                if (let.ExpressionAst != null)
                    UpdateExpressionFieldReferences(let.ExpressionAst, oldName, newName);
                break;
        }
    }

    private static void UpdateExpressionFieldReferences(BmExpression expr, string oldName, string newName)
    {
        BmExpressionWalker.Walk(expr, node =>
        {
            if (node is BmIdentifierExpression id)
            {
                for (int i = 0; i < id.Path.Count; i++)
                {
                    if (string.Equals(id.Path[i], oldName, StringComparison.OrdinalIgnoreCase))
                        id.Path[i] = newName;
                }
            }
        });
    }
}
