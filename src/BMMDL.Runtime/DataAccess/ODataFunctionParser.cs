namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Parser for OData function expressions in $filter.
/// Handles string, math, date/time, and pattern matching functions.
/// </summary>
internal class ODataFunctionParser
{
    private readonly BmEntity? _entity;

    public ODataFunctionParser(BmEntity? entity)
    {
        _entity = entity;
    }

    /// <summary>
    /// Quote a SQL identifier for safe interpolation.
    /// </summary>
    private static string Q(string identifier) => NamingConvention.QuoteIdentifier(identifier);

    /// <summary>
    /// Escape LIKE/ILIKE wildcards (%, _) in a user-provided value so they match literally.
    /// </summary>
    private static string EscapeLikeValue(object val)
    {
        var s = val?.ToString() ?? "";
        return s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }

    /// <summary>
    /// Try to parse an OData function expression.
    /// Returns SQL clause if matched, null otherwise.
    /// </summary>
    public string? TryParse(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // String search functions: contains(), startswith(), endswith()
        var result = TryParseContains(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseStartsWith(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseEndsWith(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // String transform functions: tolower(), toupper(), trim()
        result = TryParseStringTransform(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // String property functions: length(), indexof(), substring(), concat()
        result = TryParseLength(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseIndexOf(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseSubstring(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseConcat(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // Pattern matching: matchesPattern()
        result = TryParseMatchesPattern(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // Date/time part functions: year(), month(), day(), hour(), minute(), second()
        result = TryParseDatePart(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // now() comparison: field op now()
        result = TryParseNow(expression);
        if (result != null) return result;

        // date()/time() extraction
        result = TryParseDate(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseTime(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // Temporal: fractionalseconds(), totaloffsetminutes()
        result = TryParseFractionalSeconds(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseTotalOffsetMinutes(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        // Math functions: round(), floor(), ceiling()
        result = TryParseRound(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseFloor(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        result = TryParseCeiling(expression, parameters, ref parameterIndex);
        if (result != null) return result;

        return null;
    }

    #region String Search Functions

    /// <summary>
    /// Parse contains(field, value) — ILIKE %value% or array containment.
    /// </summary>
    private string? TryParseContains(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^contains\s*\(\s*(\w+)\s*,\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var fieldName = match.Groups[1].Value;
        var quotedColumn = Q(NamingConvention.ToSnakeCase(fieldName));

        // Check if the field is an array type — use PostgreSQL array containment
        if (_entity != null)
        {
            var arrayField = _entity.Fields.FirstOrDefault(f =>
                f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (arrayField?.TypeRef is MetaModel.Types.BmArrayType)
            {
                var arrayVal = FilterExpressionParser.ParseValue(match.Groups[2].Value.Trim());
                var arrayParamName = $"@p{parameterIndex++}";
                parameters.Add(new NpgsqlParameter(arrayParamName, arrayVal));
                return $"{arrayParamName} = ANY({quotedColumn})";
            }
        }

        var val = FilterExpressionParser.ParseValue(match.Groups[2].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{EscapeLikeValue(val)}%"));
        return $"{quotedColumn} ILIKE {paramName}";
    }

    /// <summary>
    /// Parse startswith(field, value) — ILIKE value%.
    /// </summary>
    private static string? TryParseStartsWith(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^startswith\s*\(\s*(\w+)\s*,\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var val = FilterExpressionParser.ParseValue(match.Groups[2].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, $"{EscapeLikeValue(val)}%"));
        return $"{quotedColumn} ILIKE {paramName}";
    }

    /// <summary>
    /// Parse endswith(field, value) — ILIKE %value.
    /// </summary>
    private static string? TryParseEndsWith(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^endswith\s*\(\s*(\w+)\s*,\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var val = FilterExpressionParser.ParseValue(match.Groups[2].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{EscapeLikeValue(val)}"));
        return $"{quotedColumn} ILIKE {paramName}";
    }

    #endregion

    #region String Transform Functions

    /// <summary>
    /// Parse tolower/toupper/trim(field) op value.
    /// </summary>
    private static string? TryParseStringTransform(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^(tolower|toupper|trim)\s*\(\s*(\w+)\s*\)\s+(eq|ne)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var funcName = match.Groups[1].Value.ToLowerInvariant();
        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[2].Value));
        var sqlOp = GetSqlOperator(match.Groups[3].Value);
        var val = FilterExpressionParser.ParseValue(match.Groups[4].Value.Trim());

        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, val));

        var sqlFunc = funcName switch
        {
            "tolower" => "LOWER",
            "toupper" => "UPPER",
            "trim" => "TRIM",
            _ => throw new ArgumentException($"Unknown string transform: {funcName}")
        };

        return $"{sqlFunc}({quotedColumn}) {sqlOp} {sqlFunc}({paramName})";
    }

    #endregion

    #region String Property Functions

    /// <summary>
    /// Parse length(field) op value.
    /// </summary>
    private static string? TryParseLength(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^length\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"LENGTH({quotedColumn}) {op} {paramName}";
    }

    /// <summary>
    /// Parse indexof(field, 'sub') op value — PostgreSQL POSITION (0-based).
    /// </summary>
    private static string? TryParseIndexOf(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^indexof\s*\(\s*(\w+)\s*,\s*(.+?)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var searchValue = FilterExpressionParser.ParseValue(match.Groups[2].Value);
        var op = GetSqlOperator(match.Groups[3].Value);
        var compareValue = FilterExpressionParser.ParseValue(match.Groups[4].Value);

        var searchParamName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(searchParamName, searchValue));

        var compareParamName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(compareParamName, compareValue));

        // PostgreSQL POSITION returns 1-based index, OData indexof is 0-based
        return $"(POSITION({searchParamName} IN {quotedColumn}) - 1) {op} {compareParamName}";
    }

    /// <summary>
    /// Parse substring(field, start[, length]) op value.
    /// </summary>
    private static string? TryParseSubstring(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^substring\s*\(\s*(\w+)\s*,\s*(\d+)(?:\s*,\s*(\d+))?\s*\)\s+(eq|ne)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));

        if (!int.TryParse(match.Groups[2].Value, out var startIndex))
            startIndex = 0;

        var hasLength = match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value);
        var op = GetSqlOperator(match.Groups[4].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[5].Value.Trim());

        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));

        // PostgreSQL SUBSTRING is 1-based, OData is 0-based
        var pgStart = startIndex + 1;

        if (hasLength)
        {
            if (!int.TryParse(match.Groups[3].Value, out var length))
                length = 0;
            return $"SUBSTRING({quotedColumn} FROM {pgStart} FOR {length}) {op} {paramName}";
        }
        return $"SUBSTRING({quotedColumn} FROM {pgStart}) {op} {paramName}";
    }

    /// <summary>
    /// Parse concat(field, arg) op value — arg can be field name or literal.
    /// </summary>
    private static string? TryParseConcat(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^concat\s*\(\s*(\w+)\s*,\s*(.+?)\s*\)\s+(eq|ne)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var secondArg = match.Groups[2].Value.Trim();
        var op = GetSqlOperator(match.Groups[3].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[4].Value.Trim());

        // Determine if second arg is a field or a literal
        string secondColumnOrValue;
        if (secondArg.StartsWith("'") || secondArg.StartsWith("\""))
        {
            var literalParamName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(literalParamName, FilterExpressionParser.ParseValue(secondArg)));
            secondColumnOrValue = literalParamName;
        }
        else
        {
            secondColumnOrValue = Q(NamingConvention.ToSnakeCase(secondArg));
        }

        var valueParamName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(valueParamName, value));
        return $"CONCAT({quotedColumn}, {secondColumnOrValue}) {op} {valueParamName}";
    }

    #endregion

    #region Pattern Matching

    /// <summary>
    /// Parse matchesPattern(field, 'regex') — PostgreSQL ~ operator.
    /// </summary>
    private static string? TryParseMatchesPattern(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^matchesPattern\s*\(\s*(\w+)\s*,\s*'([^']+)'\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var pattern = match.Groups[2].Value;
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, pattern));
        return $"{quotedColumn} ~ {paramName}";
    }

    #endregion

    #region Date/Time Functions

    /// <summary>
    /// Parse year/month/day/hour/minute/second(field) op value — EXTRACT().
    /// </summary>
    private static string? TryParseDatePart(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^(year|month|day|hour|minute|second)\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var part = match.Groups[1].Value.ToUpperInvariant();
        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[2].Value));
        var op = GetSqlOperator(match.Groups[3].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[4].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"EXTRACT({part} FROM {quotedColumn}) {op} {paramName}";
    }

    /// <summary>
    /// Parse field op now() — comparison against current timestamp.
    /// </summary>
    private static string? TryParseNow(string expression)
    {
        var match = Regex.Match(expression, @"^(\w+)\s+(eq|ne|gt|ge|lt|le)\s+now\s*\(\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        return $"{quotedColumn} {op} NOW()";
    }

    /// <summary>
    /// Parse date(field) op value — DATE cast.
    /// </summary>
    private static string? TryParseDate(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^date\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"{quotedColumn}::DATE {op} {paramName}";
    }

    /// <summary>
    /// Parse time(field) op value — TIME cast.
    /// </summary>
    private static string? TryParseTime(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^time\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"{quotedColumn}::TIME {op} {paramName}";
    }

    /// <summary>
    /// Parse fractionalseconds(field) op value — milliseconds extraction.
    /// </summary>
    private static string? TryParseFractionalSeconds(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^fractionalseconds\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"(EXTRACT(MILLISECONDS FROM {quotedColumn}) / 1000.0) {op} {paramName}";
    }

    /// <summary>
    /// Parse totaloffsetminutes(field) op value — timezone offset in minutes.
    /// </summary>
    private static string? TryParseTotalOffsetMinutes(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^totaloffsetminutes\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"(EXTRACT(TIMEZONE FROM {quotedColumn}) / 60) {op} {paramName}";
    }

    #endregion

    #region Math Functions

    /// <summary>
    /// Parse round(field) op value.
    /// </summary>
    private static string? TryParseRound(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^round\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"ROUND({quotedColumn}) {op} {paramName}";
    }

    /// <summary>
    /// Parse floor(field) op value.
    /// </summary>
    private static string? TryParseFloor(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^floor\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"FLOOR({quotedColumn}) {op} {paramName}";
    }

    /// <summary>
    /// Parse ceiling(field) op value.
    /// </summary>
    private static string? TryParseCeiling(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var match = Regex.Match(expression, @"^ceiling\s*\(\s*(\w+)\s*\)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var quotedColumn = Q(NamingConvention.ToSnakeCase(match.Groups[1].Value));
        var op = GetSqlOperator(match.Groups[2].Value);
        var value = FilterExpressionParser.ParseValue(match.Groups[3].Value.Trim());
        var paramName = $"@p{parameterIndex++}";
        parameters.Add(new NpgsqlParameter(paramName, value));
        return $"CEILING({quotedColumn}) {op} {paramName}";
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
        _ => throw new ArgumentException($"Unknown OData operator: {odataOp}")
    };
}
