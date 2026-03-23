namespace BMMDL.Runtime.DataAccess.Parsers;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Parser for OData arithmetic expressions in $filter.
/// Handles: add, sub, mul, div, mod operators.
/// </summary>
public static class ArithmeticParser
{
    /// <summary>
    /// Try to parse an arithmetic expression.
    /// Pattern: field add|sub|mul|div|mod operand op value
    /// </summary>
    /// <param name="expression">Expression to parse.</param>
    /// <param name="parameters">Parameters list to add to.</param>
    /// <param name="parameterIndex">Current parameter index (ref).</param>
    /// <returns>SQL clause if matched, null otherwise.</returns>
    public static string? TryParse(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // Pattern: field add|sub|mul|div|mod operand op value
        // Example: price add 10 gt 100 => (price + 10) > @p0
        var arithOps = new Dictionary<string, string>
        {
            { "add", "+" },
            { "sub", "-" },
            { "mul", "*" },
            { "div", "/" },
            { "mod", "%" }
        };

        foreach (var (odataOp, sqlOp) in arithOps)
        {
            var pattern = $@"^(\w+)\s+{odataOp}\s+(\d+(?:\.\d+)?)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$";
            var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var fieldName = match.Groups[1].Value;
                var columnName = NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(fieldName));
                var arithOperand = match.Groups[2].Value;
                var compOp = GetSqlOperator(match.Groups[3].Value);
                var valueStr = match.Groups[4].Value.Trim();
                var value = ParseValue(valueStr);
                var paramName = $"@p{parameterIndex++}";
                parameters.Add(new NpgsqlParameter(paramName, value));
                
                return $"({columnName} {sqlOp} {arithOperand}) {compOp} {paramName}";
            }
        }

        return null;
    }

    /// <summary>
    /// Transform embedded arithmetic in expression.
    /// Pattern: field add|sub|mul|div|mod operand
    /// </summary>
    public static string TransformExpression(string expression)
    {
        var arithOps = new Dictionary<string, string>
        {
            { " add ", " + " },
            { " sub ", " - " },
            { " mul ", " * " },
            { " div ", " / " },
            { " mod ", " % " }
        };

        var result = expression;
        foreach (var (odataOp, sqlOp) in arithOps)
        {
            if (result.Contains(odataOp, StringComparison.OrdinalIgnoreCase))
            {
                result = Regex.Replace(result, Regex.Escape(odataOp), sqlOp, RegexOptions.IgnoreCase);
            }
        }

        return result;
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
        
        // Try parse as number - int first (more specific), then decimal
        if (int.TryParse(valueStr, out var i))
            return i;
        if (decimal.TryParse(valueStr, out var dec))
            return dec;
        
        return valueStr;
    }
}
