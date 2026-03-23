using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Services;

namespace BMMDL.CodeGen;

/// <summary>
/// Resolves BMMDL types to PostgreSQL types using metadata
/// </summary>
public class TypeResolver
{
    private readonly MetaModelCache _cache;
    private readonly ComplexTypeMappingOptions _complexTypeOptions;
    
    public TypeResolver(MetaModelCache cache, ComplexTypeMappingOptions? complexTypeOptions = null)
    {
        _cache = cache;
        _complexTypeOptions = complexTypeOptions ?? new ComplexTypeMappingOptions();
    }
    
    /// <summary>
    /// Resolve a field's type to PostgreSQL
    /// </summary>
    public ResolvedType Resolve(BmField field)
    {
        var typeName = field.TypeString;
        
        // Strip "localized" prefix — localized fields use the same column type as their inner type.
        // The _texts companion table is generated separately by PostgresDdlGenerator.
        if (typeName.StartsWith("localized ", StringComparison.OrdinalIgnoreCase))
        {
            typeName = typeName.Substring("localized ".Length).Trim();
            if (string.IsNullOrEmpty(typeName))
            {
                var hasDbDefaultLocalized = !string.IsNullOrEmpty(field.DefaultValueString) &&
                                            !field.DefaultValueString.StartsWith("$");
                var isNullableLocalized = field.IsNullable || !hasDbDefaultLocalized;
                return new ResolvedType
                {
                    PostgresType = "VARCHAR(255)",
                    Strategy = MappingStrategy.Primitive,
                    Nullable = isNullableLocalized,
                    DefaultValue = SafeDefaultValue(field.DefaultValueString)
                };
            }
        }
        
        // Step 0: Check if it's an array type
        // Array<T> maps to PostgreSQL T[] (e.g., Array<String(100)> → VARCHAR(100)[])
        // Check TypeRef first (freshly compiled models), then TypeString (registry-loaded models)
        if (field.TypeRef is BmArrayType arrayType)
        {
            return ResolveArrayType(arrayType, field);
        }
        if (typeName.StartsWith("Array<", StringComparison.OrdinalIgnoreCase) && typeName.EndsWith(">"))
        {
            // Parse Array<ElementType> from TypeString when TypeRef is not available
            var elementTypeString = typeName[6..^1].Trim();
            var builder = new BmTypeReferenceBuilder();
            var parsedArrayType = new BmArrayType(builder.Parse(elementTypeString));
            return ResolveArrayType(parsedArrayType, field);
        }
        
        // Step 1: Check if it's a type alias (BmType)
        if (_cache.Types.TryGetValue(typeName, out var typeAlias))
        {
            return ResolveTypeAlias(typeAlias, field);
        }
        
        // Step 2: Check if it's an enum
        if (_cache.Enums.TryGetValue(typeName, out var enumType))
        {
            return ResolveEnum(enumType, field);
        }
        
        // Step 3: Check if it's a primitive type
        var baseTypeName = ExtractBaseType(typeName);
        var rule = TypeMappingRegistry.GetRule(baseTypeName);
        if (rule != null)
        {
            return ResolvePrimitive(rule, typeName, field);
        }
        
        // Fallback: treat as VARCHAR
        // Same nullable inference: fields without DB-level default are nullable
        // DSL expressions ($now, $user, etc.) don't generate DB defaults, so treat as no-default
        var hasDbDefault = !string.IsNullOrEmpty(field.DefaultValueString) && 
                           !field.DefaultValueString.StartsWith("$");
        var isNullable = field.IsNullable || !hasDbDefault;
        return new ResolvedType
        {
            PostgresType = "VARCHAR(255)",
            Strategy = MappingStrategy.Primitive,
            Nullable = isNullable,
            DefaultValue = SafeDefaultValue(field.DefaultValueString)
        };
    }
    
    private ResolvedType ResolveTypeAlias(BmType typeAlias, BmField field)
    {
        // Check if it's a structured type (has Fields)
        if (typeAlias.Fields.Count > 0)
        {
            var strategy = _complexTypeOptions.GetStrategy(
                typeAlias.Name, 
                typeAlias.Fields.Count
            );
            
            if (strategy == ComplexTypeStrategy.JSONB)
            {
                // Store as JSONB
                // DSL expressions don't generate DB defaults
                var hasDbDefaultJsonb = !string.IsNullOrEmpty(field.DefaultValueString) && 
                                   !field.DefaultValueString.StartsWith("$");
                var isNullableJsonb = field.IsNullable || !hasDbDefaultJsonb;
                return new ResolvedType
                {
                    PostgresType = "JSONB",
                    Strategy = MappingStrategy.Complex,
                    Nullable = isNullableJsonb,
                    DefaultValue = SafeDefaultValue(field.DefaultValueString)
                };
            }
            else // Flatten
            {
                // Flatten into multiple columns
                var flattenedFields = new List<FlattenedField>();
                var prefix = NamingConvention.GetColumnName(field.Name);
                
                foreach (var structField in typeAlias.Fields)
                {
                    var resolvedField = Resolve(structField);
                    flattenedFields.Add(new FlattenedField
                    {
                        ColumnName = $"{prefix}_{NamingConvention.GetColumnName(structField.Name)}",
                        PostgresType = resolvedField.PostgresType,
                        Nullable = resolvedField.Nullable,
                        DefaultValue = resolvedField.DefaultValue
                    });
                }
                
                return new ResolvedType
                {
                    PostgresType = "FLATTENED", // Special marker
                    Strategy = MappingStrategy.Complex,
                    Nullable = field.IsNullable,
                    FlattenedFields = flattenedFields
                };
            }
        }
        
        // If it has a BaseType, resolve it recursively with parameters
        if (!string.IsNullOrEmpty(typeAlias.BaseType))
        {
            // Build type string with parameters
            var typeString = BuildTypeString(typeAlias);
            
            var baseField = new BmField
            {
                Name = field.Name,
                TypeString = typeString,
                IsNullable = field.IsNullable,
                DefaultValueString = field.DefaultValueString
            };
            
            return Resolve(baseField);
        }
        
        // Otherwise treat as VARCHAR
        // DSL expressions don't generate DB defaults
        var hasDbDefaultFallback = !string.IsNullOrEmpty(field.DefaultValueString) && 
                           !field.DefaultValueString.StartsWith("$");
        var isFieldNullable = field.IsNullable || !hasDbDefaultFallback;
        return new ResolvedType
        {
            PostgresType = "VARCHAR(255)",
            Strategy = MappingStrategy.Primitive,
            Nullable = isFieldNullable,
            DefaultValue = SafeDefaultValue(field.DefaultValueString)
        };
    }
    
    private string BuildTypeString(BmType typeAlias)
    {
        var baseType = typeAlias.BaseType;
        
        // type Amount : Decimal(15, 2)
        if (typeAlias.Precision.HasValue &&  typeAlias.Scale.HasValue)
        {
            return $"{baseType}({typeAlias.Precision},{typeAlias.Scale})";
        }
        
        // type Name : String(200)
        if (typeAlias.Length.HasValue)
        {
            return $"{baseType}({typeAlias.Length})";
        }
        
        // No parameters
        return baseType;
    }
    
    private ResolvedType ResolveEnum(BmEnum enumType, BmField field)
    {
        // Get all enum values (escape single quotes to prevent SQL injection)
        var values = string.Join("', '", enumType.Values.Select(v => v.Name.Replace("'", "''")));
        
        // Check if default value is a valid enum value (not a column reference)
        // SafeDefaultValue already quotes enum values (e.g., #Active → 'Active')
        var formattedDefault = SafeDefaultValue(field.DefaultValueString);
        
        // Same nullable inference applies
        // DSL expressions don't generate DB defaults
        var hasDbDefault = !string.IsNullOrEmpty(field.DefaultValueString) && 
                           !field.DefaultValueString.StartsWith("$");
        var isNullable = field.IsNullable || !hasDbDefault;
        
        // Use VARCHAR with CHECK constraint
        var resolved = new ResolvedType
        {
            PostgresType = "VARCHAR(50)",
            Strategy = MappingStrategy.Enum,
            Nullable = isNullable,
            DefaultValue = formattedDefault
        };
        
        // Add CHECK constraint
        var columnName = NamingConvention.GetColumnName(field.Name);
        resolved.Constraints.Add($"CHECK ({NamingConvention.QuoteIdentifier(columnName)} IN ('{values}'))");
        
        return resolved;
    }
    
    private ResolvedType ResolvePrimitive(TypeMappingRule rule, string fullTypeName, BmField field)
    {
        // Extract parameters from type name (e.g., String(100) → [100])
        var parameters = ExtractTypeParameters(fullTypeName);
        
        // Format the PostgreSQL type
        var postgresType = parameters.Length > 0 
            ? rule.Format(parameters) 
            : rule.Format();
        
        // Infer nullable: if field has no DB-level default value, treat it as nullable
        // Key fields are handled separately in DDL generator (always NOT NULL)
        // Fields with explicit default values don't need to be nullable
        // DSL expressions ($now, $user, etc.) don't generate DB defaults, so treat as no-default
        var hasDbDefault = !string.IsNullOrEmpty(field.DefaultValueString) && 
                           !field.DefaultValueString.StartsWith("$");
        var isNullable = field.IsNullable || !hasDbDefault;
        
        var resolved = new ResolvedType
        {
            PostgresType = postgresType,
            Strategy = rule.Strategy,
            Nullable = isNullable,
            DefaultValue = FormatDefaultValue(field.DefaultValueString, rule.Strategy)
        };
        
        return resolved;
    }

    /// <summary>
    /// Resolve an array type to PostgreSQL array syntax (e.g., Array&lt;String(100)&gt; → VARCHAR(100)[])
    /// </summary>
    private ResolvedType ResolveArrayType(BmArrayType arrayType, BmField field)
    {
        // Resolve the element type by creating a synthetic field
        var elementField = new BmField
        {
            Name = field.Name,
            TypeString = arrayType.ElementType.ToTypeString(),
            TypeRef = arrayType.ElementType,
            IsNullable = false, // Element nullability is separate from array nullability
            DefaultValueString = null
        };
        
        var elementResolved = Resolve(elementField);
        
        // PostgreSQL array syntax: base_type[] (e.g., VARCHAR(100)[], INTEGER[], UUID[])
        var hasDbDefault = !string.IsNullOrEmpty(field.DefaultValueString) && 
                           !field.DefaultValueString.StartsWith("$");
        var isNullable = field.IsNullable || !hasDbDefault;
        
        return new ResolvedType
        {
            PostgresType = $"{elementResolved.PostgresType}[]",
            Strategy = MappingStrategy.Primitive,
            Nullable = isNullable,
            DefaultValue = FormatDefaultValue(field.DefaultValueString, MappingStrategy.Primitive)
        };
    }
    
    private string ExtractBaseType(string typeName)
    {
        // String(100) → String
        // Decimal(18,2) → Decimal
        var openParen = typeName.IndexOf('(');
        return openParen > 0 ? typeName[..openParen] : typeName;
    }
    
    private object[] ExtractTypeParameters(string typeName)
    {
        // String(100) → [100]
        // Decimal(18,2) → [18, 2]
        var openParen = typeName.IndexOf('(');
        if (openParen < 0)
            return [];
        
        var closeParen = typeName.IndexOf(')');
        if (closeParen < 0) return [];
        var paramsStr = typeName.Substring(openParen + 1, closeParen - openParen - 1);
        
        return paramsStr.Split(',')
            .Select(p => p.Trim())
            .Select<string, object>(p => int.TryParse(p, out var i) ? i : p)
            .ToArray();
    }
    
    private string? FormatDefaultValue(string? value, MappingStrategy strategy)
    {
        if (value == null)
            return null;
        
        // Skip DSL expressions - handled by application or triggers
        if (value.StartsWith("$"))
            return null;
        
        // Skip enum tokens like #Active - use the resolved enum value instead
        if (value.StartsWith("#"))
        {
            // Extract enum value name after #
            var enumVal = value[1..].Replace("'", "''");
            return $"'{enumVal}'";
        }

        // Boolean literals
        if (value == "true" || value == "false")
            return value.ToUpper();

        // Numeric literals
        if (int.TryParse(value, out _) || decimal.TryParse(value, out _))
            return value;

        // Function calls like now(), uuid_generate_v4()
        if (value.Contains("()"))
            return value;

        // Already quoted string (from DSL like default 'PCS')
        if (value.StartsWith("'") && value.EndsWith("'"))
            return value;  // Keep as-is

        // String literals - quote them for PostgreSQL
        // This handles cases like 'PCS', 'Active', etc.
        return $"'{value.Replace("'", "''")}'";
    }
    
    /// <summary>
    /// Filter out DSL expressions and format default values for PostgreSQL.
    /// Returns null for DSL expressions that need application/trigger handling.
    /// </summary>
    private string? SafeDefaultValue(string? value)
    {
        if (value == null)
            return null;
        
        // Skip DSL expressions - handled by application or triggers
        // $now, $today, $user, etc.
        if (value.StartsWith("$"))
            return null;
        
        // Handle enum tokens with # prefix - extract and quote the value
        if (value.StartsWith("#"))
        {
            var enumVal = value[1..].Replace("'", "''");
            return $"'{enumVal}'";
        }

        // Boolean literals
        if (value == "true" || value == "false")
            return value.ToUpper();

        // Numeric literals
        if (int.TryParse(value, out _) || decimal.TryParse(value, out _))
            return value;

        // Function calls like now(), uuid_generate_v4()
        if (value.Contains("()"))
            return value;

        // Already quoted string (from DSL like default 'PCS')
        if (value.StartsWith("'") && value.EndsWith("'"))
            return value;  // Keep as-is

        // String literals - quote them for PostgreSQL
        return $"'{value.Replace("'", "''")}'";
    }
}

