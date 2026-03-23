namespace BMMDL.MetaModel.Types;

/// <summary>
/// Base class for all type references in BMMDL.
/// </summary>
public abstract class BmTypeReference
{
    /// <summary>
    /// Source file where this type reference was defined.
    /// </summary>
    public string? SourceFile { get; set; }
    
    /// <summary>
    /// Line number in source.
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// Whether this type is nullable.
    /// </summary>
    public bool IsNullable { get; set; }
    
    /// <summary>
    /// Get the canonical string representation of this type.
    /// </summary>
    public abstract string ToTypeString();
    
    public override string ToString() => ToTypeString();
}

/// <summary>
/// Primitive/built-in type kinds.
/// </summary>
public enum BmPrimitiveKind
{
    String,
    Integer,
    Decimal,
    Boolean,
    Date,
    Time,
    DateTime,
    Timestamp,
    UUID,
    Binary
}

/// <summary>
/// Represents a primitive/built-in type like String, Integer, Decimal, etc.
/// </summary>
public class BmPrimitiveType : BmTypeReference
{
    public BmPrimitiveKind Kind { get; set; }
    
    /// <summary>
    /// Length constraint for String and Binary types.
    /// </summary>
    public int? Length { get; set; }
    
    /// <summary>
    /// Total number of digits for Decimal type (before + after decimal point).
    /// Default: 18
    /// </summary>
    public int? Precision { get; set; }
    
    /// <summary>
    /// Number of digits after decimal point for Decimal type.
    /// Default: 2
    /// </summary>
    public int? Scale { get; set; }
    
    public BmPrimitiveType(BmPrimitiveKind kind)
    {
        Kind = kind;
    }
    
    /// <summary>
    /// Create a String type with optional length.
    /// </summary>
    public static BmPrimitiveType String(int? length = null) => 
        new(BmPrimitiveKind.String) { Length = length };
    
    /// <summary>
    /// Create an Integer type.
    /// </summary>
    public static BmPrimitiveType Integer() => 
        new(BmPrimitiveKind.Integer);
    
    /// <summary>
    /// Create a Decimal type with precision and scale.
    /// </summary>
    /// <param name="precision">Total digits (default 18)</param>
    /// <param name="scale">Digits after decimal (default 2)</param>
    public static BmPrimitiveType Decimal(int? precision = null, int? scale = null) => 
        new(BmPrimitiveKind.Decimal) 
        { 
            Precision = precision ?? 18, 
            Scale = scale ?? 2 
        };
    
    /// <summary>
    /// Create a Boolean type.
    /// </summary>
    public static BmPrimitiveType Boolean() => 
        new(BmPrimitiveKind.Boolean);
    
    /// <summary>
    /// Create a Date type.
    /// </summary>
    public static BmPrimitiveType Date() => 
        new(BmPrimitiveKind.Date);
    
    /// <summary>
    /// Create a Time type.
    /// </summary>
    public static BmPrimitiveType Time() => 
        new(BmPrimitiveKind.Time);
    
    /// <summary>
    /// Create a DateTime type.
    /// </summary>
    public static BmPrimitiveType DateTime() => 
        new(BmPrimitiveKind.DateTime);
    
    /// <summary>
    /// Create a Timestamp type.
    /// </summary>
    public static BmPrimitiveType Timestamp() => 
        new(BmPrimitiveKind.Timestamp);
    
    /// <summary>
    /// Create a UUID type.
    /// </summary>
    public static BmPrimitiveType UUID() => 
        new(BmPrimitiveKind.UUID);
    
    /// <summary>
    /// Create a Binary type with optional length.
    /// </summary>
    public static BmPrimitiveType Binary(int? length = null) => 
        new(BmPrimitiveKind.Binary) { Length = length };
    
    public override string ToTypeString()
    {
        var result = Kind switch
        {
            BmPrimitiveKind.String when Length.HasValue => $"String({Length})",
            BmPrimitiveKind.Binary when Length.HasValue => $"Binary({Length})",
            BmPrimitiveKind.Decimal when Precision.HasValue && Scale.HasValue => $"Decimal({Precision},{Scale})",
            BmPrimitiveKind.Decimal when Precision.HasValue => $"Decimal({Precision})",
            _ => Kind.ToString()
        };
        
        return IsNullable ? $"{result}?" : result;
    }
    
    /// <summary>
    /// Get the effective precision (with default).
    /// </summary>
    public int EffectivePrecision => Precision ?? 18;
    
    /// <summary>
    /// Get the effective scale (with default).
    /// </summary>
    public int EffectiveScale => Scale ?? 2;
}

/// <summary>
/// Reference to a user-defined type (type alias or struct type).
/// </summary>
public class BmCustomTypeReference : BmTypeReference
{
    /// <summary>
    /// The type name as written in source (may be qualified).
    /// </summary>
    public string TypeName { get; set; } = "";
    
    /// <summary>
    /// Type parameters if generic (e.g., for future use).
    /// </summary>
    public List<int>? TypeParameters { get; set; }
    
    /// <summary>
    /// Resolved type definition (populated after type resolution phase).
    /// </summary>
    public object? ResolvedType { get; set; }  // Will be BmType from Parser
    
    public BmCustomTypeReference(string typeName)
    {
        TypeName = typeName;
    }
    
    /// <summary>
    /// Whether this type has been resolved.
    /// </summary>
    public bool IsResolved => ResolvedType != null;
    
    public override string ToTypeString()
    {
        var result = TypeName;
        if (TypeParameters?.Count > 0)
        {
            result += $"({string.Join(",", TypeParameters)})";
        }
        return IsNullable ? $"{result}?" : result;
    }
}

/// <summary>
/// Reference to an entity type (for associations/compositions).
/// </summary>
public class BmEntityTypeReference : BmTypeReference
{
    /// <summary>
    /// The entity name as written in source.
    /// </summary>
    public string EntityName { get; set; } = "";
    
    /// <summary>
    /// Resolved entity definition (populated after resolution phase).
    /// </summary>
    public object? ResolvedEntity { get; set; }  // Will be BmEntity from Parser
    
    public BmEntityTypeReference(string entityName)
    {
        EntityName = entityName;
    }
    
    public bool IsResolved => ResolvedEntity != null;
    
    public override string ToTypeString()
    {
        return IsNullable ? $"{EntityName}?" : EntityName;
    }
}

/// <summary>
/// Array type: Array<ElementType>
/// </summary>
public class BmArrayType : BmTypeReference
{
    public BmTypeReference ElementType { get; set; }
    
    public BmArrayType(BmTypeReference elementType)
    {
        ElementType = elementType;
    }
    
    public override string ToTypeString()
    {
        return $"Array<{ElementType.ToTypeString()}>";
    }
}

/// <summary>
/// Localized type wrapper for multi-language support.
/// </summary>
public class BmLocalizedType : BmTypeReference
{
    public BmTypeReference InnerType { get; set; }
    
    public BmLocalizedType(BmTypeReference innerType)
    {
        InnerType = innerType;
    }
    
    public override string ToTypeString()
    {
        return $"localized {InnerType.ToTypeString()}";
    }
}
