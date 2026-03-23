namespace BMMDL.CodeGen;

/// <summary>
/// Registry of built-in type mapping rules
/// </summary>
public static class TypeMappingRegistry
{
    private static readonly Dictionary<string, TypeMappingRule> _rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UUID"] = new TypeMappingRule
        {
            BmmdlTypePattern = "UUID",
            PostgresType = "UUID",
            Strategy = MappingStrategy.Primitive
        },
        ["String"] = new TypeMappingRule
        {
            BmmdlTypePattern = "String(*)?",
            PostgresType = "VARCHAR({0})",
            DefaultParams = [255],
            Strategy = MappingStrategy.Primitive
        },
        ["Integer"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Integer",
            PostgresType = "INTEGER",
            Strategy = MappingStrategy.Primitive
        },
        ["Long"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Long",
            PostgresType = "BIGINT",
            Strategy = MappingStrategy.Primitive
        },
        ["Decimal"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Decimal(*,*)?",
            PostgresType = "NUMERIC({0},{1})",
            DefaultParams = [18, 2],
            Strategy = MappingStrategy.Primitive
        },
        ["Boolean"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Boolean",
            PostgresType = "BOOLEAN",
            Strategy = MappingStrategy.Primitive
        },
        ["Date"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Date",
            PostgresType = "DATE",
            Strategy = MappingStrategy.Primitive
        },
        ["DateTime"] = new TypeMappingRule
        {
            BmmdlTypePattern = "DateTime",
            PostgresType = "TIMESTAMP",
            Strategy = MappingStrategy.Primitive
        },
        ["Timestamp"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Timestamp",
            PostgresType = "TIMESTAMPTZ",
            Strategy = MappingStrategy.Primitive
        },
        ["Time"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Time",
            PostgresType = "TIME",
            Strategy = MappingStrategy.Primitive
        },
        ["Binary"] = new TypeMappingRule
        {
            BmmdlTypePattern = "Binary",
            PostgresType = "BYTEA",
            Strategy = MappingStrategy.Primitive
        },
        ["FileReference"] = new TypeMappingRule
        {
            BmmdlTypePattern = "FileReference",
            PostgresType = "FILE_METADATA_COMPOSITE",  // Special marker, expanded in DDL generator
            Strategy = MappingStrategy.Complex  // Expands to multiple metadata columns
        }
    };

    /// <summary>
    /// Get mapping rule for a BMMDL type
    /// </summary>
    public static TypeMappingRule? GetRule(string bmmdlType)
    {
        return _rules.TryGetValue(bmmdlType, out var rule) ? rule : null;
    }

    /// <summary>
    /// Register or override a type mapping rule
    /// </summary>
    public static void Register(string bmmdlType, TypeMappingRule rule)
    {
        _rules[bmmdlType] = rule;
    }

    /// <summary>
    /// Check if a type has a registered mapping
    /// </summary>
    public static bool HasRule(string bmmdlType)
    {
        return _rules.ContainsKey(bmmdlType);
    }

    /// <summary>
    /// Get all registered type names
    /// </summary>
    public static IEnumerable<string> GetAllTypes()
    {
        return _rules.Keys;
    }
}
