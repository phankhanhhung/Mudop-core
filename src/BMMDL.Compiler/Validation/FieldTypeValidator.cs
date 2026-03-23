using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Service;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates field types: struct type fields, default expression type compatibility,
/// and type resolution for service/bound operation parameters.
/// </summary>
public class FieldTypeValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    private static readonly HashSet<string> s_builtInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "String", "Integer", "Decimal", "Boolean", "Date", "Time", "DateTime", "Timestamp",
        "UUID", "Binary", "Int32", "Int64", "Float", "Double", "Bool", "Byte", "Char",
        "LocalDate", "LocalTime", "Instant", "Duration", "Guid", "Void"
    };

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        // Validate struct types
        count += ValidateTypes(context, model);

        // Validate default expression type compatibility
        count += ValidateDefaultExpressions(context, model);

        // Validate service parameter types
        count += ValidateServiceParameterTypes(context, model);

        return count;
    }

    private int ValidateTypes(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var type in model.Types)
        {
            count++;

            // Struct types should have fields
            if (type.Fields.Count == 0 && string.IsNullOrEmpty(type.BaseType))
            {
                context.AddWarning(ErrorCodes.SEM_TYPE_NO_FIELDS, $"Type '{type.Name}' has no fields and no base type", type.SourceFile, type.StartLine, PassName);
            }
        }

        return count;
    }

    private int ValidateDefaultExpressions(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.DefaultExpr == null) continue;
                count++;

                var fieldBaseType = ExtractBaseTypeName(field.TypeString);
                if (string.IsNullOrEmpty(fieldBaseType)) continue;

                if (field.DefaultExpr is BmLiteralExpression literal)
                {
                    if (!IsDefaultCompatible(literal, fieldBaseType))
                    {
                        context.AddWarning(ErrorCodes.SEM_DEFAULT_TYPE_MISMATCH,
                            $"Field '{field.Name}' in entity '{entity.Name}' has type '{field.TypeString}' but default value is {literal.Kind} literal",
                            entity.SourceFile, field.StartLine, PassName);
                    }

                    // V4: Validate enum default values — check that the member actually exists in the enum
                    if (literal.Kind == BmLiteralKind.EnumValue && literal.Value is string enumMember)
                    {
                        ValidateEnumMemberExists(context, model, entity, field, fieldBaseType, enumMember);
                    }
                }
            }
        }

        return count;
    }

    private int ValidateServiceParameterTypes(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var service in model.Services)
        {
            foreach (var func in service.Functions.Concat<BmFunction>(service.Actions))
            {
                // Validate return type
                if (!string.IsNullOrEmpty(func.ReturnType) && !IsKnownType(func.ReturnType, context))
                {
                    context.AddWarning(ErrorCodes.SEM_UNRESOLVED_PARAM_TYPE,
                        $"Function/action '{func.Name}' in service '{service.Name}' has unresolved return type '{func.ReturnType}'",
                        service.SourceFile, func.StartLine, PassName);
                }
                count++;

                // Validate parameter types
                foreach (var param in func.Parameters)
                {
                    if (!string.IsNullOrEmpty(param.Type) && !IsKnownType(param.Type, context))
                    {
                        context.AddWarning(ErrorCodes.SEM_UNRESOLVED_PARAM_TYPE,
                            $"Parameter '{param.Name}' in '{func.Name}' (service '{service.Name}') has unresolved type '{param.Type}'",
                            service.SourceFile, param.StartLine, PassName);
                    }
                    count++;
                }
            }
        }

        // Also validate bound actions/functions on entities
        foreach (var entity in model.Entities)
        {
            foreach (var func in entity.BoundFunctions.Concat<BmFunction>(entity.BoundActions))
            {
                if (!string.IsNullOrEmpty(func.ReturnType) && !IsKnownType(func.ReturnType, context))
                {
                    context.AddWarning(ErrorCodes.SEM_UNRESOLVED_PARAM_TYPE,
                        $"Bound function/action '{func.Name}' on entity '{entity.Name}' has unresolved return type '{func.ReturnType}'",
                        entity.SourceFile, func.StartLine, PassName);
                }
                count++;

                foreach (var param in func.Parameters)
                {
                    if (!string.IsNullOrEmpty(param.Type) && !IsKnownType(param.Type, context))
                    {
                        context.AddWarning(ErrorCodes.SEM_UNRESOLVED_PARAM_TYPE,
                            $"Parameter '{param.Name}' in bound '{func.Name}' (entity '{entity.Name}') has unresolved type '{param.Type}'",
                            entity.SourceFile, param.StartLine, PassName);
                    }
                    count++;
                }
            }
        }

        return count;
    }

    internal static bool IsDefaultCompatible(BmLiteralExpression literal, string fieldBaseType)
    {
        // Null literal is compatible with any nullable type
        if (literal.Kind == BmLiteralKind.Null) return true;
        // Enum literals are valid for enum-typed fields (validated elsewhere)
        if (literal.Kind == BmLiteralKind.EnumValue) return true;

        var upper = fieldBaseType.ToUpperInvariant();
        return literal.Kind switch
        {
            BmLiteralKind.String => upper is "STRING" or "UUID" or "DATE" or "TIME" or "DATETIME" or "TIMESTAMP",
            BmLiteralKind.Integer => upper is "INTEGER" or "INT32" or "INT64" or "DECIMAL" or "FLOAT" or "DOUBLE",
            BmLiteralKind.Decimal => upper is "DECIMAL" or "FLOAT" or "DOUBLE" or "INTEGER" or "INT32" or "INT64",
            BmLiteralKind.Boolean => upper is "BOOLEAN" or "BOOL",
            _ => true
        };
    }

    internal static string? ExtractBaseTypeName(string? typeString)
    {
        if (string.IsNullOrEmpty(typeString)) return null;
        var idx = typeString.IndexOfAny(['(', '[', '<', '?']);
        return idx > 0 ? typeString[..idx] : typeString;
    }

    internal static bool IsKnownType(string typeName, CompilationContext context)
    {
        if (string.IsNullOrEmpty(typeName)) return true;

        // Extract base name before any parameters
        var baseName = typeName.Split('(', '[', '<', '?')[0].Trim();

        // Handle Array<T>
        if (baseName.Equals("Array", StringComparison.OrdinalIgnoreCase))
            return true; // Element type checked separately

        // Built-in primitives
        if (s_builtInTypes.Contains(baseName)) return true;

        // Check if registered in symbol table (entity, type, or enum)
        if (context.Symbols.Contains(baseName)) return true;

        // Try with model namespace
        if (context.Model?.Namespace != null && context.Symbols.Contains($"{context.Model.Namespace}.{baseName}"))
            return true;

        // Try imported namespaces
        if (context.Model?.Module?.Imports != null)
        {
            foreach (var ns in context.Model.Module.Imports)
            {
                if (context.Symbols.Contains($"{ns}.{baseName}")) return true;
            }
        }

        // Try all published namespaces
        if (context.Model?.AllModules != null)
        {
            foreach (var mod in context.Model.AllModules)
            {
                foreach (var pub in mod.Publishes)
                {
                    if (context.Symbols.Contains($"{pub}.{baseName}")) return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// V4: Validates that an enum literal default value (#Member) references a member that
    /// actually exists in the resolved enum type. Emits a warning if the enum is found
    /// but the member is not.
    /// </summary>
    private static void ValidateEnumMemberExists(
        CompilationContext context,
        BmModel model,
        BmEntity entity,
        BmField field,
        string fieldBaseType,
        string enumMember)
    {
        // Try to find the enum definition by name (simple or qualified)
        var enumDef = model.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, fieldBaseType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, fieldBaseType, StringComparison.OrdinalIgnoreCase));

        if (enumDef == null)
        {
            // Enum type not found in model — could be in another module or not yet resolved.
            // Don't report an error here; type resolution is handled elsewhere.
            return;
        }

        var memberExists = enumDef.Values.Any(v =>
            string.Equals(v.Name, enumMember, StringComparison.OrdinalIgnoreCase));

        if (!memberExists)
        {
            var validMembers = string.Join(", ", enumDef.Values.Select(v => v.Name));
            context.AddWarning(ErrorCodes.SEM_DEFAULT_TYPE_MISMATCH,
                $"Field '{field.Name}' in entity '{entity.Name}' has default value '#{enumMember}' " +
                $"but enum '{enumDef.Name}' has no member '{enumMember}'. Valid members: {validMembers}",
                entity.SourceFile, field.StartLine, PassName);
        }
    }
}
