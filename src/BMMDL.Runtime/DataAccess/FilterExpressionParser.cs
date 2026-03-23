namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess.Parsers;
using Npgsql;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses OData-style filter expressions into SQL WHERE clauses.
/// Generates parameterized SQL to prevent injection attacks.
/// </summary>
/// <remarks>
/// Supported operators:
/// - Comparison: eq, ne, gt, ge, lt, le, in
/// - String: contains, startswith, endswith, length, indexof, substring, concat
/// - Logical: and, or, not
/// - Null check: eq null, ne null
/// - Date: year, month, day, now
/// </remarks>
public class FilterExpressionParser
{
    private int _parameterIndex;
    private readonly List<NpgsqlParameter> _parameters = new();
    private readonly BmEntity? _entity;
    private readonly ODataFunctionParser _functionParser;
    private readonly ODataLambdaParser _lambdaParser;

    public FilterExpressionParser()
    {
        _functionParser = new ODataFunctionParser(null);
        _lambdaParser = new ODataLambdaParser(null);
    }

    /// <summary>
    /// Create a parser with entity context for accurate FK resolution in any/all lambdas.
    /// </summary>
    public FilterExpressionParser(BmEntity entity)
    {
        _entity = entity;
        _functionParser = new ODataFunctionParser(entity);
        _lambdaParser = new ODataLambdaParser(entity);
    }

    /// <summary>
    /// Parse an OData-style filter expression to SQL WHERE clause.
    /// </summary>
    /// <param name="filter">OData filter expression.</param>
    /// <returns>Tuple of (SQL WHERE clause, parameters).</returns>
    /// <example>
    /// Input: "status eq 'Draft' and amount gt 1000"
    /// Output: ("status = @p0 AND amount > @p1", [@p0='Draft', @p1=1000])
    /// </example>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return ("1=1", Array.Empty<NpgsqlParameter>());

        _parameterIndex = 0;
        _parameters.Clear();

        var sql = ParseExpression(filter.Trim());

        return (sql, _parameters.AsReadOnly());
    }

    /// <summary>
    /// Parse an ORDER BY clause.
    /// </summary>
    /// <param name="orderBy">OData-style order by (e.g., "name asc, createdAt desc").</param>
    /// <param name="entityDef">Optional entity definition for field validation.</param>
    /// <returns>SQL ORDER BY clause.</returns>
    public static string ParseOrderBy(string orderBy, BmEntity? entityDef = null)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return "";

        // Build valid field names set for validation
        HashSet<string>? validFields = null;
        if (entityDef != null)
        {
            validFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in entityDef.Fields)
            {
                validFields.Add(field.Name);
                validFields.Add(NamingConvention.ToSnakeCase(field.Name));
            }
            // Add temporal system columns if entity is temporal
            if (entityDef.IsTemporal)
            {
                validFields.Add("system_start");
                validFields.Add("system_end");
                validFields.Add("SystemStart");
                validFields.Add("SystemEnd");
            }
            // Also add association names as valid (for navigation paths)
            foreach (var assoc in entityDef.Associations)
            {
                validFields.Add(assoc.Name);
                validFields.Add(NamingConvention.ToSnakeCase(assoc.Name));
            }
        }

        var parts = orderBy.Split(',');
        var clauses = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
                continue;

            var fieldName = tokens[0];

            // Validate field name contains only safe SQL identifier characters (letters, digits, underscores, forward slashes)
            // This prevents SQL injection when entityDef is null and field names bypass validation
            if (!Regex.IsMatch(fieldName, @"^[a-zA-Z_][a-zA-Z0-9_/]*$"))
            {
                throw new ArgumentException(
                    $"Invalid $orderby field '{fieldName}': field names must contain only letters, digits, underscores, and forward slashes.");
            }

            var direction = "ASC";
            if (tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase))
            {
                direction = "DESC";
            }

            // Handle navigation path: e.g., "customer/name" → join-qualified reference
            if (fieldName.Contains('/'))
            {
                var segments = fieldName.Split('/');
                // Validate each navigation segment individually
                foreach (var seg in segments)
                {
                    if (!Regex.IsMatch(seg, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                        throw new ArgumentException($"Invalid navigation segment '{seg}' in $orderby.");
                }
                if (segments.Length == 2)
                {
                    // Convert "customer/name" → "customer"."name" (using snake_case table/column, quoted)
                    var navTable = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(segments[0]));
                    var navColumn = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(segments[1]));
                    clauses.Add($"{navTable}.{navColumn} {direction}");
                    continue;
                }
            }

            var columnName = NamingConvention.ToSnakeCase(fieldName);

            // Validate field name against entity definition
            if (validFields != null && !validFields.Contains(fieldName) && !validFields.Contains(columnName))
            {
                throw new ArgumentException(
                    $"Invalid $orderby field '{fieldName}'. " +
                    $"Valid fields for entity '{entityDef!.Name}': {string.Join(", ", entityDef.Fields.Select(f => f.Name))}");
            }

            clauses.Add($"{NamingConvention.QuoteIdentifier(columnName)} {direction}");
        }

        return string.Join(", ", clauses);
    }

    /// <summary>
    /// Parse a literal value from the filter expression.
    /// </summary>
    internal static object ParseValue(string valueStr)
    {
        valueStr = valueStr.Trim();

        // String literal (single or double quotes)
        if ((valueStr.StartsWith("'") && valueStr.EndsWith("'")) ||
            (valueStr.StartsWith("\"") && valueStr.EndsWith("\"")))
        {
            var innerValue = valueStr.Substring(1, valueStr.Length - 2);

            // Try to parse as GUID - important for UUID column comparisons
            if (Guid.TryParse(innerValue, out var guidVal))
                return guidVal;

            return innerValue;
        }

        // Boolean
        if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        // GUID
        if (Guid.TryParse(valueStr, out var guid))
            return guid;

        // Integer
        if (int.TryParse(valueStr, out var intVal))
            return intVal;

        // Decimal
        if (decimal.TryParse(valueStr, out var decVal))
            return decVal;

        // DateTime
        if (DateTime.TryParse(valueStr, out var dateVal))
            return dateVal;

        // Default to string
        return valueStr;
    }

    /// <summary>
    /// Convert OData operator to SQL operator.
    /// </summary>
    internal static string GetSqlOperator(string odataOp) => odataOp.ToLowerInvariant() switch
    {
        "eq" => "=",
        "ne" => "<>",
        "gt" => ">",
        "ge" => ">=",
        "lt" => "<",
        "le" => "<=",
        _ => throw new ArgumentException($"Unknown OData operator: {odataOp}")
    };

    /// <summary>
    /// Resolve a navigation path like "customer/name" to a SQL column name.
    /// Simple fields are converted via ToSnakeCase. Navigation paths with "/" are
    /// split, each segment is snake_cased, and joined with "_".
    /// </summary>
    private static string ResolveNavigationPath(string path)
    {
        if (!path.Contains('/'))
            return NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(path));

        // Navigation path: quote each segment individually for dot-qualified access
        var segments = path.Split('/');
        if (segments.Length == 2)
        {
            // table.column style navigation
            var navTable = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(segments[0]));
            var navColumn = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(segments[1]));
            return $"{navTable}.{navColumn}";
        }

        // Fallback: flatten with underscore and quote the whole thing
        return NamingConvention.QuoteIdentifier(
            string.Join("_", segments.Select(NamingConvention.ToSnakeCase)));
    }

    #region Expression Parsing

    /// <summary>
    /// Parse a filter expression (handles AND/OR/NOT and delegates to sub-parsers).
    /// </summary>
    private string ParseExpression(string expression) => ParseExpression(expression, 0);

    private const int MaxExpressionDepth = 50;

    private string ParseExpression(string expression, int depth)
    {
        if (depth > MaxExpressionDepth)
            throw new InvalidOperationException(
                $"Filter expression exceeds maximum nesting depth of {MaxExpressionDepth}.");
        // Handle parentheses first
        expression = HandleParentheses(expression);

        // Split by OR (lower precedence)
        var orParts = SplitByOperator(expression, " or ");
        if (orParts.Count > 1)
        {
            var orClauses = orParts.Select(p => ParseExpression(p.Trim(), depth + 1));
            return "(" + string.Join(" OR ", orClauses) + ")";
        }

        // Split by AND
        var andParts = SplitByOperator(expression, " and ");
        if (andParts.Count > 1)
        {
            var andClauses = andParts.Select(p => ParseExpression(p.Trim(), depth + 1));
            return string.Join(" AND ", andClauses);
        }

        // Handle NOT
        if (expression.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            var inner = expression.Substring(4).Trim();
            return "NOT (" + ParseExpression(inner, depth + 1) + ")";
        }

        // Handle IN operator: field in ('a','b','c')
        var inMatch = Regex.Match(expression, @"^([\w/]+)\s+in\s*\((.+)\)$", RegexOptions.IgnoreCase);
        if (inMatch.Success)
        {
            return ParseInOperator(inMatch.Groups[1].Value, inMatch.Groups[2].Value);
        }

        // Handle HAS operator for enum flags: status has 'Active'
        var hasMatch = Regex.Match(expression, @"^([\w/]+)\s+has\s+(.+)$", RegexOptions.IgnoreCase);
        if (hasMatch.Success)
        {
            return ParseHasOperator(hasMatch.Groups[1].Value, hasMatch.Groups[2].Value);
        }

        // Try lambda expressions (any/all)
        var lambdaResult = _lambdaParser.TryParse(expression, _parameters, ref _parameterIndex);
        if (lambdaResult != null)
        {
            return lambdaResult;
        }

        // Parse single comparison
        return ParseComparison(expression);
    }

    /// <summary>
    /// Parse a single comparison expression.
    /// Delegates to specialized parsers for arithmetic, functions, and simple comparisons.
    /// </summary>
    private string ParseComparison(string comparison)
    {
        comparison = comparison.Trim();

        // Try arithmetic expressions (add, sub, mul, div, mod)
        var result = ArithmeticParser.TryParse(comparison, _parameters, ref _parameterIndex);
        if (result != null) return result;

        // Try OData function expressions
        result = _functionParser.TryParse(comparison, _parameters, ref _parameterIndex);
        if (result != null) return result;

        // Null checks: field eq null / field ne null
        result = TryParseNullCheck(comparison);
        if (result != null) return result;

        // Simple comparison: field op value
        result = TryParseSimpleComparison(comparison);
        if (result != null) return result;

        throw new ArgumentException($"Unable to parse filter expression: {comparison}");
    }

    /// <summary>
    /// Parse null check: field eq null / field ne null.
    /// </summary>
    private static string? TryParseNullCheck(string comparison)
    {
        var match = Regex.Match(comparison, @"^([\w/]+)\s+(eq|ne)\s+null$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var columnName = ResolveNavigationPath(match.Groups[1].Value);
        var op = match.Groups[2].Value.Equals("eq", StringComparison.OrdinalIgnoreCase) ? "IS NULL" : "IS NOT NULL";
        return $"{columnName} {op}";
    }

    /// <summary>
    /// Parse simple comparison: field op value.
    /// </summary>
    private string? TryParseSimpleComparison(string comparison)
    {
        var match = Regex.Match(comparison, @"^([\w/]+)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var columnName = ResolveNavigationPath(match.Groups[1].Value);
        var sqlOp = GetSqlOperator(match.Groups[2].Value);
        var valueStr = match.Groups[3].Value.Trim();
        var value = ParseValue(valueStr);
        var paramName = $"@p{_parameterIndex++}";
        _parameters.Add(new NpgsqlParameter(paramName, value));
        return $"{columnName} {sqlOp} {paramName}";
    }

    #endregion

    #region Operator Parsing

    /// <summary>
    /// Parse IN operator: field in ('a','b','c').
    /// </summary>
    private string ParseInOperator(string fieldName, string valuesStr)
    {
        var columnName = ResolveNavigationPath(fieldName);
        var values = ParseInValues(valuesStr);

        if (values.Count == 0)
            return "1=0"; // Empty IN always false

        var paramNames = new List<string>();
        foreach (var value in values)
        {
            var paramName = $"@p{_parameterIndex++}";
            _parameters.Add(new NpgsqlParameter(paramName, value));
            paramNames.Add(paramName);
        }

        return $"{columnName} IN ({string.Join(", ", paramNames)})";
    }

    /// <summary>
    /// Parse HAS operator for enum flags: status has 'Active'.
    /// </summary>
    private string ParseHasOperator(string fieldName, string valueStr)
    {
        var columnName = ResolveNavigationPath(fieldName);
        var value = ParseValue(valueStr.Trim());
        var paramName = $"@p{_parameterIndex++}";
        _parameters.Add(new NpgsqlParameter(paramName, value));

        if (value is int or long)
        {
            // Bitwise check for numeric flags
            return $"({columnName} & {paramName}) = {paramName}";
        }

        // String enum - simple equality
        return $"{columnName} = {paramName}";
    }

    /// <summary>
    /// Parse comma-separated values for IN operator.
    /// </summary>
    private static List<object> ParseInValues(string valuesStr)
    {
        var values = new List<object>();
        var depth = 0;
        var inString = false;
        var current = new StringBuilder();

        for (int i = 0; i < valuesStr.Length; i++)
        {
            var c = valuesStr[i];

            // Track string literal boundaries
            if (c == '\'' && !inString)
            {
                inString = true;
                current.Append(c);
                continue;
            }
            if (c == '\'' && inString)
            {
                current.Append(c);
                // Check for escaped quote ('')
                if (i + 1 < valuesStr.Length && valuesStr[i + 1] == '\'')
                {
                    i++;
                    current.Append(valuesStr[i]);
                    continue;
                }
                inString = false;
                continue;
            }

            if (inString)
            {
                current.Append(c);
                continue;
            }

            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                var val = current.ToString().Trim();
                if (!string.IsNullOrEmpty(val))
                    values.Add(ParseValue(val));
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        var lastVal = current.ToString().Trim();
        if (!string.IsNullOrEmpty(lastVal))
            values.Add(ParseValue(lastVal));

        return values;
    }

    #endregion

    #region Parentheses Handling

    /// <summary>
    /// Handle parenthesized expressions by replacing them with placeholders and processing recursively.
    /// Skips function calls like contains(), startswith(), endswith().
    /// </summary>
    private string HandleParentheses(string expression)
    {
        var result = new StringBuilder();
        var depth = 0;
        var parenStart = -1;
        var isFunctionCall = false;
        var inString = false;

        for (int i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            // Track string literal boundaries — parentheses inside strings are not grouping
            if (c == '\'')
            {
                if (!inString)
                {
                    inString = true;
                }
                else
                {
                    // Check for escaped quote ('')
                    if (i + 1 < expression.Length && expression[i + 1] == '\'')
                    {
                        // Escaped quote — append both and skip
                        if (depth == 0 || isFunctionCall) result.Append(c);
                        i++;
                        if (depth == 0 || isFunctionCall) result.Append(expression[i]);
                        continue;
                    }
                    inString = false;
                }

                if (depth == 0 || isFunctionCall)
                    result.Append(c);
                continue;
            }

            if (inString)
            {
                if (depth == 0 || isFunctionCall)
                    result.Append(c);
                continue;
            }

            if (c == '(')
            {
                if (depth == 0)
                {
                    isFunctionCall = IsFunctionCall(expression, i);
                    if (!isFunctionCall)
                    {
                        parenStart = i;
                    }
                }
                depth++;
                if (isFunctionCall && depth > 0)
                {
                    result.Append(c);
                }
            }
            else if (c == ')')
            {
                depth--;
                if (isFunctionCall)
                {
                    result.Append(c);
                    if (depth == 0)
                    {
                        isFunctionCall = false;
                    }
                }
                else if (depth == 0 && parenStart >= 0)
                {
                    var inner = expression.Substring(parenStart + 1, i - parenStart - 1);
                    var parsed = ParseExpression(inner);
                    result.Append("(" + parsed + ")");
                    parenStart = -1;
                }
            }
            else if (depth == 0 || isFunctionCall)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Check if the parenthesis at position is part of a function call.
    /// </summary>
    private static bool IsFunctionCall(string expression, int parenPos)
    {
        if (parenPos == 0)
            return false;

        var funcEnd = parenPos - 1;

        // Skip whitespace
        while (funcEnd >= 0 && char.IsWhiteSpace(expression[funcEnd]))
            funcEnd--;

        if (funcEnd < 0)
            return false;

        // Check for 'in' operator — parentheses after 'in' are value lists, not grouping
        if (funcEnd >= 1)
        {
            var c0 = expression[funcEnd];
            var c1 = funcEnd >= 2 ? expression[funcEnd - 1] : ' ';

            if ((c0 == 'n' || c0 == 'N') && (c1 == 'i' || c1 == 'I'))
            {
                var charBefore = funcEnd >= 2 ? expression[funcEnd - 2] : ' ';
                if (char.IsWhiteSpace(charBefore) || funcEnd <= 1)
                {
                    return true;
                }
            }
        }

        // Check for function names — OData string, date functions, and lambda functions
        var funcNames = new[] {
            "contains", "startswith", "endswith", "tolower", "toupper", "trim",
            "length", "indexof", "substring", "concat",
            "year", "month", "day", "hour", "minute", "second", "now",
            "date", "time", "round", "floor", "ceiling", "matchesPattern",
            "fractionalseconds", "totaloffsetminutes",
            "any", "all"
        };
        foreach (var func in funcNames)
        {
            if (funcEnd >= func.Length - 1)
            {
                var start = funcEnd - func.Length + 1;
                if (start >= 0)
                {
                    var substr = expression.Substring(start, func.Length);
                    if (substr.Equals(func, StringComparison.OrdinalIgnoreCase))
                    {
                        if (start == 0 || !char.IsLetterOrDigit(expression[start - 1]))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Split expression by operator, respecting parentheses.
    /// </summary>
    private static List<string> SplitByOperator(string expression, string op)
    {
        var results = new List<string>();
        var depth = 0;
        var inString = false;
        var lastSplit = 0;

        for (int i = 0; i < expression.Length; i++)
        {
            // Track string literal boundaries (single-quoted)
            if (expression[i] == '\'' && !inString)
            {
                inString = true;
                continue;
            }
            if (expression[i] == '\'' && inString)
            {
                // Check for escaped quote ('')
                if (i + 1 < expression.Length && expression[i + 1] == '\'')
                {
                    i++; // Skip escaped quote
                    continue;
                }
                inString = false;
                continue;
            }

            if (inString) continue;

            if (expression[i] == '(') depth++;
            else if (expression[i] == ')') depth--;
            else if (depth == 0 && i + op.Length <= expression.Length)
            {
                var substring = expression.Substring(i, op.Length);
                if (substring.Equals(op, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(expression.Substring(lastSplit, i - lastSplit));
                    lastSplit = i + op.Length;
                    i += op.Length - 1;
                }
            }
        }

        if (lastSplit < expression.Length)
        {
            results.Add(expression.Substring(lastSplit));
        }

        return results;
    }

    #endregion
}
