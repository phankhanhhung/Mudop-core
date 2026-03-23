using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Runtime.OData;

/// <summary>
/// Shared type mapping utilities for OData metadata generation.
/// Converts BMMDL types to OData EDM types and frontend type strings.
/// </summary>
public static class MetadataTypeMapper
{
    /// <summary>
    /// Maps a BMMDL type string to an OData EDM type string.
    /// Used by CSDL generation for $metadata.
    /// </summary>
    public static string MapToEdmType(string bmmdlType)
    {
        return bmmdlType.ToLowerInvariant() switch
        {
            "string" or "text" => "Edm.String",
            "int" or "integer" or "int32" => "Edm.Int32",
            "long" or "int64" => "Edm.Int64",
            "decimal" or "money" => "Edm.Decimal",
            "float" or "double" => "Edm.Double",
            "bool" or "boolean" => "Edm.Boolean",
            "date" => "Edm.Date",
            "datetime" or "timestamp" => "Edm.DateTimeOffset",
            "time" => "Edm.TimeOfDay",
            "guid" or "uuid" => "Edm.Guid",
            "binary" or "bytes" => "Edm.Binary",
            _ => "Edm.String" // Default
        };
    }

    /// <summary>
    /// Maps a BMMDL type string to a frontend-friendly type name.
    /// Used by JSON metadata endpoints for frontend consumption.
    /// </summary>
    public static string MapBmmdlTypeToFrontend(string typeStr)
    {
        var normalized = typeStr.TrimEnd('?').ToLowerInvariant();
        // Strip parameters like String(100) → string
        var parenIdx = normalized.IndexOf('(');
        if (parenIdx > 0) normalized = normalized[..parenIdx];

        return normalized switch
        {
            "string" or "text" => "String",
            "integer" or "int" => "Integer",
            "decimal" => "Decimal",
            "boolean" or "bool" => "Boolean",
            "date" => "Date",
            "time" => "Time",
            "datetime" => "DateTime",
            "timestamp" => "Timestamp",
            "uuid" or "guid" => "UUID",
            "binary" => "Binary",
            _ => "String"
        };
    }

    /// <summary>
    /// Converts a BMMDL field name to the OData property name format (PascalCase)
    /// matching DynamicRepository's column→property conversion: snake_case → PascalCase.
    /// E.g., "productCode" → "ProductCode", "ID" → "Id", "categoryId" → "CategoryId"
    /// </summary>
    public static string ToODataPropertyName(string fieldName)
    {
        return NamingConvention.ToPascalCase(NamingConvention.ToSnakeCase(fieldName));
    }

    /// <summary>
    /// Parses type parameters from a type string like "Decimal(15,2)" or "String(100)".
    /// Returns the base type name and extracted length/precision/scale.
    /// </summary>
    public static (string baseType, int? length, int? precision, int? scale) ParseTypeParameters(string typeStr)
    {
        var parenIdx = typeStr.IndexOf('(');
        if (parenIdx <= 0 || !typeStr.EndsWith(')'))
            return (typeStr, null, null, null);

        var baseName = typeStr[..parenIdx].Trim();
        var paramsStr = typeStr[(parenIdx + 1)..^1];
        var parts = paramsStr.Split(',', StringSplitOptions.TrimEntries);

        var isStringLike = baseName.Equals("String", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("Binary", StringComparison.OrdinalIgnoreCase);

        if (isStringLike && parts.Length == 1 && int.TryParse(parts[0], out var len))
            return (baseName, len, null, null);

        if (parts.Length >= 1 && int.TryParse(parts[0], out var p))
        {
            int? s = parts.Length >= 2 && int.TryParse(parts[1], out var sv) ? sv : null;
            return (baseName, null, p, s);
        }

        return (baseName, null, null, null);
    }

    /// <summary>
    /// Maps a BmField to its frontend type and facets (maxLength, precision, scale).
    /// Uses the provided type resolver to look up custom type definitions (type aliases).
    /// </summary>
    public static (string type, int? maxLength, int? precision, int? scale) MapFieldType(
        BmField field,
        Func<string, BMMDL.MetaModel.BmType?> typeResolver)
    {
        if (field.TypeRef is BmPrimitiveType primitive)
        {
            var type = primitive.Kind switch
            {
                BmPrimitiveKind.String => "String",
                BmPrimitiveKind.Integer => "Integer",
                BmPrimitiveKind.Decimal => "Decimal",
                BmPrimitiveKind.Boolean => "Boolean",
                BmPrimitiveKind.Date => "Date",
                BmPrimitiveKind.Time => "Time",
                BmPrimitiveKind.DateTime => "DateTime",
                BmPrimitiveKind.Timestamp => "Timestamp",
                BmPrimitiveKind.UUID => "UUID",
                BmPrimitiveKind.Binary => "Binary",
                _ => "String"
            };
            return (type, primitive.Length, primitive.Precision, primitive.Scale);
        }

        if (field.TypeRef is BmArrayType)
            return ("Array", null, null, null);

        // Custom type reference (could be enum or type alias like Amount, Quantity)
        if (field.TypeRef is BmCustomTypeReference customRef)
        {
            // Try to resolve through model's type definitions (handles type aliases)
            var resolvedType = typeResolver(customRef.TypeName);
            if (resolvedType != null && !string.IsNullOrEmpty(resolvedType.BaseType))
            {
                var (baseName, parsedLen, parsedPrec, parsedScale) = ParseTypeParameters(resolvedType.BaseType);
                var frontendType = MapBmmdlTypeToFrontend(baseName);
                return (frontendType,
                    resolvedType.Length ?? parsedLen,
                    resolvedType.Precision ?? parsedPrec,
                    resolvedType.Scale ?? parsedScale);
            }

            return (customRef.TypeName, null, null, null);
        }

        // Fallback: parse from TypeString (TypeRef is often null when loaded from registry)
        var typeStr = (field.TypeString ?? "String").TrimEnd('?');

        // Check if typeStr is a type alias (e.g., "Amount" → "Decimal(15,2)")
        var aliasType = typeResolver(typeStr);
        if (aliasType != null && !string.IsNullOrEmpty(aliasType.BaseType))
        {
            var (resolvedType, parsedLength, parsedPrecision, parsedScale) = ParseTypeParameters(aliasType.BaseType);
            var frontendType = MapBmmdlTypeToFrontend(resolvedType);
            return (frontendType,
                aliasType.Length ?? parsedLength,
                aliasType.Precision ?? parsedPrecision,
                aliasType.Scale ?? parsedScale);
        }

        var frontendType2 = MapBmmdlTypeToFrontend(typeStr);
        return (frontendType2, null, null, null);
    }
}
