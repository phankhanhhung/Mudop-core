using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates seed data definitions: entity existence, column validity,
/// row value counts, and key column presence.
/// </summary>
public class SeedValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;
        var seedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in model.Seeds)
        {
            count++;

            // SEM124: duplicate seed name
            if (!seedNames.Add(seed.QualifiedName))
            {
                context.AddError(ErrorCodes.SEM_SEED_DUPLICATE_NAME,
                    $"Duplicate seed name '{seed.QualifiedName}'",
                    seed.SourceFile, seed.StartLine, PassName);
            }

            // SEM120: entity not found
            var entity = model.Entities.FirstOrDefault(e =>
                e.Name.Equals(seed.EntityName, StringComparison.OrdinalIgnoreCase) ||
                e.QualifiedName.Equals(seed.EntityName, StringComparison.OrdinalIgnoreCase));

            if (entity == null)
            {
                context.AddError(ErrorCodes.SEM_SEED_ENTITY_NOT_FOUND,
                    $"Seed '{seed.Name}' references non-existent entity '{seed.EntityName}'",
                    seed.SourceFile, seed.StartLine, PassName);
                continue; // Can't validate columns without entity
            }

            // SEM121: column not found
            foreach (var col in seed.Columns)
            {
                count++;

                var fieldExists = entity.Fields.Any(f =>
                    f.Name.Equals(col, StringComparison.OrdinalIgnoreCase));
                var assocExists = entity.Associations.Any(a =>
                    a.Name.Equals(col, StringComparison.OrdinalIgnoreCase));

                if (!fieldExists && !assocExists)
                {
                    context.AddError(ErrorCodes.SEM_SEED_COLUMN_NOT_FOUND,
                        $"Seed '{seed.Name}' column '{col}' not found on entity '{entity.Name}'",
                        seed.SourceFile, seed.StartLine, PassName);
                }
            }

            // SEM123: computed/virtual field in seed
            foreach (var col in seed.Columns)
            {
                var field = entity.Fields.FirstOrDefault(f =>
                    f.Name.Equals(col, StringComparison.OrdinalIgnoreCase));

                if (field?.IsVirtual == true || field?.IsComputed == true)
                {
                    context.AddError(ErrorCodes.SEM_SEED_COMPUTED_FIELD,
                        $"Seed '{seed.Name}' column '{col}' is a computed/virtual field and cannot be seeded",
                        seed.SourceFile, seed.StartLine, PassName);
                }
            }

            // SEM122: row value count mismatch
            foreach (var row in seed.Rows)
            {
                count++;

                if (row.Values.Count != seed.Columns.Count)
                {
                    context.AddError(ErrorCodes.SEM_SEED_ROW_COUNT_MISMATCH,
                        $"Seed '{seed.Name}' row at line {row.Line} has {row.Values.Count} values but {seed.Columns.Count} columns declared",
                        seed.SourceFile, row.Line, PassName);
                }
            }

            // SEM125: no rows defined (warning)
            if (seed.Rows.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_SEED_NO_ROWS,
                    $"Seed '{seed.Name}' has no data rows",
                    seed.SourceFile, seed.StartLine, PassName);
            }

            // SEM126: warning if key column missing
            var keyFields = entity.Fields.Where(f => f.IsKey).ToList();
            foreach (var key in keyFields)
            {
                if (!seed.Columns.Any(c => c.Equals(key.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    context.AddWarning(ErrorCodes.SEM_SEED_MISSING_KEY,
                        $"Seed '{seed.Name}' does not include key field '{key.Name}' — rows may not be idempotent",
                        seed.SourceFile, seed.StartLine, PassName);
                }
            }
        }

        return count;
    }
}
