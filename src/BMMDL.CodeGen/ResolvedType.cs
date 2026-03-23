namespace BMMDL.CodeGen;

/// <summary>
/// Result of type resolution - a BMMDL type resolved to PostgreSQL type
/// </summary>
public class ResolvedType
{
    /// <summary>The final PostgreSQL type (e.g., "VARCHAR(100)", "NUMERIC(18,2)")</summary>
    public string PostgresType { get; set; } = string.Empty;
    
    /// <summary>The mapping strategy used</summary>
    public MappingStrategy Strategy { get; set; }
    
    /// <summary>Whether the column should be nullable</summary>
    public bool Nullable { get; set; } = true;
    
    /// <summary>Default value for the column</summary>
    public string? DefaultValue { get; set; }
    
    /// <summary>Additional constraints to apply</summary>
    public List<string> Constraints { get; set; } = new();
    
    /// <summary>For flattened structured types: list of columns with prefix</summary>
    public List<FlattenedField>? FlattenedFields { get; set; }
    
    /// <summary>
    /// Generate the full column definition
    /// </summary>
    public string ToColumnDefinition(string columnName)
    {
        var parts = new List<string>
        {
            columnName,
            PostgresType
        };
        
        if (!Nullable)
        {
            parts.Add("NOT NULL");
        }
        
        // Add DEFAULT if value is set (TypeResolver filters DSL expressions via SafeDefaultValue)
        if (DefaultValue != null)
        {
            parts.Add($"DEFAULT {DefaultValue}");
        }
        
        return string.Join(" ", parts);
    }
}
