using System.Text;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Compiler.Migration;

/// <summary>
/// Interface for migration script generators.
/// </summary>
public interface IMigrationGenerator
{
    string Generate(ModelDiff diff, MigrationOptions options);
}

/// <summary>
/// Options for migration generation.
/// </summary>
public class MigrationOptions
{
    public string SchemaName { get; set; } = "public";
    public bool IncludeDropStatements { get; set; } = false;
    public bool IncludeTransactions { get; set; } = true;
    public string NamingConvention { get; set; } = "snake_case"; // snake_case or PascalCase
}

/// <summary>
/// Generates PostgreSQL migration scripts from model diffs.
/// </summary>
public class SqlMigrationGenerator : IMigrationGenerator
{
    public string Generate(ModelDiff diff, MigrationOptions options)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"-- Migration from {diff.FromVersion} to {diff.ToVersion}");
        sb.AppendLine($"-- Generated at: {DateTime.UtcNow:O}");
        sb.AppendLine($"-- Change type: {diff.OverallChangeType}");
        sb.AppendLine();
        
        if (options.IncludeTransactions)
            sb.AppendLine("BEGIN;");
        
        sb.AppendLine();
        
        // Process entity changes
        foreach (var entityDiff in diff.EntityChanges)
        {
            GenerateEntityMigration(entityDiff, options, sb);
        }
        
        // Process enum changes (typically need to be before table changes)
        foreach (var enumDiff in diff.EnumChanges)
        {
            GenerateEnumMigration(enumDiff, options, sb);
        }
        
        if (options.IncludeTransactions)
        {
            sb.AppendLine();
            sb.AppendLine("COMMIT;");
        }
        
        return sb.ToString();
    }
    
    private void GenerateEntityMigration(EntityDiff entityDiff, MigrationOptions options, StringBuilder sb)
    {
        var tableName = ToTableName(entityDiff.EntityName, options);
        
        switch (entityDiff.ChangeKind)
        {
            case DiffKind.Added:
                GenerateCreateTable(entityDiff, tableName, options, sb);
                break;
                
            case DiffKind.Removed:
                if (options.IncludeDropStatements)
                {
                    sb.AppendLine($"DROP TABLE IF EXISTS {tableName} CASCADE;");
                }
                else
                {
                    sb.AppendLine($"-- WARNING: Entity {entityDiff.EntityName} was removed");
                    sb.AppendLine($"-- DROP TABLE IF EXISTS {tableName} CASCADE;");
                }
                sb.AppendLine();
                break;
                
            case DiffKind.Modified:
                GenerateAlterTable(entityDiff, tableName, options, sb);
                break;
        }
    }
    
    private void GenerateCreateTable(EntityDiff entityDiff, string tableName, MigrationOptions options, StringBuilder sb)
    {
        sb.AppendLine($"-- Create table: {tableName}");

        if (entityDiff.AddedEntity == null)
        {
            throw new InvalidOperationException(
                $"Cannot generate CREATE TABLE statement for '{tableName}': " +
                $"EntityDiff has ChangeKind=Added but AddedEntity is null. " +
                $"This indicates a bug in ModelDiffEngine.ComputeEntityDiffs().");
        }

        var entity = entityDiff.AddedEntity;
        sb.AppendLine($"CREATE TABLE {tableName} (");
        
        var columns = new List<string>();
        var primaryKeys = new List<string>();
        
        foreach (var field in entity.Fields)
        {
            var columnName = ToColumnName(field.Name, options);
            var sqlType = MapToSqlType(field.TypeString);
            var nullable = field.IsNullable ? "" : " NOT NULL";
            
            if (field.IsKey)
            {
                primaryKeys.Add(columnName);
            }
            
            columns.Add($"    {columnName} {sqlType}{nullable}");
        }
        
        sb.AppendLine(string.Join(",\n", columns));
        
        if (primaryKeys.Count > 0)
        {
            sb.AppendLine($"    , PRIMARY KEY ({string.Join(", ", primaryKeys)})");
        }
        
        sb.AppendLine(");");
        
        // Generate indexes
        foreach (var index in entity.Indexes)
        {
            var indexName = ToIndexName(index.Name, options);
            var indexColumns = string.Join(", ", index.Fields.Select(f => ToColumnName(f, options)));
            var unique = index.IsUnique ? "UNIQUE " : "";
            sb.AppendLine($"CREATE {unique}INDEX {indexName} ON {tableName} ({indexColumns});");
        }
        
        sb.AppendLine();
    }
    
    private void GenerateAlterTable(EntityDiff entityDiff, string tableName, MigrationOptions options, StringBuilder sb)
    {
        sb.AppendLine($"-- Modify table: {tableName}");
        
        foreach (var fieldDiff in entityDiff.FieldChanges)
        {
            var columnName = ToColumnName(fieldDiff.FieldName, options);
            
            switch (fieldDiff.ChangeKind)
            {
                case DiffKind.Added:
                    var sqlType = MapToSqlType(fieldDiff.NewType ?? "String");
                    var nullable = fieldDiff.NewNullable == true ? "" : " NOT NULL";
                    sb.AppendLine($"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlType}{nullable};");
                    break;
                    
                case DiffKind.Removed:
                    if (options.IncludeDropStatements)
                    {
                        sb.AppendLine($"ALTER TABLE {tableName} DROP COLUMN {columnName};");
                    }
                    else
                    {
                        sb.AppendLine($"-- ALTER TABLE {tableName} DROP COLUMN {columnName};");
                    }
                    break;
                    
                case DiffKind.Renamed:
                    var oldColumnName = ToColumnName(fieldDiff.OldFieldName!, options);
                    sb.AppendLine($"ALTER TABLE {tableName} RENAME COLUMN {oldColumnName} TO {columnName};");
                    if (!string.IsNullOrEmpty(fieldDiff.TransformExpression))
                    {
                        sb.AppendLine($"UPDATE {tableName} SET {columnName} = {fieldDiff.TransformExpression};");
                    }
                    break;
                    
                case DiffKind.Modified:
                    if (fieldDiff.OldType != fieldDiff.NewType)
                    {
                        var newSqlType = MapToSqlType(fieldDiff.NewType ?? "String");
                        sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {columnName} TYPE {newSqlType};");
                    }
                    if (fieldDiff.OldNullable != fieldDiff.NewNullable)
                    {
                        var nullability = fieldDiff.NewNullable == true ? "DROP NOT NULL" : "SET NOT NULL";
                        sb.AppendLine($"ALTER TABLE {tableName} ALTER COLUMN {columnName} {nullability};");
                    }
                    if (!string.IsNullOrEmpty(fieldDiff.TransformExpression))
                    {
                        sb.AppendLine($"UPDATE {tableName} SET {columnName} = {fieldDiff.TransformExpression};");
                    }
                    break;
            }
        }
        
        // Index changes
        foreach (var indexDiff in entityDiff.IndexChanges)
        {
            var indexName = ToIndexName(indexDiff.Name, options);
            
            switch (indexDiff.ChangeKind)
            {
                case DiffKind.Added:
                    var columns = string.Join(", ", indexDiff.NewFields.Select(f => ToColumnName(f, options)));
                    sb.AppendLine($"CREATE INDEX {indexName} ON {tableName} ({columns});");
                    break;
                    
                case DiffKind.Removed:
                    sb.AppendLine($"DROP INDEX IF EXISTS {indexName};");
                    break;
                    
                case DiffKind.Modified:
                    sb.AppendLine($"DROP INDEX IF EXISTS {indexName};");
                    var newColumns = string.Join(", ", indexDiff.NewFields.Select(f => ToColumnName(f, options)));
                    sb.AppendLine($"CREATE INDEX {indexName} ON {tableName} ({newColumns});");
                    break;
            }
        }
        
        sb.AppendLine();
    }
    
    private void GenerateEnumMigration(EnumDiff enumDiff, MigrationOptions options, StringBuilder sb)
    {
        var typeName = ToEnumTypeName(enumDiff.EnumName, options);

        switch (enumDiff.ChangeKind)
        {
            case DiffKind.Added:
                if (enumDiff.AddedEnum != null && enumDiff.AddedEnum.Values.Count > 0)
                {
                    var values = string.Join(", ", enumDiff.AddedEnum.Values.Select(v => $"'{v.Name.Replace("'", "''")}'"));
                    sb.AppendLine($"CREATE TYPE {typeName} AS ENUM ({values});");
                }
                else
                {
                    sb.AppendLine($"-- WARNING: Cannot create enum {typeName} - no values defined");
                }
                break;

            case DiffKind.Removed:
                sb.AppendLine($"-- DROP TYPE IF EXISTS {typeName};");
                break;

            case DiffKind.Modified:
                foreach (var valDiff in enumDiff.ValueChanges)
                {
                    if (valDiff.ChangeKind == DiffKind.Added)
                    {
                        sb.AppendLine($"ALTER TYPE {typeName} ADD VALUE '{valDiff.Name.Replace("'", "''")}';");
                    }
                }
                break;
        }

        sb.AppendLine();
    }
    
    private string ToTableName(string entityName, MigrationOptions options)
    {
        // Remove namespace, get last part
        var name = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
        var snaked = options.NamingConvention == "snake_case" ? NamingConvention.ToSnakeCase(name) : name;
        return NamingConvention.QuoteIdentifier(snaked);
    }

    private string ToColumnName(string fieldName, MigrationOptions options)
    {
        var snaked = options.NamingConvention == "snake_case" ? NamingConvention.ToSnakeCase(fieldName) : fieldName;
        return NamingConvention.QuoteIdentifier(snaked);
    }

    private string ToIndexName(string indexName, MigrationOptions options)
    {
        var snaked = options.NamingConvention == "snake_case" ? NamingConvention.ToSnakeCase(indexName) : indexName;
        return NamingConvention.QuoteIdentifier(snaked);
    }

    private string ToEnumTypeName(string enumName, MigrationOptions options)
    {
        var name = enumName.Contains('.') ? enumName.Split('.').Last() : enumName;
        var snaked = options.NamingConvention == "snake_case" ? NamingConvention.ToSnakeCase(name) + "_enum" : name;
        return NamingConvention.QuoteIdentifier(snaked);
    }
    
    private static string MapToSqlType(string bmmdlType)
    {
        // Extract base type and params
        var match = System.Text.RegularExpressions.Regex.Match(bmmdlType, @"(\w+)(\((.+)\))?");
        var baseType = match.Groups[1].Value;
        var param = match.Groups[3].Value;
        
        return baseType.ToLower() switch
        {
            "string" => string.IsNullOrEmpty(param) ? "TEXT" : $"VARCHAR({param})",
            "integer" => "INTEGER",
            "decimal" => string.IsNullOrEmpty(param) ? "NUMERIC(18,2)" : $"NUMERIC({param})",
            "boolean" => "BOOLEAN",
            "date" => "DATE",
            "time" => "TIME",
            "datetime" or "timestamp" => "TIMESTAMP WITH TIME ZONE",
            "uuid" => "UUID",
            "binary" => "BYTEA",
            _ => "TEXT" // Default fallback
        };
    }
}

/// <summary>
/// Generates JSON representation of model diffs.
/// </summary>
public class JsonMigrationGenerator : IMigrationGenerator
{
    public string Generate(ModelDiff diff, MigrationOptions options)
    {
        return System.Text.Json.JsonSerializer.Serialize(diff, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }
}
