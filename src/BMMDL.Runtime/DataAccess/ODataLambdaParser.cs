namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess.Parsers;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Parser for OData lambda expressions (any/all) in $filter.
/// Generates EXISTS/NOT EXISTS SQL subqueries for collection filtering.
/// </summary>
internal class ODataLambdaParser
{
    private readonly BmEntity? _entity;

    public ODataLambdaParser(BmEntity? entity)
    {
        _entity = entity;
    }

    /// <summary>Shorthand for quoting a SQL identifier.</summary>
    private static string Q(string id) => NamingConvention.QuoteIdentifier(id);

    /// <summary>
    /// Try to parse a lambda expression from the filter.
    /// Returns SQL clause if matched, null otherwise.
    /// </summary>
    public string? TryParse(string expression, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // ANY lambda: collection/any(x: x/field op value)
        var anyMatch = Regex.Match(expression, @"^(\w+)/any\s*\(\s*(\w+)\s*:\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (anyMatch.Success)
        {
            return ParseAnyAllLambda(anyMatch.Groups[1].Value, anyMatch.Groups[2].Value,
                anyMatch.Groups[3].Value, isAny: true, parameters, ref parameterIndex);
        }

        // ALL lambda: collection/all(x: x/field op value)
        var allMatch = Regex.Match(expression, @"^(\w+)/all\s*\(\s*(\w+)\s*:\s*(.+)\s*\)$", RegexOptions.IgnoreCase);
        if (allMatch.Success)
        {
            return ParseAnyAllLambda(allMatch.Groups[1].Value, allMatch.Groups[2].Value,
                allMatch.Groups[3].Value, isAny: false, parameters, ref parameterIndex);
        }

        // Simple ANY (no lambda): collection/any() — checks if collection is non-empty
        var simpleAnyMatch = Regex.Match(expression, @"^(\w+)/any\s*\(\s*\)$", RegexOptions.IgnoreCase);
        if (simpleAnyMatch.Success)
        {
            return ParseSimpleAny(simpleAnyMatch.Groups[1].Value, parameters, ref parameterIndex);
        }

        // Try alternative lambda syntax via static LambdaParser
        var lambdaResult = LambdaParser.TryParse(expression, parameters, ref parameterIndex);
        if (lambdaResult != null)
        {
            return lambdaResult;
        }

        return null;
    }

    /// <summary>
    /// Parse any/all lambda expression: collection/any(x: x/field op value).
    /// Generates EXISTS/NOT EXISTS SQL subquery.
    /// </summary>
    private string ParseAnyAllLambda(string collectionName, string lambdaVar, string lambdaExpr,
        bool isAny, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // Check if the collection is actually an array-type field
        if (_entity != null)
        {
            var arrayField = _entity.Fields.FirstOrDefault(f =>
                f.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
            if (arrayField?.TypeRef is MetaModel.Types.BmArrayType)
            {
                return ParseArrayLambda(collectionName, lambdaVar, lambdaExpr, isAny, parameters, ref parameterIndex);
            }
        }

        var collectionTable = NamingConvention.ToSnakeCase(collectionName);
        var quotedTable = Q(collectionTable);

        // Transform lambda expression: replace "x/field" with "field" for SQL parsing
        var transformedExpr = TransformLambdaExpression(lambdaExpr, lambdaVar);

        // Parse the transformed expression with a fresh parser
        var innerParser = new FilterExpressionParser();
        var (innerWhere, innerParams) = innerParser.Parse(transformedExpr);

        // Single-pass parameter renaming to prevent collision
        var paramMap = new Dictionary<string, string>();
        foreach (var param in innerParams)
        {
            var newName = $"@p{parameterIndex++}";
            paramMap[param.ParameterName] = newName;
            parameters.Add(new NpgsqlParameter(newName, param.Value));
        }
        if (paramMap.Count > 0)
        {
            var pattern = string.Join("|",
                paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                + @"(?=\W|$)";
            innerWhere = Regex.Replace(innerWhere, pattern, m => paramMap[m.Value]);
        }

        var fkColumn = ResolveCollectionFkColumn(collectionName);
        var quotedFk = Q(fkColumn);

        // Qualify the parent ID reference to avoid ambiguity in JOINs
        var parentIdRef = _entity != null
            ? $"{Q(NamingConvention.ToSnakeCase(_entity.Name))}.{Q("id")}"
            : Q("id");

        if (isAny)
        {
            return $"EXISTS (SELECT 1 FROM {quotedTable} sub WHERE sub.{quotedFk} = {parentIdRef} AND {innerWhere})";
        }
        else
        {
            return $"NOT EXISTS (SELECT 1 FROM {quotedTable} sub WHERE sub.{quotedFk} = {parentIdRef} AND NOT ({innerWhere}))";
        }
    }

    /// <summary>
    /// Transform lambda expression by replacing variable references.
    /// Converts "x/field" to "field" for SQL parsing.
    /// </summary>
    private static string TransformLambdaExpression(string expr, string lambdaVar)
    {
        var pattern = $@"(^|\s){Regex.Escape(lambdaVar)}/(\w+)";
        return Regex.Replace(expr, pattern, "$1$2", RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>
    /// Parse simple any() without lambda: collection/any().
    /// Checks if collection has at least one item.
    /// </summary>
    private string ParseSimpleAny(string collectionName, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        // Check if the collection is an array field
        if (_entity != null)
        {
            var arrayField = _entity.Fields.FirstOrDefault(f =>
                f.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
            if (arrayField?.TypeRef is MetaModel.Types.BmArrayType)
            {
                var quotedCol = Q(NamingConvention.ToSnakeCase(collectionName));
                return $"cardinality({quotedCol}) > 0";
            }
        }

        var collectionTable = NamingConvention.ToSnakeCase(collectionName);
        var quotedTable = Q(collectionTable);
        var fkColumn = ResolveCollectionFkColumn(collectionName);
        var quotedFk = Q(fkColumn);

        // Qualify the parent ID reference to avoid ambiguity in JOINs
        var parentIdRef = _entity != null
            ? $"{Q(NamingConvention.ToSnakeCase(_entity.Name))}.{Q("id")}"
            : Q("id");

        return $"EXISTS (SELECT 1 FROM {quotedTable} sub WHERE sub.{quotedFk} = {parentIdRef})";
    }

    /// <summary>
    /// Parse any/all lambda on an array-type field.
    /// any(x: x eq value) → value = ANY(column)
    /// all(x: x eq value) → NOT EXISTS (SELECT 1 FROM unnest(column) u(v) WHERE NOT (u.v = value))
    /// </summary>
    private string ParseArrayLambda(string fieldName, string lambdaVar, string lambdaExpr,
        bool isAny, List<NpgsqlParameter> parameters, ref int parameterIndex)
    {
        var quotedColumn = Q(NamingConvention.ToSnakeCase(fieldName));

        // Parse the lambda body: "x eq 'value'" → extract value and operator
        var bodyPattern = $@"^{Regex.Escape(lambdaVar)}\s+(eq|ne|gt|ge|lt|le)\s+(.+)$";
        var bodyMatch = Regex.Match(lambdaExpr.Trim(), bodyPattern, RegexOptions.IgnoreCase);

        if (bodyMatch.Success)
        {
            var op = bodyMatch.Groups[1].Value.ToLowerInvariant();
            var valueStr = bodyMatch.Groups[2].Value.Trim();
            var value = FilterExpressionParser.ParseValue(valueStr);
            var paramName = $"@p{parameterIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, value));

            if (isAny)
            {
                if (op == "eq")
                {
                    return $"{paramName} = ANY({quotedColumn})";
                }
                var sqlOp = GetSqlOperator(op);
                return $"EXISTS (SELECT 1 FROM unnest({quotedColumn}) u(v) WHERE u.v {sqlOp} {paramName})";
            }
            else
            {
                var sqlOp = GetSqlOperator(op);
                return $"NOT EXISTS (SELECT 1 FROM unnest({quotedColumn}) u(v) WHERE NOT (u.v {sqlOp} {paramName}))";
            }
        }

        // Fallback: if we can't parse the body, treat as non-empty check
        return isAny
            ? $"cardinality({quotedColumn}) > 0"
            : $"cardinality({quotedColumn}) >= 0";
    }

    /// <summary>
    /// Resolve the FK column name for a collection navigation property.
    /// </summary>
    private string ResolveCollectionFkColumn(string collectionName)
    {
        if (_entity != null)
        {
            // Check compositions first (child has parent FK)
            var composition = _entity.Compositions
                .FirstOrDefault(c => c.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
            if (composition != null)
            {
                return NamingConvention.GetFkColumnName(_entity.Name);
            }

            // Check associations (OneToMany: target has FK back to this entity)
            var association = _entity.Associations
                .FirstOrDefault(a => a.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
            if (association != null)
            {
                return NamingConvention.GetFkColumnName(association.Name);
            }
        }

        // Fallback: infer FK from singularized collection name + "_id"
        var singularName = InferSingularName(collectionName);
        return NamingConvention.GetFkColumnName(singularName);
    }

    /// <summary>
    /// Infer singular name from a plural collection name.
    /// </summary>
    private static string InferSingularName(string pluralName)
    {
        if (string.IsNullOrEmpty(pluralName))
            return pluralName;

        var irregulars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "children", "child" },
            { "people", "person" },
            { "men", "man" },
            { "women", "woman" },
            { "data", "datum" },
            { "criteria", "criterion" }
        };

        if (irregulars.TryGetValue(pluralName, out var singular))
            return singular;

        if (pluralName.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return pluralName[..^3] + "y";

        if (pluralName.EndsWith("ses", StringComparison.OrdinalIgnoreCase) ||
            pluralName.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
            pluralName.EndsWith("zes", StringComparison.OrdinalIgnoreCase) ||
            pluralName.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
            pluralName.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
            return pluralName[..^2];

        if (pluralName.EndsWith("s", StringComparison.OrdinalIgnoreCase) && pluralName.Length > 1)
            return pluralName[..^1];

        return pluralName;
    }

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
