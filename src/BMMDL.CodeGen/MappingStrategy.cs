namespace BMMDL.CodeGen;

/// <summary>
/// Strategy for mapping BMMDL types to PostgreSQL types
/// </summary>
public enum MappingStrategy
{
    /// <summary>Direct mapping to PostgreSQL primitive type</summary>
    Primitive,
    
    /// <summary>Type alias that needs resolution to base type</summary>
    Alias,
    
    /// <summary>Complex/structured type (JSONB or flattened columns)</summary>
    Complex,
    
    /// <summary>Enum type (VARCHAR with CHECK constraint)</summary>
    Enum,
    
    /// <summary>Association to another entity (foreign key)</summary>
    Association,
    
    /// <summary>Composition of child entities (separate table)</summary>
    Composition
}
