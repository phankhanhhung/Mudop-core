using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates migration definitions: version uniqueness, step entity references,
/// and alter action field references.
/// </summary>
public class MigrationValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        // Check for duplicate migration versions
        var versionsSeen = new Dictionary<string, BmMigrationDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var migration in model.Migrations)
        {
            count++;

            // Migration must have a version
            if (string.IsNullOrEmpty(migration.Version))
            {
                context.AddWarning(ErrorCodes.SEM_MIGRATION_DUPLICATE_VERSION,
                    $"Migration '{migration.Name}' has no version specified",
                    migration.SourceFile, migration.StartLine, PassName);
            }
            else
            {
                if (versionsSeen.TryGetValue(migration.Version, out var existing))
                {
                    context.AddError(ErrorCodes.SEM_MIGRATION_DUPLICATE_VERSION,
                        $"Duplicate migration version '{migration.Version}' (also defined by migration '{existing.Name}')",
                        migration.SourceFile, migration.StartLine, PassName);
                }
                else
                {
                    versionsSeen[migration.Version] = migration;
                }
            }

            // Migration should have at least one up step
            if (migration.UpSteps.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_MIGRATION_NO_STEPS,
                    $"Migration '{migration.Name}' (version '{migration.Version}') has no up steps",
                    migration.SourceFile, migration.StartLine, PassName);
            }

            // Validate referenced entities in steps
            foreach (var step in migration.UpSteps.Concat(migration.DownSteps))
            {
                count++;
                ValidateMigrationStep(context, model, migration, step);
            }
        }

        return count;
    }

    private void ValidateMigrationStep(CompilationContext context, BmModel model, BmMigrationDef migration, BmMigrationStep step)
    {
        switch (step)
        {
            case BmAlterEntityStep alter:
                if (!string.IsNullOrEmpty(alter.EntityName))
                {
                    var entity = model.FindEntity(alter.EntityName);
                    if (entity == null)
                    {
                        context.AddWarning(ErrorCodes.SEM_MIGRATION_ENTITY_NOT_FOUND,
                            $"Migration '{migration.Name}': ALTER ENTITY references non-existent entity '{alter.EntityName}'",
                            step.SourceFile ?? migration.SourceFile, step.StartLine, PassName);
                    }
                    else
                    {
                        // Validate field references in alter actions
                        foreach (var action in alter.Actions)
                        {
                            ValidateAlterAction(context, entity, migration, action);
                        }
                    }
                }
                break;

            case BmDropEntityStep drop:
                if (!string.IsNullOrEmpty(drop.EntityName))
                {
                    var entity = model.FindEntity(drop.EntityName);
                    if (entity == null)
                    {
                        context.AddWarning(ErrorCodes.SEM_MIGRATION_ENTITY_NOT_FOUND,
                            $"Migration '{migration.Name}': DROP ENTITY references non-existent entity '{drop.EntityName}'",
                            step.SourceFile ?? migration.SourceFile, step.StartLine, PassName);
                    }
                }
                break;

            case BmTransformStep transform:
                if (!string.IsNullOrEmpty(transform.EntityName))
                {
                    var entity = model.FindEntity(transform.EntityName);
                    if (entity == null)
                    {
                        context.AddWarning(ErrorCodes.SEM_MIGRATION_ENTITY_NOT_FOUND,
                            $"Migration '{migration.Name}': TRANSFORM references non-existent entity '{transform.EntityName}'",
                            step.SourceFile ?? migration.SourceFile, step.StartLine, PassName);
                    }
                }
                break;
        }
    }

    private void ValidateAlterAction(CompilationContext context, BmEntity entity, BmMigrationDef migration, BmAlterAction action)
    {
        switch (action)
        {
            case BmAlterDropColumnAction drop:
            {
                var fieldExists = entity.Fields.Any(f =>
                    string.Equals(f.Name, drop.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!fieldExists)
                {
                    context.AddWarning(ErrorCodes.SEM_MIGRATION_FIELD_NOT_FOUND,
                        $"Migration '{migration.Name}': DROP COLUMN '{drop.ColumnName}' not found on entity '{entity.Name}'",
                        action.SourceFile ?? migration.SourceFile, action.StartLine, PassName);
                }
                break;
            }

            case BmAlterRenameColumnAction rename:
            {
                var fieldExists = entity.Fields.Any(f =>
                    string.Equals(f.Name, rename.OldName, StringComparison.OrdinalIgnoreCase));
                if (!fieldExists)
                {
                    context.AddWarning(ErrorCodes.SEM_MIGRATION_FIELD_NOT_FOUND,
                        $"Migration '{migration.Name}': RENAME COLUMN '{rename.OldName}' not found on entity '{entity.Name}'",
                        action.SourceFile ?? migration.SourceFile, action.StartLine, PassName);
                }
                break;
            }

            case BmAlterColumnAction alter:
            {
                var fieldExists = entity.Fields.Any(f =>
                    string.Equals(f.Name, alter.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!fieldExists)
                {
                    context.AddWarning(ErrorCodes.SEM_MIGRATION_FIELD_NOT_FOUND,
                        $"Migration '{migration.Name}': ALTER COLUMN '{alter.ColumnName}' not found on entity '{entity.Name}'",
                        action.SourceFile ?? migration.SourceFile, action.StartLine, PassName);
                }
                break;
            }
        }
    }
}
