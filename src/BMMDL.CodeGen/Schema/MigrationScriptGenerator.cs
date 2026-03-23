using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BMMDL.CodeGen.Schema;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Generates PostgreSQL migration scripts from schema diffs.
/// </summary>
public class MigrationScriptGenerator
{
    /// <summary>
    /// Generates a complete migration from a schema diff.
    /// </summary>
    public Migration GenerateMigration(SchemaDiff diff, string migrationName)
    {
        var upScript = GenerateUpScript(diff);
        var downScript = GenerateDownScript(diff);
        
        return new Migration
        {
            Name = migrationName,
            Timestamp = DateTime.UtcNow,
            UpScript = upScript,
            DownScript = downScript,
            Checksum = ComputeChecksum(upScript)
        };
    }
    
    private string GenerateUpScript(SchemaDiff diff)
    {
        var sb = new StringBuilder();
        
        // 1. Drop constraints first (FKs, then others)
        foreach (var tableChange in diff.TablesToModify)
        {
            foreach (var constraint in tableChange.ConstraintsToDrop)
            {
                sb.AppendLine($"ALTER TABLE {NamingConvention.QuoteIdentifier(tableChange.Schema)}.{NamingConvention.QuoteIdentifier(tableChange.TableName)} DROP CONSTRAINT {NamingConvention.QuoteIdentifier(constraint)};");
            }
        }

        // 2. Drop indexes
        foreach (var tableChange in diff.TablesToModify)
        {
            foreach (var index in tableChange.IndexesToDrop)
            {
                sb.AppendLine($"DROP INDEX {NamingConvention.QuoteIdentifier(tableChange.Schema)}.{NamingConvention.QuoteIdentifier(index)};");
            }
        }

        // 3. Add new tables
        foreach (var table in diff.TablesToAdd)
        {
            sb.AppendLine(GenerateCreateTable(table));
        }

        // 4. Modify existing tables
        foreach (var tableChange in diff.TablesToModify)
        {
            sb.Append(GenerateAlterTable(tableChange));
        }

        // 5. Drop tables
        foreach (var tableName in diff.TablesToDrop)
        {
            // tableName may be schema.table or just table
            var parts = tableName.Split('.');
            var quotedName = parts.Length > 1
                ? $"{NamingConvention.QuoteIdentifier(parts[0])}.{NamingConvention.QuoteIdentifier(parts[1])}"
                : NamingConvention.QuoteIdentifier(parts[0]);
            sb.AppendLine($"DROP TABLE {quotedName};");
        }
        
        return sb.ToString();
    }
    
    private string GenerateDownScript(SchemaDiff diff)
    {
        // Reverse operations
        var sb = new StringBuilder();
        sb.AppendLine("-- Rollback script");
        sb.AppendLine("-- This is a basic reverse migration");
        sb.AppendLine("-- Manual verification recommended");
        sb.AppendLine();

        // Reverse table drops → creates
        foreach (var droppedTable in diff.DroppedTableInfo)
        {
            sb.AppendLine($"-- Restore dropped table: {droppedTable.FullyQualifiedName}");
            sb.Append(GenerateCreateTable(droppedTable));
            sb.AppendLine();
        }

        // Reverse modifications
        foreach (var tableChange in diff.TablesToModify)
        {
            var tableName = $"{NamingConvention.QuoteIdentifier(tableChange.Schema)}.{NamingConvention.QuoteIdentifier(tableChange.TableName)}";

            // Reverse column renames (swap old↔new)
            foreach (var (oldName, newName) in tableChange.ColumnRenames)
            {
                sb.AppendLine($"ALTER TABLE {tableName} RENAME COLUMN {NamingConvention.QuoteIdentifier(newName)} TO {NamingConvention.QuoteIdentifier(oldName)};");
            }

            // Reverse column additions → drops
            foreach (var column in tableChange.ColumnsToAdd)
            {
                sb.AppendLine($"ALTER TABLE {tableName} DROP COLUMN {NamingConvention.QuoteIdentifier(column.Name)};");
            }

            // Reverse column drops → adds
            foreach (var droppedColumn in tableChange.DroppedColumnInfo)
            {
                sb.AppendLine($"-- Restore dropped column: {droppedColumn.Name}");
                sb.AppendLine($"ALTER TABLE {tableName} ADD COLUMN {FormatColumn(droppedColumn)};");
            }
        }

        // Reverse table adds → drops
        foreach (var table in diff.TablesToAdd)
        {
            sb.AppendLine($"DROP TABLE IF EXISTS {NamingConvention.QuoteIdentifier(table.Schema)}.{NamingConvention.QuoteIdentifier(table.Name)} CASCADE;");
        }

        return sb.ToString();
    }
    
    private string GenerateCreateTable(TableInfo table)
    {
        var sb = new StringBuilder();
        var qualifiedTableName = $"{NamingConvention.QuoteIdentifier(table.Schema)}.{NamingConvention.QuoteIdentifier(table.Name)}";
        sb.AppendLine($"CREATE TABLE {qualifiedTableName} (");

        var columns = new List<string>();
        foreach (var column in table.Columns)
        {
            columns.Add($"  {FormatColumn(column)}");
        }

        sb.AppendLine(string.Join(",\n", columns));
        sb.AppendLine(");");

        // Add constraints (PK, UNIQUE, CHECK)
        foreach (var constraint in table.Constraints)
        {
            var constraintSql = GenerateAddConstraint(qualifiedTableName, constraint);
            if (!string.IsNullOrEmpty(constraintSql))
                sb.AppendLine(constraintSql);
        }

        // Add indexes
        foreach (var index in table.Indexes.Where(i => !i.IsPrimary))
        {
            sb.AppendLine(GenerateCreateIndex(table.Schema, table.Name, index));
        }

        return sb.ToString();
    }
    
    private string GenerateAlterTable(TableChange change)
    {
        var sb = new StringBuilder();
        var tableName = $"{NamingConvention.QuoteIdentifier(change.Schema)}.{NamingConvention.QuoteIdentifier(change.TableName)}";

        // Rename columns first (preserves data, must run before drop/add)
        foreach (var (oldName, newName) in change.ColumnRenames)
        {
            sb.AppendLine($"ALTER TABLE {tableName} RENAME COLUMN {NamingConvention.QuoteIdentifier(oldName)} TO {NamingConvention.QuoteIdentifier(newName)};");
        }

        // Add columns
        foreach (var column in change.ColumnsToAdd)
        {
            sb.AppendLine($"ALTER TABLE {tableName} ADD COLUMN {FormatColumn(column)};");
        }

        // Modify columns
        foreach (var mod in change.ColumnsToModify)
        {
            sb.Append(GenerateColumnModification(tableName, mod));
        }

        // Drop columns
        foreach (var column in change.ColumnsToDrop)
        {
            sb.AppendLine($"ALTER TABLE {tableName} DROP COLUMN {NamingConvention.QuoteIdentifier(column)};");
        }

        // Add indexes
        foreach (var index in change.IndexesToAdd)
        {
            sb.AppendLine(GenerateCreateIndex(change.Schema, change.TableName, index));
        }

        // Add constraints
        foreach (var constraint in change.ConstraintsToAdd)
        {
            sb.AppendLine(GenerateAddConstraint(tableName, constraint));
        }

        return sb.ToString();
    }
    
    private string FormatColumn(ColumnInfo column)
    {
        var sb = new StringBuilder();
        sb.Append(NamingConvention.QuoteIdentifier(column.Name));
        sb.Append(" ");

        // Data type
        sb.Append(column.DataType.ToUpper());

        // Max length for varchar/character varying
        if (column.MaxLength.HasValue &&
            (column.DataType.Contains("character", StringComparison.OrdinalIgnoreCase) ||
             column.DataType.StartsWith("varchar", StringComparison.OrdinalIgnoreCase)))
        {
            sb.Append($"({column.MaxLength})");
        }

        // Precision/scale for numeric/decimal
        if (column.NumericPrecision.HasValue &&
            (column.DataType.Contains("numeric", StringComparison.OrdinalIgnoreCase) ||
             column.DataType.StartsWith("decimal", StringComparison.OrdinalIgnoreCase)))
        {
            sb.Append($"({column.NumericPrecision},{column.NumericScale ?? 0})");
        }

        // Nullable
        if (!column.IsNullable)
        {
            sb.Append(" NOT NULL");
        }

        // Default
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            sb.Append($" DEFAULT {column.DefaultValue}");
        }

        // Generated
        if (column.IsGenerated && !string.IsNullOrEmpty(column.GenerationExpression))
        {
            sb.Append($" GENERATED ALWAYS AS ({column.GenerationExpression}) STORED");
        }

        return sb.ToString();
    }
    
    private string GenerateColumnModification(string tableName, ColumnModification mod)
    {
        var sb = new StringBuilder();
        var quotedColumnName = NamingConvention.QuoteIdentifier(mod.ColumnName);

        foreach (var change in mod.Changes)
        {
            switch (change)
            {
                case ChangeType.DataTypeChange:
                    sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColumnName} TYPE {mod.NewDefinition.DataType};");
                    break;

                case ChangeType.NullabilityChange:
                    var nullClause = mod.NewDefinition.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
                    sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColumnName} {nullClause};");
                    break;

                case ChangeType.DefaultValueChange:
                    if (string.IsNullOrEmpty(mod.NewDefinition.DefaultValue))
                    {
                        sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColumnName} DROP DEFAULT;");
                    }
                    else
                    {
                        sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColumnName} SET DEFAULT {mod.NewDefinition.DefaultValue};");
                    }
                    break;

                case ChangeType.ComputedExpressionChange:
                    // PostgreSQL doesn't support ALTER for generated columns
                    // Must drop and recreate
                    sb.AppendLine($"-- WARNING: Cannot alter generated expression. Drop and recreate required:");
                    sb.AppendLine($"-- ALTER TABLE {tableName} DROP COLUMN {quotedColumnName};");
                    sb.AppendLine($"-- ALTER TABLE {tableName} ADD COLUMN {FormatColumn(mod.NewDefinition)};");
                    break;
            }
        }

        return sb.ToString();
    }
    
    private string GenerateCreateIndex(string schema, string tableName, IndexInfo index)
    {
        var unique = index.IsUnique ? "UNIQUE " : "";
        var columns = string.Join(", ", index.Columns.Select(NamingConvention.QuoteIdentifier));
        return $"CREATE {unique}INDEX {NamingConvention.QuoteIdentifier(index.Name)} ON {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(tableName)}({columns});";
    }
    
    private string GenerateAddConstraint(string tableName, ConstraintInfo constraint)
    {
        var quotedColumns = string.Join(", ", constraint.Columns.Select(NamingConvention.QuoteIdentifier));
        return constraint.Type switch
        {
            ConstraintType.PrimaryKey => $"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(constraint.Name)} PRIMARY KEY ({quotedColumns});",
            ConstraintType.ForeignKey => "", // FK constraints removed — app-level enforcement
            ConstraintType.Unique => $"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(constraint.Name)} UNIQUE ({quotedColumns});",
            ConstraintType.Check => $"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(constraint.Name)} CHECK ({constraint.CheckClause});",
            _ => ""
        };
    }

    /// <summary>
    /// Generates a migration script to drop all FK constraints from the specified schema.
    /// Used as a one-time migration when transitioning from DB-level to app-level referential integrity.
    /// </summary>
    public static string GenerateDropAllForeignKeys(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
            throw new ArgumentException("Schema name cannot be empty", nameof(schemaName));

        var escapedSchemaName = schemaName.Replace("'", "''");
        return $@"-- Drop all FK constraints from schema '{escapedSchemaName}'
-- Referential integrity is now enforced at application level by ReferentialIntegrityService
DO $$
DECLARE r RECORD;
BEGIN
    FOR r IN (
        SELECT conname, conrelid::regclass AS table_name
        FROM pg_constraint
        WHERE contype = 'f'
        AND connamespace = (SELECT oid FROM pg_namespace WHERE nspname = '{escapedSchemaName}')
    ) LOOP
        EXECUTE 'ALTER TABLE ' || r.table_name || ' DROP CONSTRAINT ' || quote_ident(r.conname);
    END LOOP;
END $$;
";
    }
    
    /// <summary>
    /// Generates a Migration from an explicit BmMigrationDef (declared in BMMDL source).
    /// </summary>
    public Migration GenerateFromMigrationDef(BmMigrationDef migrationDef, string schemaName)
    {
        var upScript = GenerateMigrationStepsScript(migrationDef.UpSteps, schemaName);
        var downScript = migrationDef.DownSteps.Count > 0
            ? GenerateMigrationStepsScript(migrationDef.DownSteps, schemaName)
            : $"-- No DOWN steps defined for migration '{migrationDef.Name}'\n";

        return new Migration
        {
            Name = migrationDef.Name,
            Timestamp = DateTime.UtcNow,
            UpScript = upScript,
            DownScript = downScript,
            Checksum = ComputeChecksum(upScript)
        };
    }

    private string GenerateMigrationStepsScript(List<BmMigrationStep> steps, string schemaName)
    {
        // Note: no BEGIN/COMMIT here — transaction management is the caller's responsibility
        // (PluginManager wraps each migration in a per-migration transaction per fix H3)
        var sb = new StringBuilder();

        foreach (var step in steps)
        {
            switch (step)
            {
                case BmAlterEntityStep alter:
                    sb.Append(GenerateAlterEntitySql(alter, schemaName));
                    break;
                case BmAddEntityStep add:
                    sb.Append(GenerateAddEntitySql(add, schemaName));
                    break;
                case BmDropEntityStep drop:
                    var dropTable = NamingConvention.ToSnakeCase(drop.EntityName);
                    sb.AppendLine($"DROP TABLE IF EXISTS {NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(dropTable)} CASCADE;");
                    sb.AppendLine();
                    break;
                case BmTransformStep transform:
                    sb.Append(GenerateTransformSql(transform, schemaName));
                    break;
            }
        }

        return sb.ToString();
    }

    private string GenerateAddEntitySql(BmAddEntityStep step, string schemaName)
    {
        var sb = new StringBuilder();
        var tableName = $"{NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(step.EntityName))}";
        sb.AppendLine($"-- ADD ENTITY {step.EntityName}");

        if (step.Fields.Count > 0)
        {
            sb.AppendLine($"CREATE TABLE {tableName} (");
            var columns = new List<string>();
            var keyFields = new List<string>();

            foreach (var field in step.Fields)
            {
                var colName = NamingConvention.ToSnakeCase(field.Name);
                var quotedColName = NamingConvention.QuoteIdentifier(colName);
                var colType = MapTypeToPostgres(field.TypeString);
                var nullable = field.IsKey || !field.IsNullable ? " NOT NULL" : "";
                var defaultVal = field.DefaultValueString != null ? $" DEFAULT {field.DefaultValueString}" : "";
                columns.Add($"    {quotedColName} {colType}{nullable}{defaultVal}");

                if (field.IsKey)
                    keyFields.Add(quotedColName);
            }

            // Add FK columns for compositions (child entities have parent FK)
            foreach (var comp in step.Compositions)
            {
                var fkColName = NamingConvention.GetFkColumnName(comp.Name);
                columns.Add($"    {NamingConvention.QuoteIdentifier(fkColName)} uuid");
            }

            sb.AppendLine(string.Join(",\n", columns));

            if (keyFields.Count > 0)
            {
                sb.AppendLine($"    , PRIMARY KEY ({string.Join(", ", keyFields)})");
            }

            sb.AppendLine(");");

            // Generate indexes
            foreach (var idx in step.Indexes)
            {
                var idxName = NamingConvention.ToSnakeCase(idx.Name);
                var idxCols = string.Join(", ", idx.Fields.Select(f => NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(f))));
                var unique = idx.IsUnique ? "UNIQUE " : "";
                sb.AppendLine($"CREATE {unique}INDEX {NamingConvention.QuoteIdentifier(idxName)} ON {tableName} ({idxCols});");
            }

            // Generate constraints
            foreach (var constraint in step.Constraints)
            {
                var constraintName = NamingConvention.ToSnakeCase(constraint.Name);
                if (constraint is BmUniqueConstraint unique)
                {
                    var cols = string.Join(", ", unique.Fields.Select(f => NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(f))));
                    sb.AppendLine($"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(constraintName)} UNIQUE ({cols});");
                }
                else if (constraint is BmCheckConstraint check && !string.IsNullOrEmpty(check.ConditionString))
                {
                    sb.AppendLine($"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(constraintName)} CHECK ({check.ConditionString});");
                }
            }
        }
        else
        {
            // Fallback when no structured fields are available
            sb.AppendLine($"-- Entity elements: {step.ElementsText}");
            sb.AppendLine($"-- (Full DDL generation deferred to schema init)");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private string GenerateAlterEntitySql(BmAlterEntityStep step, string schemaName)
    {
        var sb = new StringBuilder();
        var tableName = $"{NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(step.EntityName))}";
        sb.AppendLine($"-- ALTER ENTITY {step.EntityName}");

        foreach (var action in step.Actions)
        {
            switch (action)
            {
                case BmAlterAddColumnAction addCol:
                    var colType = MapTypeToPostgres(addCol.TypeString);
                    var nullable = addCol.IsNullable ? "" : " NOT NULL";
                    var defaultVal = addCol.DefaultValue != null ? $" DEFAULT {addCol.DefaultValue}" : "";
                    sb.AppendLine($"ALTER TABLE {tableName} ADD COLUMN {NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(addCol.FieldName))} {colType}{nullable}{defaultVal};");
                    break;

                case BmAlterDropColumnAction dropCol:
                    sb.AppendLine($"ALTER TABLE {tableName} DROP COLUMN IF EXISTS {NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(dropCol.ColumnName))};");
                    break;

                case BmAlterRenameColumnAction renameCol:
                    sb.AppendLine($"ALTER TABLE {tableName} RENAME COLUMN {NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(renameCol.OldName))} TO {NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(renameCol.NewName))};");
                    break;

                case BmAlterColumnAction alterCol:
                    foreach (var change in alterCol.Changes)
                    {
                        var colName = NamingConvention.ToSnakeCase(alterCol.ColumnName);
                        var quotedColName = NamingConvention.QuoteIdentifier(colName);
                        switch (change)
                        {
                            case BmChangeTypeChange typeChange:
                                sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColName} TYPE {MapTypeToPostgres(typeChange.NewTypeString)};");
                                break;
                            case BmSetDefaultChange setDefault:
                                sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColName} SET DEFAULT {setDefault.Expression};");
                                break;
                            case BmDropDefaultChange:
                                sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColName} DROP DEFAULT;");
                                break;
                            case BmSetNullableChange nullableChange:
                                var clause = nullableChange.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
                                sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {quotedColName} {clause};");
                                break;
                        }
                    }
                    break;

                case BmAlterAddIndexAction addIdx:
                    var unique = addIdx.IsUnique ? "UNIQUE " : "";
                    var cols = string.Join(", ", addIdx.Columns.Select(c => NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(c))));
                    sb.AppendLine($"CREATE {unique}INDEX {NamingConvention.QuoteIdentifier(addIdx.IndexName)} ON {tableName}({cols});");
                    break;

                case BmAlterDropIndexAction dropIdx:
                    sb.AppendLine($"DROP INDEX IF EXISTS {NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(dropIdx.IndexName)};");
                    break;

                case BmAlterAddConstraintAction addConstraint:
                    sb.AppendLine($"ALTER TABLE {tableName} ADD CONSTRAINT {NamingConvention.QuoteIdentifier(addConstraint.ConstraintName)} {addConstraint.ConstraintText};");
                    break;

                case BmAlterDropConstraintAction dropConstraint:
                    sb.AppendLine($"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {NamingConvention.QuoteIdentifier(dropConstraint.ConstraintName)};");
                    break;
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private string GenerateTransformSql(BmTransformStep step, string schemaName)
    {
        var sb = new StringBuilder();
        var tableName = $"{NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(step.EntityName))}";
        sb.AppendLine($"-- TRANSFORM {step.EntityName}");

        foreach (var action in step.Actions)
        {
            switch (action)
            {
                case BmTransformSetAction setAction:
                    sb.AppendLine($"UPDATE {tableName} SET {NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(setAction.FieldName))} = {ConvertExpressionIdentifiers(setAction.Expression)};");
                    break;
                case BmTransformUpdateAction updateAction:
                    if (updateAction.Assignments.Count > 0)
                    {
                        var setClauses = string.Join(", ", updateAction.Assignments.Select(a =>
                            $"{NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(a.FieldName))} = {ConvertExpressionIdentifiers(a.Expression)}"));
                        var whereClause = !string.IsNullOrWhiteSpace(updateAction.WhereClause)
                            ? $" WHERE {ConvertExpressionIdentifiers(updateAction.WhereClause)}"
                            : "";
                        sb.AppendLine($"UPDATE {tableName} SET {setClauses}{whereClause};");
                    }
                    break;
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Converts PascalCase identifiers in a raw expression string to snake_case column names.
    /// Preserves string literals, numeric literals, and SQL keywords.
    /// </summary>
    private static string ConvertExpressionIdentifiers(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return expression;

        // Split on string literals to preserve them
        // PostgreSQL escapes single quotes by doubling them (''), not with backslash
        var parts = Regex.Split(expression, @"('(?:[^']|'')*')");
        var sb = new StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                // Odd parts are string literals — preserve as-is
                sb.Append(parts[i]);
            }
            else
            {
                // Even parts are expression code — convert identifiers
                sb.Append(Regex.Replace(parts[i], @"\b([A-Z][a-zA-Z0-9]*)\b", m =>
                {
                    var word = m.Groups[1].Value;
                    // Skip SQL keywords and boolean/null literals
                    if (_sqlKeywords.Contains(word.ToUpperInvariant()))
                        return word;
                    return NamingConvention.ToSnakeCase(word);
                }));
            }
        }

        return sb.ToString();
    }

    private static readonly HashSet<string> _sqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Logical operators and literals
        "AND", "OR", "NOT", "IN", "IS", "NULL", "TRUE", "FALSE",
        // Conditional expressions
        "LIKE", "BETWEEN", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END",
        // Aliases, casts, functions
        "AS", "CAST", "COALESCE", "NULLIF", "ASC", "DESC",
        // DML statements
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "SET",
        // DDL keywords
        "CREATE", "DROP", "ALTER", "ADD", "COLUMN", "TABLE", "TYPE",
        "PRIMARY", "REFERENCES", "UNIQUE", "CHECK", "CONSTRAINT", "DEFAULT",
        "FOREIGN", "INDEX", "SEQUENCE", "EXTENSION", "TRIGGER", "RULE",
        "VIEW", "FUNCTION", "PROCEDURE", "SCHEMA", "DATABASE",
        // Privilege keywords
        "USER", "ROLE", "GRANT", "REVOKE",
        // Transaction control
        "TRANSACTION", "COMMIT", "ROLLBACK", "SAVEPOINT", "LOCK", "BEGIN",
        // Query clauses
        "DISTINCT", "ALL", "ANY", "SOME", "LATERAL",
        "RETURNING", "LIMIT", "OFFSET", "GROUP", "HAVING", "ORDER",
        "INTO", "VALUES", "ARRAY", "FETCH", "FOR",
        // Joins
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "ON",
        "NATURAL", "USING",
        // Set operations
        "UNION", "EXCEPT", "INTERSECT",
        // Common table expressions and window functions
        "WITH", "RECURSIVE", "WINDOW", "OVER", "PARTITION",
        // Aggregate keywords
        "AGGREGATE"
    };

    private static string MapTypeToPostgres(string bmmdlType)
    {
        // Basic type mapping from BMMDL types to PostgreSQL types
        var type = bmmdlType.Trim();
        if (type.StartsWith("String", StringComparison.OrdinalIgnoreCase))
        {
            var parenIndex = type.IndexOf('(');
            if (parenIndex >= 0) return "varchar" + type.Substring(parenIndex);
            return "varchar";
        }
        if (type.StartsWith("Decimal", StringComparison.OrdinalIgnoreCase))
        {
            var parenIndex = type.IndexOf('(');
            if (parenIndex >= 0) return "numeric" + type.Substring(parenIndex);
            return "numeric";
        }

        return type.ToUpperInvariant() switch
        {
            "UUID" => "uuid",
            "INTEGER" or "INT" => "integer",
            "BOOLEAN" or "BOOL" => "boolean",
            "DATE" => "date",
            "TIME" => "time",
            "DATETIME" => "timestamp",
            "TIMESTAMP" => "timestamptz",
            "BINARY" => "bytea",
            _ => throw new InvalidOperationException($"Unknown BMMDL type '{type}' cannot be mapped to PostgreSQL")
        };
    }

    private string ComputeChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
    
    /// <summary>
    /// Generate a backup table before destructive operations.
    /// Returns SQL to create backup and a comment describing the operation.
    /// </summary>
    public string GenerateBackupTable(string schema, string tableName, string operationDescription)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupTableName = $"{tableName}_backup_{timestamp}";

        var sb = new StringBuilder();
        sb.AppendLine($"-- Creating backup before: {operationDescription}");
        sb.AppendLine($"CREATE TABLE {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(backupTableName)} AS SELECT * FROM {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(tableName)};");
        var escapedDescription = operationDescription.Replace("'", "''");
        sb.AppendLine($"COMMENT ON TABLE {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(backupTableName)} IS 'Backup before: {escapedDescription}';");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a restore script from a backup table.
    /// </summary>
    public string GenerateRestoreScript(string schema, string tableName, string backupTableName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Restore {tableName} from backup {backupTableName}");
        sb.AppendLine("BEGIN;");
        sb.AppendLine($"DROP TABLE IF EXISTS {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(tableName)} CASCADE;");
        sb.AppendLine($"ALTER TABLE {NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(backupTableName)} RENAME TO {NamingConvention.QuoteIdentifier(tableName)};");
        sb.AppendLine("COMMIT;");

        return sb.ToString();
    }
    
    /// <summary>
    /// Generate a complete migration with backup for destructive operations.
    /// Includes backup creation for DROP COLUMN and TYPE changes.
    /// </summary>
    public Migration GenerateSafeMigration(SchemaDiff diff, string migrationName)
    {
        var upScript = GenerateSafeUpScript(diff);
        var downScript = GenerateDownScript(diff);
        
        return new Migration
        {
            Name = migrationName,
            Timestamp = DateTime.UtcNow,
            UpScript = upScript,
            DownScript = downScript,
            Checksum = ComputeChecksum(upScript)
        };
    }
    
    private string GenerateSafeUpScript(SchemaDiff diff)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("-- Migration with automatic backups for destructive operations");
        sb.AppendLine("BEGIN;");
        sb.AppendLine();
        
        // 1. Create backups for tables being dropped
        foreach (var tableName in diff.TablesToDrop)
        {
            var parts = tableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "public";
            var table = parts.Length > 1 ? parts[1] : parts[0];
            sb.AppendLine(GenerateBackupTable(schema, table, $"DROP TABLE {tableName}"));
        }
        
        // 2. Create backups for tables with destructive column changes
        foreach (var tableChange in diff.TablesToModify)
        {
            bool needsBackup = tableChange.ColumnsToDrop.Count > 0 ||
                               tableChange.ColumnsToModify.Any(m => 
                                   m.Changes.Contains(ChangeType.DataTypeChange) ||
                                   m.Changes.Contains(ChangeType.ComputedExpressionChange));
            
            if (needsBackup)
            {
                var operations = new List<string>();
                if (tableChange.ColumnsToDrop.Count > 0)
                    operations.Add($"DROP COLUMN {string.Join(", ", tableChange.ColumnsToDrop)}");
                if (tableChange.ColumnsToModify.Any(m => m.Changes.Contains(ChangeType.DataTypeChange)))
                    operations.Add("TYPE changes");
                    
                sb.AppendLine(GenerateBackupTable(
                    tableChange.Schema, 
                    tableChange.TableName, 
                    string.Join("; ", operations)));
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("-- Apply migration changes");
        sb.AppendLine();
        
        // 3. Drop constraints first (FKs, then others)
        foreach (var tableChange in diff.TablesToModify)
        {
            foreach (var constraint in tableChange.ConstraintsToDrop)
            {
                sb.AppendLine($"ALTER TABLE {NamingConvention.QuoteIdentifier(tableChange.Schema)}.{NamingConvention.QuoteIdentifier(tableChange.TableName)} DROP CONSTRAINT IF EXISTS {NamingConvention.QuoteIdentifier(constraint)};");
            }
        }

        // 4. Drop indexes
        foreach (var tableChange in diff.TablesToModify)
        {
            foreach (var index in tableChange.IndexesToDrop)
            {
                sb.AppendLine($"DROP INDEX IF EXISTS {NamingConvention.QuoteIdentifier(tableChange.Schema)}.{NamingConvention.QuoteIdentifier(index)};");
            }
        }

        // 5. Add new tables
        foreach (var table in diff.TablesToAdd)
        {
            sb.AppendLine(GenerateCreateTable(table));
        }

        // 6. Modify existing tables
        foreach (var tableChange in diff.TablesToModify)
        {
            sb.Append(GenerateAlterTable(tableChange));
        }

        // 7. Drop tables
        foreach (var tableName in diff.TablesToDrop)
        {
            // tableName may be schema.table or just table
            var parts = tableName.Split('.');
            var quotedName = parts.Length > 1
                ? $"{NamingConvention.QuoteIdentifier(parts[0])}.{NamingConvention.QuoteIdentifier(parts[1])}"
                : NamingConvention.QuoteIdentifier(parts[0]);
            sb.AppendLine($"DROP TABLE IF EXISTS {quotedName} CASCADE;");
        }
        
        sb.AppendLine();
        sb.AppendLine("COMMIT;");
        
        return sb.ToString();
    }
}
