using System.Text;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Enums;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Services;

namespace BMMDL.CodeGen.Migration;

/// <summary>
/// Helper for generating SQL migration scripts for computed fields.
/// Handles safe formula changes and strategy conversions.
/// </summary>
public class ComputedFieldMigrationHelper
{
    private readonly MetaModelCache _cache;
    public ComputedFieldMigrationHelper(MetaModelCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Generates a safe migration script for changing a computed field's formula.
    /// Uses the "v2 column" pattern to avoid downtime/locking issues.
    /// </summary>
    public string GenerateSafeFormulaChange(BmEntity entity, BmField field, string newFormula, ComputedStrategy strategy)
    {
        var sb = new StringBuilder();
        var tableName = NamingConvention.GetTableName(entity);
        var quotedTable = QuoteTableName(tableName);
        var columnName = NamingConvention.GetColumnName(field.Name);
        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);
        var v2ColumnName = $"{columnName}_v2";
        var quotedV2Column = NamingConvention.QuoteIdentifier(v2ColumnName);

        // Step 1: Add new column with v2 suffix
        sb.AppendLine($"-- Step 1: Add new computed column with v2 suffix");
        sb.Append($"ALTER TABLE {quotedTable} ADD COLUMN {quotedV2Column} ");
        
        // Resolve type for the column
        var resolver = new TypeResolver(_cache);
        var resolvedType = resolver.Resolve(field);
        sb.Append($"{resolvedType.PostgresType} ");

        // Add GENERATED clause
        // Note: In a real implementation, we'd use ExpressionTranslator here.
        // For MVP/Helper, we assume newFormula is already translated or simple enough.
        // Ideally receiving the translated SQL expression would be better.
        ValidateFormula(newFormula);
        sb.Append($"GENERATED ALWAYS AS ({newFormula}) ");

        if (strategy == ComputedStrategy.Stored)
        {
            sb.Append("STORED");
        }
        else
        {
             // Virtual is default, but explicit allows clarity if needed in some DBs. 
             // Postgres syntax is just 'GENERATED ALWAYS AS (...) STORED' for stored, 
             // or 'GENERATED ALWAYS AS (...)' for virtual.
        }
        
        sb.AppendLine(";");
        
        // Step 2: Swap instructions (Commented out as strict manual step usually)
        sb.AppendLine();
        sb.AppendLine($"-- Step 2: Swap Columns (Execute when data is verified)");
        sb.AppendLine($"-- ALTER TABLE {quotedTable} DROP COLUMN {quotedColumn};");
        sb.AppendLine($"-- ALTER TABLE {quotedTable} RENAME COLUMN {quotedV2Column} TO {quotedColumn};");

        return sb.ToString();
    }

    /// <summary>
    /// Generates script to convert a field's strategy (e.g., Stored -> Virtual).
    /// </summary>
    public string GenerateStrategyConversion(BmEntity entity, BmField field, ComputedStrategy oldStrategy, ComputedStrategy newStrategy, string formula)
    {
        var sb = new StringBuilder();
        var tableName = NamingConvention.GetTableName(entity);
        var quotedTable = QuoteTableName(tableName);
        var columnName = NamingConvention.GetColumnName(field.Name);
        var quotedColumn = NamingConvention.QuoteIdentifier(columnName);

        sb.AppendLine($"-- Converting {field.Name} from {oldStrategy} to {newStrategy}");

        // Postgres requires dropping and re-adding for STORED <-> VIRTUAL changes usually
        sb.AppendLine($"ALTER TABLE {quotedTable} DROP COLUMN {quotedColumn};");

        sb.Append($"ALTER TABLE {quotedTable} ADD COLUMN {quotedColumn} ");
        
        var resolver = new TypeResolver(_cache);
        var resolvedType = resolver.Resolve(field);
        sb.Append($"{resolvedType.PostgresType} ");

        ValidateFormula(formula);
        if (newStrategy == ComputedStrategy.Application)
        {
            // Just a normal column now
            // Maybe update with current value?
            sb.AppendLine(";");
            sb.AppendLine($"UPDATE {quotedTable} SET {quotedColumn} = {formula};");
        }
        else
        {
            sb.Append($"GENERATED ALWAYS AS ({formula}) ");
            if (newStrategy == ComputedStrategy.Stored)
            {
                sb.Append("STORED");
            }
            sb.AppendLine(";");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quote a potentially schema-qualified table name, quoting each part separately.
    /// E.g., "platform.sales_order" -> "\"platform\".\"sales_order\""
    /// </summary>
    private static string QuoteTableName(string tableName)
    {
        var dotIdx = tableName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            var schema = tableName[..dotIdx];
            var table = tableName[(dotIdx + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table)}";
        }
        return NamingConvention.QuoteIdentifier(tableName);
    }

    /// <summary>
    /// Validates that a formula string is safe for interpolation into DDL.
    /// Rejects obvious SQL injection patterns.
    /// </summary>
    private static void ValidateFormula(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            throw new ArgumentException("Formula cannot be empty", nameof(formula));

        // Reject obvious injection patterns - semicolons, comments, DDL keywords
        var forbidden = new[] { ";", "--", "/*", "DROP ", "ALTER ", "CREATE ", "TRUNCATE ", "DELETE ", "INSERT ", "UPDATE ", "GRANT ", "REVOKE " };
        foreach (var pattern in forbidden)
        {
            if (formula.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Formula contains forbidden SQL pattern: '{pattern.Trim()}'", nameof(formula));
        }
    }
}
