using System.Text;
using BMMDL.MetaModel.Structure;

namespace BMMDL.MetaModel.Utilities;

/// <summary>
/// Unified naming conventions for converting BMMDL names to PostgreSQL names.
/// Provides conversions between PascalCase (C#) and snake_case (PostgreSQL).
/// </summary>
public static class NamingConvention
{
    #region Core Conversions

    /// <summary>
    /// Convert PascalCase or camelCase to snake_case.
    /// </summary>
    /// <example>
    /// "SalesOrder" → "sales_order"
    /// "OrderID" → "order_id"
    /// "HTMLParser" → "html_parser"
    /// "employeeCode" → "employee_code"
    /// </example>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var result = new StringBuilder();
        
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            
            if (char.IsUpper(c))
            {
                // Add underscore before uppercase if:
                // - Not at the start
                // - Previous char was lowercase OR
                // - Next char is lowercase (handles "HTMLParser" → "html_parser")
                if (i > 0)
                {
                    var prevIsLower = char.IsLower(name[i - 1]);
                    var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    
                    if (prevIsLower || nextIsLower)
                    {
                        result.Append('_');
                    }
                }
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Convert snake_case to PascalCase.
    /// </summary>
    /// <example>
    /// "sales_order" → "SalesOrder"
    /// "order_id" → "OrderId"
    /// </example>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_');
        var result = new StringBuilder();
        
        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;
                
            result.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                result.Append(part.Substring(1).ToLowerInvariant());
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Pluralize a word (simple English rules).
    /// </summary>
    public static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;
            
        // Common irregular plurals
        var irregulars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = "people",
            ["child"] = "children",
            ["foot"] = "feet",
            ["tooth"] = "teeth",
            ["man"] = "men",
            ["woman"] = "women"
        };
        
        if (irregulars.TryGetValue(word, out var irregular))
            return irregular;
        
        // Words ending in s, x, z, ch, sh
        if (word.EndsWith("s") || word.EndsWith("x") || word.EndsWith("z") ||
            word.EndsWith("ch") || word.EndsWith("sh"))
        {
            return word + "es";
        }
        
        // Words ending in consonant + y
        if (word.Length > 1 && word.EndsWith("y") && !IsVowel(word[^2]))
        {
            return word[..^1] + "ies";
        }
        
        // Words ending in f or fe
        if (word.EndsWith("f"))
        {
            return word[..^1] + "ves";
        }
        if (word.EndsWith("fe"))
        {
            return word[..^2] + "ves";
        }
        
        // Default: add s
        return word + "s";
    }

    private static bool IsVowel(char c)
    {
        return "aeiouAEIOU".Contains(c);
    }

    #endregion

    #region Schema & Table Names

    /// <summary>
    /// Get schema name from module or namespace.
    /// Example: HR module → hr schema
    /// </summary>
    public static string GetSchemaName(string moduleName)
    {
        return ToSnakeCase(moduleName.Replace(".", "_"));
    }

    /// <summary>
    /// Convert entity to table name (without schema prefix).
    /// Example: Employee → employee
    /// </summary>
    public static string GetTableName(BmEntity entity)
    {
        return ToSnakeCase(entity.Name);
    }

    /// <summary>
    /// Get the fully qualified table name for an entity by name.
    /// </summary>
    /// <param name="entityName">Entity name (e.g., "SalesOrder")</param>
    /// <param name="schemaNamespace">Namespace/schema (e.g., "SCM", "Platform")</param>
    /// <returns>Qualified table name (e.g., "scm.sales_order")</returns>
    public static string GetTableName(string entityName, string? schemaNamespace = null)
    {
        var tableName = ToSnakeCase(entityName);
        
        if (string.IsNullOrWhiteSpace(schemaNamespace))
        {
            return tableName;
        }
        
        var schemaName = GetSchemaName(schemaNamespace);
        
        // Also check if schema became empty after conversion
        if (string.IsNullOrWhiteSpace(schemaName))
            return tableName;
            
        return $"{schemaName}.{tableName}";
    }

    /// <summary>
    /// Get fully qualified table name with schema.
    /// Example: HR.Employee → hr.employees
    /// </summary>
    public static string GetQualifiedTableName(BmEntity entity, string? moduleName)
    {
        var table = GetTableName(entity);
        
        if (string.IsNullOrWhiteSpace(moduleName))
            return table;
            
        var schema = GetSchemaName(moduleName);
        
        // Also check if schema became empty after conversion
        if (string.IsNullOrWhiteSpace(schema))
            return table;
            
        return $"{schema}.{table}";
    }

    #endregion

    #region Column Names

    /// <summary>
    /// Convert field name to column name.
    /// Example: employeeCode → employee_code
    /// </summary>
    public static string GetColumnName(string fieldName)
    {
        return ToSnakeCase(fieldName);
    }

    /// <summary>
    /// Convert association to foreign key column name.
    /// Example: department → department_id
    /// </summary>
    public static string GetFkColumnName(string associationName)
    {
        return ToSnakeCase(associationName) + "_id";
    }

    /// <summary>
    /// Get the OData/C# foreign key field name (PascalCase with "Id" suffix).
    /// Example: "Customer" → "CustomerId", "department" → "departmentId"
    /// </summary>
    /// <remarks>
    /// Use this for in-memory data dictionaries where keys match OData conventions.
    /// For SQL column names, use <see cref="GetFkColumnName"/> instead.
    /// </remarks>
    public static string GetFkFieldName(string name)
    {
        return name + "Id";
    }

    /// <summary>
    /// Get the OData/C# foreign key field name in camelCase.
    /// Example: "Department" → "departmentId", "warehouse" → "warehouseId"
    /// </summary>
    public static string GetFkCamelFieldName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLower(name[0]) + name[1..] + "Id";
    }

    #endregion

    #region Constraint & Index Names

    /// <summary>
    /// Convert constraint name.
    /// Example: UniqueEmail → unique_email
    /// </summary>
    public static string GetConstraintName(string constraintName)
    {
        return ToSnakeCase(constraintName);
    }

    /// <summary>
    /// Convert index name.
    /// Example: IdxEmployeeCode → idx_employee_code
    /// </summary>
    public static string GetIndexName(string indexName)
    {
        return ToSnakeCase(indexName);
    }

    /// <summary>
    /// Get CHECK constraint name with chk_ prefix.
    /// </summary>
    public static string GetCheckConstraintName(string constraintName)
    {
        return "chk_" + ToSnakeCase(constraintName);
    }

    /// <summary>
    /// Get UNIQUE constraint name with uq_ prefix.
    /// </summary>
    public static string GetUniqueConstraintName(string constraintName)
    {
        return "uq_" + ToSnakeCase(constraintName);
    }

    /// <summary>
    /// Get FK constraint name with fk_ prefix.
    /// </summary>
    public static string GetFkConstraintName(string tableName, string fieldName)
    {
        return $"fk_{tableName}_{ToSnakeCase(fieldName)}";
    }

    #endregion

    #region SQL Identifiers

    /// <summary>
    /// Quote a PostgreSQL identifier (schema, table, column names).
    /// Escapes internal double quotes by doubling them.
    /// </summary>
    /// <example>
    /// "sales_order" -> "\"sales_order\""
    /// </example>
    public static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    #endregion
}
