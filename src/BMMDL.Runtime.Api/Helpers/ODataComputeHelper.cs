namespace BMMDL.Runtime.Api.Helpers;

using BMMDL.MetaModel.Utilities;
using System.Text.RegularExpressions;

/// <summary>
/// Helper for OData $compute expression evaluation.
/// Extracted from DynamicEntityController to reduce controller size.
/// </summary>
public static class ODataComputeHelper
{
    /// <summary>
    /// Apply $compute expressions to add dynamic computed properties to each item.
    /// Format: "expression as alias, expression2 as alias2"
    /// </summary>
    public static List<Dictionary<string, object?>> ApplyComputedProperties(
        List<Dictionary<string, object?>> items, string computeExpr)
    {
        var computeSpecs = computeExpr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var spec in computeSpecs)
        {
            var trimmed = spec.Trim();
            var asIndex = trimmed.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asIndex < 0) continue;

            var expression = trimmed.Substring(0, asIndex).Trim();
            var alias = trimmed.Substring(asIndex + 4).Trim();

            foreach (var item in items)
            {
                var value = EvaluateComputeExpression(expression, item);
                item[alias] = value;
            }
        }

        return items;
    }

    /// <summary>
    /// Evaluate a single OData $compute expression (add, sub, mul, div, or field reference).
    /// </summary>
    public static object? EvaluateComputeExpression(string expression, Dictionary<string, object?> item)
    {
        var addMatch = Regex.Match(expression,
            @"^(\w+)\s+add\s+(\w+|\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (addMatch.Success)
            return GetNumericValue(addMatch.Groups[1].Value, item) + GetNumericValue(addMatch.Groups[2].Value, item);

        var subMatch = Regex.Match(expression,
            @"^(\w+)\s+sub\s+(\w+|\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (subMatch.Success)
            return GetNumericValue(subMatch.Groups[1].Value, item) - GetNumericValue(subMatch.Groups[2].Value, item);

        var mulMatch = Regex.Match(expression,
            @"^(\w+)\s+mul\s+(\w+|\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (mulMatch.Success)
            return GetNumericValue(mulMatch.Groups[1].Value, item) * GetNumericValue(mulMatch.Groups[2].Value, item);

        var divMatch = Regex.Match(expression,
            @"^(\w+)\s+div\s+(\w+|\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (divMatch.Success)
        {
            var right = GetNumericValue(divMatch.Groups[2].Value, item);
            return right != 0 ? GetNumericValue(divMatch.Groups[1].Value, item) / right : null;
        }

        var fieldName = NamingConvention.ToSnakeCase(expression.Trim());
        return item.TryGetValue(fieldName, out var val) ? val : null;
    }

    private static decimal GetNumericValue(string token, Dictionary<string, object?> item)
    {
        if (decimal.TryParse(token, out var literal)) return literal;
        var fieldName = NamingConvention.ToSnakeCase(token);
        if (item.TryGetValue(fieldName, out var val) && val != null)
            return Convert.ToDecimal(val);
        return 0m;
    }
}
