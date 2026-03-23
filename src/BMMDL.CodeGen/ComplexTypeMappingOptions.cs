namespace BMMDL.CodeGen;

/// <summary>
/// Strategy for mapping complex/structured types
/// </summary>
public enum ComplexTypeStrategy
{
    /// <summary>Store as JSONB column (flexible, harder to query)</summary>
    JSONB,
    
    /// <summary>Flatten into multiple columns with prefix (query-friendly)</summary>
    Flatten
}

/// <summary>
/// Configuration for complex type mapping
/// </summary>
public class ComplexTypeMappingOptions
{
    /// <summary>Default strategy for structured types</summary>
    public ComplexTypeStrategy DefaultStrategy { get; set; } = ComplexTypeStrategy.Flatten;
    
    /// <summary>Field count threshold: types with more fields use JSONB</summary>
    public int FlattenFieldThreshold { get; set; } = 10;
    
    /// <summary>Override strategy for specific types</summary>
    public Dictionary<string, ComplexTypeStrategy> Overrides { get; set; } = new();
    
    /// <summary>
    /// Determine strategy for a given type
    /// </summary>
    public ComplexTypeStrategy GetStrategy(string typeName, int fieldCount)
    {
        // Check override first
        if (Overrides.TryGetValue(typeName, out var strategy))
        {
            return strategy;
        }
        
        // If too many fields, force JSONB
        if (fieldCount > FlattenFieldThreshold)
        {
            return ComplexTypeStrategy.JSONB;
        }
        
        return DefaultStrategy;
    }
}
