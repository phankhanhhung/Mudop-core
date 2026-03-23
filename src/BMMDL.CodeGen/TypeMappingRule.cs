namespace BMMDL.CodeGen;

/// <summary>
/// Defines a mapping rule from BMMDL type to PostgreSQL type
/// </summary>
public class TypeMappingRule
{
    /// <summary>BMMDL type pattern (e.g., "String", "String(*)", "Decimal(*,*)")</summary>
    public string BmmdlTypePattern { get; set; } = string.Empty;
    
    /// <summary>PostgreSQL type template with placeholders (e.g., "VARCHAR({0})", "NUMERIC({0},{1})")</summary>
    public string PostgresType { get; set; } = string.Empty;
    
    /// <summary>Default parameters if not specified in BMMDL type</summary>
    public object[]? DefaultParams { get; set; }
    
    /// <summary>Mapping strategy for this type</summary>
    public MappingStrategy Strategy { get; set; }
    
    /// <summary>Additional SQL constraints to apply</summary>
    public string[]? Constraints { get; set; }
    
    /// <summary>
    /// Format the PostgreSQL type with given parameters
    /// </summary>
    public string Format(params object[] parameters)
    {
        var paramsToUse = parameters.Length > 0 ? parameters : (DefaultParams ?? []);
        if (paramsToUse.Length == 0)
        {
            return PostgresType;
        }
        return string.Format(PostgresType, paramsToUse);
    }
}
