namespace BMMDL.Runtime.DataAccess.Parsers;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Parser for OData lambda expressions in $filter.
/// Handles: any() and all() collection operators.
/// </summary>
public static class LambdaParser
{
    /// <summary>
    /// Try to parse any/all lambda expression.
    /// Pattern: collection/any(x: x/field op value) or collection/all(x: x/field op value)
    /// </summary>
    /// <param name="expression">Expression to parse.</param>
    /// <param name="parameters">Parameters list to add to.</param>
    /// <param name="parameterIndex">Current parameter index (ref).</param>
    /// <returns>SQL clause if matched, null otherwise.</returns>
    public static string? TryParse(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // Pattern: collection/any(x: x/field op value) or simplified any(collection, predicate)
        var anyAllPattern = @"(\w+)/(any|all)\((\w+):\s*\3/(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+?)\)";
        var match = Regex.Match(expression, anyAllPattern, RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var collectionName = match.Groups[1].Value;
            var lambdaType = match.Groups[2].Value.ToLowerInvariant();
            var alias = match.Groups[3].Value;
            var fieldName = match.Groups[4].Value;
            var op = match.Groups[5].Value;
            var valueStr = match.Groups[6].Value;

            var collectionTable = NamingConvention.GetColumnName(collectionName);
            var columnName = NamingConvention.GetColumnName(fieldName);
            var sqlOp = GetSqlOperator(op);
            var value = ParseValue(valueStr);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));

            // Generate EXISTS subquery with proper FK join to parent entity
            // Note: Uses convention 'parent_id' - for accurate FK lookup, use FilterExpressionParser
            // which has access to entity metadata.
            // The unqualified 'id' is intentional here — this static parser lacks entity context.
            // When entity context is available, ODataLambdaParser qualifies the reference.
            if (lambdaType == "any")
            {
                return $"EXISTS (SELECT 1 FROM {collectionTable} sub WHERE sub.parent_id = id AND sub.{columnName} {sqlOp} {paramName})";
            }
            else // all
            {
                return $"NOT EXISTS (SELECT 1 FROM {collectionTable} sub WHERE sub.parent_id = id AND NOT (sub.{columnName} {sqlOp} {paramName}))";
            }
        }

        // Try simplified any pattern: any(items, i: i/qty gt 0)
        var simpleAnyPattern = @"any\((\w+),\s*(\w+):\s*\2/(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+?)\)";
        match = Regex.Match(expression, simpleAnyPattern, RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var collectionName = match.Groups[1].Value;
            var alias = match.Groups[2].Value;
            var fieldName = match.Groups[3].Value;
            var op = match.Groups[4].Value;
            var valueStr = match.Groups[5].Value;

            var collectionTable = NamingConvention.GetColumnName(collectionName);
            var columnName = NamingConvention.GetColumnName(fieldName);
            var sqlOp = GetSqlOperator(op);
            var value = ParseValue(valueStr);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));

            return $"EXISTS (SELECT 1 FROM {collectionTable} sub WHERE sub.parent_id = id AND sub.{columnName} {sqlOp} {paramName})";
        }

        return null;
    }

    /// <summary>
    /// Transform lambda expression to EXISTS subquery.
    /// </summary>
    public static string TransformExpression(string expression)
    {
        // Basic transformation for embedded lambda expressions
        // This is a simplified version - full implementation would need recursive parsing
        return expression;
    }

    private static string GetSqlOperator(string odataOp) => odataOp.ToLowerInvariant() switch
    {
        "eq" => "=",
        "ne" => "<>",
        "gt" => ">",
        "ge" => ">=",
        "lt" => "<",
        "le" => "<=",
        _ => "="
    };

    private static object ParseValue(string valueStr)
    {
        // Remove quotes for strings
        if ((valueStr.StartsWith("'") && valueStr.EndsWith("'")) ||
            (valueStr.StartsWith("\"") && valueStr.EndsWith("\"")))
        {
            return valueStr[1..^1];
        }
        
        // Try parse as number
        if (decimal.TryParse(valueStr, out var dec))
            return dec;
        if (int.TryParse(valueStr, out var i))
            return i;
        
        return valueStr;
    }
}
