using System.Text.RegularExpressions;

namespace BMMDL.MetaModel.Types;

/// <summary>
/// Builds BmTypeReference from ANTLR parse tree or string representation.
/// </summary>
public class BmTypeReferenceBuilder
{
    private static readonly HashSet<string> PrimitiveTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "String", "Integer", "Int", "Decimal", "Boolean", "Bool",
        "Date", "Time", "DateTime", "Timestamp", "UUID", "Binary"
    };

    /// <summary>
    /// Predefined structured types that are always available.
    /// </summary>
    private static readonly HashSet<string> PredefinedStructuredTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileReference"
    };
    
    private static readonly Regex TypeWithParamsRegex = new(
        @"^(?<name>\w+)\s*\(\s*(?<p1>\d+)\s*(,\s*(?<p2>\d+))?\s*\)(?<nullable>\?)?$",
        RegexOptions.Compiled);
    
    private static readonly Regex SimpleTypeRegex = new(
        @"^(?<name>[\w.]+)(?<nullable>\?)?$",
        RegexOptions.Compiled);
    
    private static readonly Regex ArrayTypeRegex = new(
        @"^Array\s*<\s*(?<inner>.+)\s*>(?<nullable>\?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex LocalizedTypeRegex = new(
        @"^localized\s+(?<inner>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse a type reference string into a BmTypeReference.
    /// </summary>
    public BmTypeReference Parse(string typeText)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            throw new ArgumentException("Type text cannot be empty", nameof(typeText));
        
        typeText = typeText.Trim();
        
        // Check for Array<T>
        var arrayMatch = ArrayTypeRegex.Match(typeText);
        if (arrayMatch.Success)
        {
            var innerType = Parse(arrayMatch.Groups["inner"].Value);
            return new BmArrayType(innerType)
            {
                IsNullable = arrayMatch.Groups["nullable"].Success
            };
        }
        
        // Check for localized T
        var localizedMatch = LocalizedTypeRegex.Match(typeText);
        if (localizedMatch.Success)
        {
            var innerType = Parse(localizedMatch.Groups["inner"].Value);
            return new BmLocalizedType(innerType);
        }
        
        // Check for Type(params) - e.g., String(200), Decimal(15,4)
        var paramMatch = TypeWithParamsRegex.Match(typeText);
        if (paramMatch.Success)
        {
            var typeName = paramMatch.Groups["name"].Value;
            var p1 = int.Parse(paramMatch.Groups["p1"].Value);
            var p2 = paramMatch.Groups["p2"].Success ? int.Parse(paramMatch.Groups["p2"].Value) : (int?)null;
            var isNullable = paramMatch.Groups["nullable"].Success;
            
            return CreateTypeWithParams(typeName, p1, p2, isNullable);
        }
        
        // Check for simple type name
        var simpleMatch = SimpleTypeRegex.Match(typeText);
        if (simpleMatch.Success)
        {
            var typeName = simpleMatch.Groups["name"].Value;
            var isNullable = simpleMatch.Groups["nullable"].Success;
            
            return CreateSimpleType(typeName, isNullable);
        }
        
        throw new ArgumentException($"Cannot parse type: {typeText}");
    }
    
    private BmTypeReference CreateTypeWithParams(string typeName, int p1, int? p2, bool isNullable)
    {
        var kind = GetPrimitiveKind(typeName);
        
        if (kind == null)
        {
            // Custom type with parameters (rare but possible)
            return new BmCustomTypeReference(typeName)
            {
                TypeParameters = p2.HasValue ? new List<int> { p1, p2.Value } : new List<int> { p1 },
                IsNullable = isNullable
            };
        }
        
        var primitive = new BmPrimitiveType(kind.Value) { IsNullable = isNullable };
        
        switch (kind.Value)
        {
            case BmPrimitiveKind.String:
            case BmPrimitiveKind.Binary:
                primitive.Length = p1;
                break;
                
            case BmPrimitiveKind.Decimal:
                primitive.Precision = p1;
                primitive.Scale = p2 ?? 0;
                break;
                
            default:
                // Other types don't usually have params, treat as first param
                primitive.Length = p1;
                break;
        }
        
        return primitive;
    }
    
    private BmTypeReference CreateSimpleType(string typeName, bool isNullable)
    {
        // Check for FileReference predefined type
        if (PredefinedStructuredTypes.Contains(typeName))
        {
            if (typeName.Equals("FileReference", StringComparison.OrdinalIgnoreCase))
            {
                return new BmFileReferenceType { IsNullable = isNullable };
            }
        }

        var kind = GetPrimitiveKind(typeName);

        if (kind == null)
        {
            // Custom type reference
            return new BmCustomTypeReference(typeName) { IsNullable = isNullable };
        }

        return new BmPrimitiveType(kind.Value) { IsNullable = isNullable };
    }
    
    private BmPrimitiveKind? GetPrimitiveKind(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "string" => BmPrimitiveKind.String,
            "integer" or "int" => BmPrimitiveKind.Integer,
            "decimal" => BmPrimitiveKind.Decimal,
            "boolean" or "bool" => BmPrimitiveKind.Boolean,
            "date" => BmPrimitiveKind.Date,
            "time" => BmPrimitiveKind.Time,
            "datetime" => BmPrimitiveKind.DateTime,
            "timestamp" => BmPrimitiveKind.Timestamp,
            "uuid" => BmPrimitiveKind.UUID,
            "binary" => BmPrimitiveKind.Binary,
            _ => null
        };
    }
    
    /// <summary>
    /// Check if a type name is a primitive type.
    /// </summary>
    public bool IsPrimitiveType(string typeName)
    {
        return GetPrimitiveKind(typeName) != null;
    }

    /// <summary>
    /// Check if a type name is a predefined structured type.
    /// </summary>
    public bool IsPredefinedStructuredType(string typeName)
    {
        return PredefinedStructuredTypes.Contains(typeName);
    }

    /// <summary>
    /// Check if a type name is recognized (primitive or predefined).
    /// </summary>
    public bool IsRecognizedType(string typeName)
    {
        return IsPrimitiveType(typeName) || IsPredefinedStructuredType(typeName);
    }
}
