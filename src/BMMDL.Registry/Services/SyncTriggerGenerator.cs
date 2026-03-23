using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using System.Text;

namespace BMMDL.Registry.Services;

/// <summary>
/// Generates PostgreSQL sync triggers for dual-version upgrade scenarios.
/// </summary>
public class SyncTriggerGenerator
{
    private readonly string _schemaName;

    public SyncTriggerGenerator(string schemaName = "public")
    {
        _schemaName = schemaName;
    }

    /// <summary>
    /// Generate sync trigger from v2 table to v1 table.
    /// This ensures v1 readers can still access data written to v2.
    /// </summary>
    public SyncTriggerResult GenerateV2ToV1SyncTrigger(
        string entityName,
        BmEntity v1Entity,
        BmEntity v2Entity)
    {
        var tableName = NamingConvention.ToSnakeCase(entityName);
        var v1TableName = $"{_schemaName}.{tableName}";
        var v2TableName = $"{_schemaName}.{tableName}_v2";
        var triggerName = $"sync_{tableName}_v2_to_v1";
        var functionName = $"fn_{triggerName}";

        var result = new SyncTriggerResult
        {
            TriggerName = triggerName,
            FunctionName = functionName,
            V1TableName = v1TableName,
            V2TableName = v2TableName
        };

        // Find common fields (exist in both versions)
        var v1Fields = v1Entity.Fields.ToDictionary(f => f.Name.ToLowerInvariant());
        var v2Fields = v2Entity.Fields.ToDictionary(f => f.Name.ToLowerInvariant());
        var commonFields = v1Fields.Keys.Intersect(v2Fields.Keys).ToList();

        // Build field mapping with type conversions
        var fieldMappings = new List<FieldMapping>();
        foreach (var fieldName in commonFields)
        {
            var v1Field = v1Fields[fieldName];
            var v2Field = v2Fields[fieldName];
            
            fieldMappings.Add(new FieldMapping
            {
                V1ColumnName = NamingConvention.ToSnakeCase(v1Field.Name),
                V2ColumnName = NamingConvention.ToSnakeCase(v2Field.Name),
                V1Type = v1Field.TypeString,
                V2Type = v2Field.TypeString,
                ConversionExpr = GenerateTypeConversion(v2Field, v1Field)
            });
        }

        result.FieldMappings = fieldMappings;

        // Generate trigger function SQL
        result.CreateFunctionSql = GenerateTriggerFunction(result, fieldMappings);
        result.CreateTriggerSql = GenerateCreateTrigger(result);
        result.DropTriggerSql = GenerateDropTrigger(result);

        return result;
    }

    private string GenerateTriggerFunction(SyncTriggerResult result, List<FieldMapping> mappings)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {_schemaName}.{result.FunctionName}()");
        sb.AppendLine("RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");
        
        // Build INSERT ... ON CONFLICT DO UPDATE
        var v1Columns = string.Join(", ", mappings.Select(m => m.V1ColumnName));
        var v2Values = string.Join(", ", mappings.Select(m => 
            string.IsNullOrEmpty(m.ConversionExpr) 
                ? $"NEW.{m.V2ColumnName}" 
                : m.ConversionExpr));
        
        sb.AppendLine($"    INSERT INTO {result.V1TableName} ({v1Columns})");
        sb.AppendLine($"    VALUES ({v2Values})");
        sb.AppendLine("    ON CONFLICT (id) DO UPDATE SET");
        
        var updates = mappings
            .Where(m => m.V1ColumnName != "id") // Don't update PK
            .Select(m => 
                string.IsNullOrEmpty(m.ConversionExpr)
                    ? $"        {m.V1ColumnName} = EXCLUDED.{m.V1ColumnName}"
                    : $"        {m.V1ColumnName} = {m.ConversionExpr.Replace("NEW.", "EXCLUDED.")}");
        
        sb.AppendLine(string.Join(",\n", updates) + ";");
        
        sb.AppendLine("    RETURN NEW;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        
        return sb.ToString();
    }

    private string GenerateCreateTrigger(SyncTriggerResult result) =>
        $"""
        CREATE TRIGGER {result.TriggerName}
        AFTER INSERT OR UPDATE ON {result.V2TableName}
        FOR EACH ROW
        EXECUTE FUNCTION {_schemaName}.{result.FunctionName}();
        """;

    private string GenerateDropTrigger(SyncTriggerResult result) =>
        $"""
        DROP TRIGGER IF EXISTS {result.TriggerName} ON {result.V2TableName};
        DROP FUNCTION IF EXISTS {_schemaName}.{result.FunctionName}();
        """;

    /// <summary>
    /// Generate type conversion expression from v2 type to v1 type.
    /// </summary>
    private string GenerateTypeConversion(BmField v2Field, BmField v1Field)
    {
        var v2Type = v2Field.TypeString ?? "";
        var v1Type = v1Field.TypeString ?? "";
        
        if (v2Type == v1Type)
            return ""; // No conversion needed

        var v2Column = $"NEW.{NamingConvention.ToSnakeCase(v2Field.Name)}";

        // String widening/narrowing
        if (v2Type.StartsWith("String(") && v1Type.StartsWith("String("))
        {
            var v1Length = SqlTypeMapper.ExtractLength(v1Type);
            var v2Length = SqlTypeMapper.ExtractLength(v2Type);

            if (v2Length > v1Length)
            {
                // Truncate to v1 length
                return $"LEFT({v2Column}, {v1Length})";
            }
        }

        // Decimal precision change
        if (v2Type.StartsWith("Decimal(") && v1Type.StartsWith("Decimal("))
        {
            var (_, v1Scale) = SqlTypeMapper.ExtractDecimalParamsTuple(v1Type);
            return $"ROUND({v2Column}, {v1Scale})";
        }

        // Default: cast
        var sqlType = SqlTypeMapper.MapToSqlType(v1Type);
        return $"CAST({v2Column} AS {sqlType})";
    }
}

#region Result Types

public class SyncTriggerResult
{
    public string TriggerName { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string V1TableName { get; set; } = "";
    public string V2TableName { get; set; } = "";
    public string CreateFunctionSql { get; set; } = "";
    public string CreateTriggerSql { get; set; } = "";
    public string DropTriggerSql { get; set; } = "";
    public List<FieldMapping> FieldMappings { get; set; } = new();

    /// <summary>
    /// Get combined create SQL (function + trigger).
    /// </summary>
    public string GetCreateSql() => CreateFunctionSql + "\n\n" + CreateTriggerSql;
}

public class FieldMapping
{
    public string V1ColumnName { get; set; } = "";
    public string V2ColumnName { get; set; } = "";
    public string V1Type { get; set; } = "";
    public string V2Type { get; set; } = "";
    public string? ConversionExpr { get; set; }
}

#endregion
