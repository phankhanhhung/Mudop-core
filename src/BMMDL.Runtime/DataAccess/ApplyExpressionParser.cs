namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses OData $apply expressions for aggregation.
/// Supported transformations:
/// - groupby((field1, field2), aggregate(...))
/// - aggregate(field with sum as total, field with avg as average)
/// - filter(expression)
/// </summary>
public class ApplyExpressionParser
{
    private int _parameterIndex;
    private readonly List<NpgsqlParameter> _parameters = new();

    private static string Q(string id) => NamingConvention.QuoteIdentifier(id);

    /// <summary>
    /// Parse $apply expression and generate SQL.
    /// </summary>
    /// <param name="apply">OData $apply expression.</param>
    /// <param name="tableName">Base table name.</param>
    /// <param name="additionalWhereClause">Optional additional WHERE condition (e.g., tenant filter) injected during SQL construction.</param>
    /// <returns>Tuple of (SQL query, parameters, select columns).</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters, List<string> Columns) Parse(
        string apply, string tableName, string? additionalWhereClause = null)
    {
        if (string.IsNullOrWhiteSpace(apply))
            return ($"SELECT * FROM {QuoteTableName(tableName)}", Array.Empty<NpgsqlParameter>(), new List<string> { "*" });

        _parameterIndex = 0;
        _parameters.Clear();

        var columns = new List<string>();
        var groupByColumns = new List<string>();
        var aggregates = new List<string>();
        var whereClause = "";

        // Parse transformations (can be chained with /)
        var transformations = SplitTransformations(apply);

        foreach (var transform in transformations)
        {
            var trimmed = transform.Trim();

            // groupby((field1, field2), aggregate(...))
            var groupByMatch = Regex.Match(trimmed, 
                @"^groupby\s*\(\s*\(([^)]+)\)\s*(?:,\s*aggregate\s*\((.+)\))?\s*\)$", 
                RegexOptions.IgnoreCase);
            if (groupByMatch.Success)
            {
                var rawFields = groupByMatch.Groups[1].Value.Split(',')
                    .Select(f => f.Trim())
                    .ToList();
                var quotedFields = rawFields
                    .Select(f =>
                    {
                        // Handle nested property paths like Address/City → address.city
                        if (f.Contains('/'))
                        {
                            var segments = f.Split('/');
                            var tableAlias = NamingConvention.ToSnakeCase(segments[0]);
                            var columnName = NamingConvention.ToSnakeCase(segments[^1]);
                            return $"{Q(tableAlias)}.{Q(columnName)}";
                        }
                        return Q(NamingConvention.ToSnakeCase(f));
                    })
                    .ToList();
                var unquotedFields = rawFields
                    .Select(f =>
                    {
                        if (f.Contains('/'))
                        {
                            var segments = f.Split('/');
                            return NamingConvention.ToSnakeCase(segments[^1]);
                        }
                        return NamingConvention.ToSnakeCase(f);
                    })
                    .ToList();
                groupByColumns.AddRange(quotedFields);
                columns.AddRange(unquotedFields);

                if (groupByMatch.Groups[2].Success)
                {
                    var aggResult = ParseAggregates(groupByMatch.Groups[2].Value);
                    aggregates.AddRange(aggResult.Expressions);
                    columns.AddRange(aggResult.Aliases);
                }
                continue;
            }

            // aggregate(field with sum as alias)
            var aggregateMatch = Regex.Match(trimmed, 
                @"^aggregate\s*\((.+)\)$", 
                RegexOptions.IgnoreCase);
            if (aggregateMatch.Success)
            {
                var aggResult = ParseAggregates(aggregateMatch.Groups[1].Value);
                aggregates.AddRange(aggResult.Expressions);
                columns.AddRange(aggResult.Aliases);
                continue;
            }

            // filter(expression)
            var filterMatch = Regex.Match(trimmed, 
                @"^filter\s*\((.+)\)$", 
                RegexOptions.IgnoreCase);
            if (filterMatch.Success)
            {
                var filterParser = new FilterExpressionParser();
                var (filterSql, filterParams) = filterParser.Parse(filterMatch.Groups[1].Value);
                
                // Single-pass parameter renaming to prevent collision
                var reindexedSql = filterSql;
                var paramMap = new Dictionary<string, string>();
                foreach (var p in filterParams)
                {
                    var newParamName = $"@p{_parameterIndex++}";
                    paramMap[p.ParameterName] = newParamName;
                    _parameters.Add(new NpgsqlParameter(newParamName, p.Value));
                }
                if (paramMap.Count > 0)
                {
                    var pattern = string.Join("|",
                        paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                        + @"(?=\W|$)";
                    reindexedSql = Regex.Replace(reindexedSql, pattern, m => paramMap[m.Value]);
                }
                whereClause = reindexedSql;
                continue;
            }
        }

        // Build SQL
        var sql = new StringBuilder();
        
        if (columns.Count == 0)
            columns.Add("*");

        var selectParts = new List<string>();
        selectParts.AddRange(groupByColumns);
        selectParts.AddRange(aggregates);

        sql.Append("SELECT ");
        sql.Append(selectParts.Count > 0 ? string.Join(", ", selectParts) : "*");
        sql.Append($" FROM {QuoteTableName(tableName)}");

        var whereParts = new List<string>();
        if (!string.IsNullOrEmpty(whereClause))
            whereParts.Add(whereClause);
        if (!string.IsNullOrEmpty(additionalWhereClause))
            whereParts.Add(additionalWhereClause);

        if (whereParts.Count > 0)
        {
            sql.Append($" WHERE {string.Join(" AND ", whereParts)}");
        }

        if (groupByColumns.Count > 0)
        {
            sql.Append($" GROUP BY {string.Join(", ", groupByColumns)}");
        }

        return (sql.ToString(), _parameters.AsReadOnly(), columns);
    }

    /// <summary>
    /// Parse aggregate expressions: field with sum as alias, field with avg as alias
    /// </summary>
    private (List<string> Expressions, List<string> Aliases) ParseAggregates(string aggregateStr)
    {
        var expressions = new List<string>();
        var aliases = new List<string>();

        // Split by comma, respecting parentheses
        var parts = SplitByComma(aggregateStr);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            
            // Pattern: field with function as alias
            var match = Regex.Match(trimmed, 
                @"^(\w+)\s+with\s+(\w+)\s+as\s+(\w+)$", 
                RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var field = NamingConvention.ToSnakeCase(match.Groups[1].Value);
                var func = match.Groups[2].Value.ToUpperInvariant();
                var alias = NamingConvention.ToSnakeCase(match.Groups[3].Value);

                var sqlFunc = func switch
                {
                    "SUM" => $"SUM({Q(field)})",
                    "AVG" => $"AVG({Q(field)})",
                    "MIN" => $"MIN({Q(field)})",
                    "MAX" => $"MAX({Q(field)})",
                    "COUNT" => $"COUNT({Q(field)})",
                    "COUNTDISTINCT" => $"COUNT(DISTINCT {Q(field)})",
                    _ => $"{func}({Q(field)})"
                };

                expressions.Add($"{sqlFunc} AS {Q(alias)}");
                aliases.Add(alias);
                continue;
            }

            // Pattern: $count as alias (count all)
            var countMatch = Regex.Match(trimmed, 
                @"^\$count\s+as\s+(\w+)$", 
                RegexOptions.IgnoreCase);
            if (countMatch.Success)
            {
                var alias = NamingConvention.ToSnakeCase(countMatch.Groups[1].Value);
                expressions.Add($"COUNT(*) AS {Q(alias)}");
                aliases.Add(alias);
            }
        }

        return (expressions, aliases);
    }

    /// <summary>
    /// Quote a potentially schema-qualified table name (e.g., "platform.entity_name" → "\"platform\".\"entity_name\"").
    /// </summary>
    private static string QuoteTableName(string tableName)
    {
        var dotIdx = tableName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            var schema = tableName[..dotIdx];
            var table = tableName[(dotIdx + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table)}";
        }
        return NamingConvention.QuoteIdentifier(tableName);
    }

    /// <summary>
    /// Split transformations by / delimiter (top level only).
    /// </summary>
    private static List<string> SplitTransformations(string apply)
    {
        var result = new List<string>();
        var depth = 0;
        var current = new StringBuilder();

        foreach (var c in apply)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == '/' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    /// <summary>
    /// Split by comma, respecting parentheses.
    /// </summary>
    private static List<string> SplitByComma(string str)
    {
        var result = new List<string>();
        var depth = 0;
        var current = new StringBuilder();

        foreach (var c in str)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
