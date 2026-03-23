using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Expressions;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates indexes, unique constraints, check constraints, foreign key constraints,
/// and cross-model duplicate detection (entity names, type names).
/// </summary>
public class ConstraintValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        count += ValidateIndexesAndConstraints(context, model);
        count += ValidateDuplicates(context, model);

        return count;
    }

    private int ValidateIndexesAndConstraints(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            // Collect all valid column names: fields + associations + compositions
            var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in entity.Fields) validNames.Add(f.Name);
            foreach (var a in entity.Associations) validNames.Add(a.Name);
            foreach (var c in entity.Compositions) validNames.Add(c.Name);

            // Validate index columns
            foreach (var index in entity.Indexes)
            {
                foreach (var col in index.Fields)
                {
                    if (!validNames.Contains(col))
                    {
                        context.AddError(ErrorCodes.SEM_INVALID_INDEX_COLUMN,
                            $"Index '{index.Name}' in entity '{entity.Name}' references unknown column '{col}'",
                            entity.SourceFile, entity.StartLine, PassName);
                    }
                    count++;
                }
            }

            // Validate unique constraint columns
            foreach (var constraint in entity.Constraints)
            {
                if (constraint is BmUniqueConstraint uc)
                {
                    foreach (var col in uc.Fields)
                    {
                        if (!validNames.Contains(col))
                        {
                            context.AddError(ErrorCodes.SEM_INVALID_CONSTRAINT_FIELD,
                                $"Unique constraint '{uc.Name}' in entity '{entity.Name}' references unknown field '{col}'",
                                entity.SourceFile, entity.StartLine, PassName);
                        }
                        count++;
                    }
                }
                else if (constraint is BmCheckConstraint cc)
                {
                    // For CHECK constraints, validate field references in the expression AST
                    if (cc.Condition != null)
                    {
                        ValidateExpressionFieldRefs(context, cc.Condition, entity, $"CHECK constraint '{cc.Name}'");
                    }
                    count++;
                }
                else if (constraint is BmForeignKeyConstraint fk)
                {
                    foreach (var col in fk.Fields)
                    {
                        if (!validNames.Contains(col))
                        {
                            context.AddError(ErrorCodes.SEM_INVALID_CONSTRAINT_FIELD,
                                $"Foreign key constraint '{fk.Name}' in entity '{entity.Name}' references unknown field '{col}'",
                                entity.SourceFile, entity.StartLine, PassName);
                        }
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private int ValidateDuplicates(CompilationContext context, BmModel model)
    {
        int count = 0;

        // Check for duplicate entity names
        var entityNames = model.Entities.GroupBy(e => e.QualifiedName ?? e.Name);
        foreach (var group in entityNames.Where(g => g.Count() > 1))
        {
            context.AddError(ErrorCodes.SEM_DUPLICATE_ENTITY, $"Duplicate entity name: '{group.Key}'", pass: PassName);
            count++;
        }

        // Check for duplicate type names
        var typeNames = model.Types.Concat<object>(model.Enums)
            .Select(t => t is BmType ty ? (ty.Namespace != null ? $"{ty.Namespace}.{ty.Name}" : ty.Name)
                       : (t is BmEnum en ? (en.Namespace != null ? $"{en.Namespace}.{en.Name}" : en.Name) : ""));
        foreach (var name in typeNames.GroupBy(n => n).Where(g => g.Count() > 1 && !string.IsNullOrEmpty(g.Key)))
        {
            context.AddError(ErrorCodes.SEM_DUPLICATE_TYPE, $"Duplicate type name: '{name.Key}'", pass: PassName);
            count++;
        }

        count++;
        return count;
    }

    private void ValidateExpressionFieldRefs(CompilationContext context, BmExpression expr, BmEntity entity, string constraintDesc)
    {
        // Collect valid field names
        var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in entity.Fields) validNames.Add(f.Name);

        // Walk the expression tree for identifier references
        var identifiers = CollectIdentifiers(expr);
        foreach (var id in identifiers)
        {
            // Only check simple (single-segment) identifiers as field refs
            if (id.IsSimple && !validNames.Contains(id.Root))
            {
                context.AddWarning(ErrorCodes.SEM_INVALID_CONSTRAINT_FIELD,
                    $"{constraintDesc} in entity '{entity.Name}' references unknown field '{id.Root}'",
                    entity.SourceFile, entity.StartLine, PassName);
            }
        }
    }

    private static List<BmIdentifierExpression> CollectIdentifiers(BmExpression expr)
    {
        return BmExpressionWalker.Collect(expr, e => e as BmIdentifierExpression);
    }
}
