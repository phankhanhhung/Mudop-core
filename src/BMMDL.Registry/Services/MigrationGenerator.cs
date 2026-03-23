using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Registry.Services;

/// <summary>
/// Generates UP/DOWN SQL migration scripts from detected changes.
/// </summary>
public class MigrationGenerator
{
    private readonly string _schemaName;

    public MigrationGenerator(string schemaName = "public")
    {
        _schemaName = schemaName;
    }



    /// <summary>
    /// Generate migration plan from change detection result.
    /// </summary>
    public MigrationPlan GenerateMigration(ChangeDetectionResult changes)
    {
        var plan = new MigrationPlan();

        // Entity changes
        foreach (var change in changes.EntityChanges)
        {
            switch (change.ChangeType)
            {
                case ObjectChangeType.Add:
                    // Entity creation is handled by field changes
                    break;
                case ObjectChangeType.Remove:
                    plan.Steps.Add(GenerateDropTable(change.EntityName));
                    break;
            }
        }

        // Field changes generate ALTER TABLE statements
        foreach (var change in changes.FieldChanges)
        {
            switch (change.ChangeType)
            {
                case ObjectChangeType.Add:
                    plan.Steps.Add(GenerateAddColumn(change));
                    break;
                case ObjectChangeType.Remove:
                    plan.Steps.Add(GenerateDropColumn(change));
                    break;
                case ObjectChangeType.Modify:
                    var alterSteps = GenerateAlterColumn(change);
                    plan.Steps.AddRange(alterSteps);
                    break;
            }
        }

        // Enum changes (for PostgreSQL, enums are types)
        foreach (var change in changes.EnumChanges)
        {
            switch (change.ChangeType)
            {
                case ObjectChangeType.Add:
                    // Skip - enum creation handled separately
                    break;
                case ObjectChangeType.Modify:
                    plan.Steps.Add(GenerateEnumChange(change));
                    break;
                case ObjectChangeType.Remove:
                    plan.Steps.Add(GenerateDropEnum(change));
                    break;
            }
        }

        return plan;
    }

    /// <summary>
    /// Generate migration scripts for a specific entity field.
    /// </summary>
    public MigrationStep GenerateAddColumn(FieldChange change)
    {
        var tableName = NamingConvention.ToSnakeCase(change.EntityName);
        var columnName = NamingConvention.ToSnakeCase(change.FieldName);
        var sqlType = change.NewValue ?? "TEXT"; // Default to TEXT if type unknown

        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);
        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        var upSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ADD COLUMN {quotedColumn} {sqlType}";
        if (!change.IsBreaking)
        {
            // Optional field
            upSql += " NULL";
        }
        else
        {
            // Required field - need default
            upSql += " NOT NULL DEFAULT ''"; // Placeholder, should use actual default
        }
        upSql += ";";

        var downSql = $"ALTER TABLE {quotedSchema}.{quotedTable} DROP COLUMN {quotedColumn};";

        return new MigrationStep
        {
            Description = $"Add column {columnName} to {tableName}",
            UpSql = upSql,
            DownSql = downSql,
            IsBreaking = change.IsBreaking,
            RequiresDataMigration = change.IsBreaking
        };
    }

    public MigrationStep GenerateDropColumn(FieldChange change)
    {
        var tableName = NamingConvention.ToSnakeCase(change.EntityName);
        var columnName = NamingConvention.ToSnakeCase(change.FieldName);
        var sqlType = change.OldValue ?? "TEXT";

        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);
        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        var upSql = $"ALTER TABLE {quotedSchema}.{quotedTable} DROP COLUMN {quotedColumn};";
        var downSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ADD COLUMN {quotedColumn} {sqlType} NULL;";

        return new MigrationStep
        {
            Description = $"Drop column {columnName} from {tableName}",
            UpSql = upSql,
            DownSql = downSql,
            IsBreaking = true,
            RequiresDataMigration = true,
            WarningMessage = "Data in this column will be lost!"
        };
    }

    public List<MigrationStep> GenerateAlterColumn(FieldChange change)
    {
        var steps = new List<MigrationStep>();
        var tableName = NamingConvention.ToSnakeCase(change.EntityName);
        var columnName = NamingConvention.ToSnakeCase(change.FieldName);

        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);
        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        // Type change
        if (change.OldValue != change.NewValue && change.OldValue != null && change.NewValue != null)
        {
            var upSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} TYPE {change.NewValue};";
            var downSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} TYPE {change.OldValue};";

            steps.Add(new MigrationStep
            {
                Description = $"Change type of {columnName} from {change.OldValue} to {change.NewValue}",
                UpSql = upSql,
                DownSql = downSql,
                IsBreaking = change.IsBreaking,
                RequiresDataMigration = change.IsBreaking
            });
        }

        // Nullability change
        if (change.Description.Contains("nullable") || change.Description.Contains("required"))
        {
            if (change.Description.Contains("nullable"))
            {
                // Made nullable
                var upSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} DROP NOT NULL;";
                var downSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} SET NOT NULL;";

                steps.Add(new MigrationStep
                {
                    Description = $"Make {columnName} nullable",
                    UpSql = upSql,
                    DownSql = downSql,
                    IsBreaking = false,
                    RequiresDataMigration = false
                });
            }
            else
            {
                // Made required
                var upSql = $"""
                    -- Update NULL values before making column required
                    UPDATE {quotedSchema}.{quotedTable} SET {quotedColumn} = '' WHERE {quotedColumn} IS NULL;
                    ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} SET NOT NULL;
                    """;
                var downSql = $"ALTER TABLE {quotedSchema}.{quotedTable} ALTER COLUMN {quotedColumn} DROP NOT NULL;";

                steps.Add(new MigrationStep
                {
                    Description = $"Make {columnName} required",
                    UpSql = upSql,
                    DownSql = downSql,
                    IsBreaking = true,
                    RequiresDataMigration = true,
                    WarningMessage = "NULL values will be replaced with empty string!"
                });
            }
        }

        return steps;
    }

    public MigrationStep GenerateDropTable(string entityName)
    {
        var tableName = NamingConvention.ToSnakeCase(entityName);
        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedTable = NamingConvention.QuoteIdentifier(tableName);

        return new MigrationStep
        {
            Description = $"Drop table {tableName}",
            UpSql = $"DROP TABLE IF EXISTS {quotedSchema}.{quotedTable} CASCADE;",
            DownSql = $"-- Table recreation not auto-generated, requires full schema",
            IsBreaking = true,
            RequiresDataMigration = true,
            WarningMessage = "All data in this table will be lost!"
        };
    }

    public MigrationStep GenerateEnumChange(EnumChange change)
    {
        var enumName = NamingConvention.ToSnakeCase(change.EnumName);
        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedEnum = NamingConvention.QuoteIdentifier(enumName);

        // PostgreSQL enum modification is complex
        // Adding values is safe, removing requires recreating
        if (change.IsBreaking)
        {
            return new MigrationStep
            {
                Description = $"Modify enum {enumName} (values removed)",
                UpSql = $"-- Enum value removal requires manual migration\n-- DROP TYPE IF EXISTS {quotedSchema}.{quotedEnum} CASCADE;\n-- CREATE TYPE ...",
                DownSql = $"-- Reverse enum migration",
                IsBreaking = true,
                RequiresDataMigration = true,
                WarningMessage = "Enum value removal requires manual migration!"
            };
        }

        return new MigrationStep
        {
            Description = $"Modify enum {enumName} (values added)",
            UpSql = $"-- ALTER TYPE {quotedSchema}.{quotedEnum} ADD VALUE 'new_value';",
            DownSql = $"-- Enum value removal not reversible",
            IsBreaking = false,
            RequiresDataMigration = false
        };
    }

    public MigrationStep GenerateDropEnum(EnumChange change)
    {
        var enumName = NamingConvention.ToSnakeCase(change.EnumName);
        var quotedSchema = NamingConvention.QuoteIdentifier(_schemaName);
        var quotedEnum = NamingConvention.QuoteIdentifier(enumName);

        return new MigrationStep
        {
            Description = $"Drop enum {enumName}",
            UpSql = $"DROP TYPE IF EXISTS {quotedSchema}.{quotedEnum} CASCADE;",
            DownSql = $"-- Enum recreation not auto-generated",
            IsBreaking = true,
            RequiresDataMigration = false
        };
    }


}

#region Migration Plan Types

/// <summary>
/// Complete migration plan with ordered steps.
/// </summary>
public class MigrationPlan
{
    public List<MigrationStep> Steps { get; } = new();
    
    public bool HasBreakingChanges => Steps.Any(s => s.IsBreaking);
    public bool RequiresDataMigration => Steps.Any(s => s.RequiresDataMigration);

    /// <summary>
    /// Get combined UP script.
    /// </summary>
    public string GetUpScript()
    {
        return string.Join("\n\n", Steps.Select(s => $"-- {s.Description}\n{s.UpSql}"));
    }

    /// <summary>
    /// Get combined DOWN script (in reverse order).
    /// </summary>
    public string GetDownScript()
    {
        return string.Join("\n\n", Steps.AsEnumerable().Reverse().Select(s => $"-- Rollback: {s.Description}\n{s.DownSql}"));
    }
}

/// <summary>
/// Single migration step.
/// </summary>
public class MigrationStep
{
    public string Description { get; set; } = "";
    public string UpSql { get; set; } = "";
    public string DownSql { get; set; } = "";
    public bool IsBreaking { get; set; }
    public bool RequiresDataMigration { get; set; }
    public string? WarningMessage { get; set; }
}

#endregion
