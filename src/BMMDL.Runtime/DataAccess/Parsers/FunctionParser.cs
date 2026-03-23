namespace BMMDL.Runtime.DataAccess.Parsers;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Parser for OData function expressions in $filter.
/// Handles: string, math, and date/time functions.
/// </summary>
public static class FunctionParser
{
    /// <summary>Quote a SQL identifier to prevent SQL injection.</summary>
    private static string Q(string id) => NamingConvention.QuoteIdentifier(id);

    #region String Functions

    /// <summary>
    /// Parse concat() function: concat(field1, field2) eq 'value'
    /// </summary>
    public static string? TryParseConcat(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"concat\((\w+),\s*(\w+)\)\s+(eq|ne)\s+'(.+?)'";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col1 = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var col2 = Q(NamingConvention.GetColumnName(match.Groups[2].Value));
            var op = GetSqlOperator(match.Groups[3].Value);
            var value = match.Groups[4].Value;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"CONCAT({col1}, {col2}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse length() function: length(field) op value
    /// </summary>
    public static string? TryParseLength(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"length\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = int.TryParse(match.Groups[3].Value, out var v3) ? v3 : 0;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"LENGTH({col}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse indexof() function: indexof(field, 'substr') op value
    /// </summary>
    public static string? TryParseIndexOf(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"indexof\((\w+),\s*'(.+?)'\)\s+(eq|ne|gt|ge|lt|le)\s+(-?\d+)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var substr = match.Groups[2].Value;
            var op = GetSqlOperator(match.Groups[3].Value);
            var value = int.TryParse(match.Groups[4].Value, out var v4) ? v4 : 0;
            var paramName1 = $"@p{parameterIndex++}";
            var paramName2 = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName1, substr));
            parameters.Add(new NpgsqlParameter(paramName2, value));
            // PostgreSQL POSITION returns 1-based, OData indexof returns 0-based
            return $"(POSITION({paramName1} IN {col}) - 1) {op} {paramName2}";
        }
        return null;
    }

    /// <summary>
    /// Parse substring() function: substring(field, start, length) eq 'value'
    /// </summary>
    public static string? TryParseSubstring(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"substring\((\w+),\s*(\d+)(?:,\s*(\d+))?\)\s+(eq|ne)\s+'(.+?)'";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var start = (int.TryParse(match.Groups[2].Value, out var v2) ? v2 : 0) + 1; // OData 0-based, PostgreSQL 1-based
            var op = GetSqlOperator(match.Groups[4].Value);
            var value = match.Groups[5].Value;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));

            if (match.Groups[3].Success)
            {
                var length = int.TryParse(match.Groups[3].Value, out var v3) ? v3 : 0;
                return $"SUBSTRING({col} FROM {start} FOR {length}) {op} {paramName}";
            }
            return $"SUBSTRING({col} FROM {start}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse matchesPattern() function: matchesPattern(field, 'regex')
    /// </summary>
    public static string? TryParseMatchesPattern(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"matchesPattern\((\w+),\s*'(.+?)'\)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var regex = match.Groups[2].Value;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, regex));
            return $"{col} ~ {paramName}";
        }
        return null;
    }

    #endregion

    #region Math Functions

    /// <summary>
    /// Parse round() function: round(field) op value
    /// </summary>
    public static string? TryParseRound(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"round\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+(?:\.\d+)?)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = decimal.Parse(match.Groups[3].Value);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"ROUND({col}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse floor() function: floor(field) op value
    /// </summary>
    public static string? TryParseFloor(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"floor\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+(?:\.\d+)?)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = decimal.Parse(match.Groups[3].Value);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"FLOOR({col}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse ceiling() function: ceiling(field) op value
    /// </summary>
    public static string? TryParseCeiling(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"ceiling\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+(?:\.\d+)?)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = decimal.Parse(match.Groups[3].Value);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"CEILING({col}) {op} {paramName}";
        }
        return null;
    }

    #endregion

    #region Date/Time Functions

    /// <summary>
    /// Parse date() function: date(field) op value
    /// </summary>
    public static string? TryParseDate(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"date\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d{4}-\d{2}-\d{2})";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = DateTime.Parse(match.Groups[3].Value);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"{col}::DATE {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse time() function: time(field) op value
    /// </summary>
    public static string? TryParseTime(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"time\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+'?(\d{2}:\d{2}(?::\d{2})?)'?";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = match.Groups[3].Value;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, TimeSpan.Parse(value)));
            return $"{col}::TIME {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse now() function: field op now()
    /// </summary>
    public static string? TryParseNow(string expression)
    {
        var pattern = @"(\w+)\s+(eq|ne|gt|ge|lt|le)\s+now\(\)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            return $"{col} {op} NOW()";
        }
        return null;
    }

    /// <summary>
    /// Parse date part functions: year/month/day/hour/minute/second(field) op value
    /// </summary>
    public static string? TryParseDatePart(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"(year|month|day|hour|minute|second)\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var part = match.Groups[1].Value.ToUpperInvariant();
            var col = Q(NamingConvention.GetColumnName(match.Groups[2].Value));
            var op = GetSqlOperator(match.Groups[3].Value);
            var value = int.TryParse(match.Groups[4].Value, out var v4) ? v4 : 0;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"EXTRACT({part} FROM {col}) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse fractionalseconds() function: fractionalseconds(field) op value
    /// </summary>
    public static string? TryParseFractionalSeconds(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"fractionalseconds\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(\d+(?:\.\d+)?)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = decimal.Parse(match.Groups[3].Value);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"(EXTRACT(MILLISECONDS FROM {col}) / 1000.0) {op} {paramName}";
        }
        return null;
    }

    /// <summary>
    /// Parse totaloffsetminutes() function: totaloffsetminutes(field) op value
    /// </summary>
    public static string? TryParseTotalOffsetMinutes(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var pattern = @"totaloffsetminutes\((\w+)\)\s+(eq|ne|gt|ge|lt|le)\s+(-?\d+)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var col = Q(NamingConvention.GetColumnName(match.Groups[1].Value));
            var op = GetSqlOperator(match.Groups[2].Value);
            var value = int.TryParse(match.Groups[3].Value, out var v3) ? v3 : 0;
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));
            return $"(EXTRACT(TIMEZONE FROM {col}) / 60) {op} {paramName}";
        }
        return null;
    }

    #endregion

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
}
