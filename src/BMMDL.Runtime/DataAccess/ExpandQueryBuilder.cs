namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Generates SQL for $expand queries with LEFT JOINs for navigation properties.
/// Internal sub-builder created by DynamicSqlBuilder.
/// </summary>
internal class ExpandQueryBuilder
{
    private readonly DynamicSqlBuilder _parent;
    private readonly IMetaModelCache _cache;

    internal ExpandQueryBuilder(DynamicSqlBuilder parent, IMetaModelCache cache)
    {
        _parent = parent;
        _cache = cache;
    }

    /// <summary>
    /// Build a SELECT query with expanded navigation properties (LEFT JOINs).
    /// Returns flat result set with prefixed column names for expanded entities.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters, List<string> ExpandedNavs) BuildSelectWithExpand(
        BmEntity entity,
        QueryOptions options,
        Guid? id = null)
    {
        var expandedNavs = new List<string>();

        // If no expand options, fall back to regular select
        if (options.ExpandOptions == null || options.ExpandOptions.Count == 0)
        {
            var (basicSql, basicParams) = _parent.BuildSelectQuery(entity, options, id);
            return (basicSql, basicParams, expandedNavs);
        }

        var parameters = new List<NpgsqlParameter>();
        var sqlBuilder = new StringBuilder();

        var mainTable = _parent.GetTableName(entity);
        var mainAlias = "m";

        // Build SELECT clause with prefixed columns
        var selectParts = new List<string>();

        // Main entity columns
        var mainColumns = DynamicSqlBuilder.BuildSelectColumns(entity, options.Select);
        if (mainColumns == "*")
        {
            selectParts.Add($"{mainAlias}.*");
        }
        else
        {
            foreach (var col in mainColumns.Split(','))
            {
                selectParts.Add($"{mainAlias}.{col.Trim()}");
            }
        }

        // Build JOINs and add expanded columns
        var joinClauses = new List<string>();
        var navIndex = 0;

        foreach (var (navName, expandOpts) in options.ExpandOptions)
        {
            // Find the association in entity
            var assoc = entity.Associations.FirstOrDefault(a =>
                a.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));

            if (assoc == null)
            {
                // Try compositions
                var comp = entity.Compositions.FirstOrDefault(c =>
                    c.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));
                if (comp != null)
                {
                    assoc = comp; // BmComposition extends BmAssociation
                }
            }

            if (assoc == null)
                continue; // Skip unknown nav properties

            // Skip OneToMany/ManyToMany — these are handled via separate sub-queries
            // (ExpandOneToManyProperties / ExpandOneToManyPropertiesBatched)
            if (assoc.Cardinality == BmCardinality.OneToMany || assoc.Cardinality == BmCardinality.ManyToMany)
                continue;

            // Look up target entity from cache
            var targetEntity = _cache.GetEntity(assoc.TargetEntity);
            if (targetEntity == null)
                continue;

            var navAlias = $"n{navIndex++}";
            var navTable = _parent.GetTableName(targetEntity);
            expandedNavs.Add(navName);

            // Add expanded columns with prefix
            var navColumns = DynamicSqlBuilder.BuildSelectColumns(targetEntity, expandOpts.Select);
            if (navColumns == "*")
            {
                // Add all fields with prefix (quote column names for reserved keyword safety)
                selectParts.Add($"{navAlias}.{NamingConvention.QuoteIdentifier("id")} AS {navName}_id");
                foreach (var field in targetEntity.Fields)
                {
                    var colName = NamingConvention.GetColumnName(field.Name);
                    selectParts.Add($"{navAlias}.{NamingConvention.QuoteIdentifier(colName)} AS {navName}_{colName}");
                }
            }
            else
            {
                foreach (var col in navColumns.Split(','))
                {
                    var trimmedCol = col.Trim();
                    selectParts.Add($"{navAlias}.{trimmedCol} AS {navName}_{trimmedCol}");
                }
            }

            // Build JOIN condition
            string joinCondition;
            if (!string.IsNullOrEmpty(assoc.OnConditionString))
            {
                // Custom ON condition from association definition
                joinCondition = assoc.OnConditionString
                    .Replace("$self.", $"{mainAlias}.", StringComparison.OrdinalIgnoreCase)
                    .Replace($"{assoc.TargetEntity}.", $"{navAlias}.", StringComparison.OrdinalIgnoreCase);

                if (!joinCondition.Contains($"{mainAlias}.") && !joinCondition.Contains($"{navAlias}."))
                {
                    var parts = joinCondition.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var leftCol = NamingConvention.GetColumnName(parts[0].Trim());
                        var rightCol = NamingConvention.GetColumnName(parts[1].Trim());
                        joinCondition = $"{mainAlias}.{leftCol} = {navAlias}.{rightCol}";
                    }
                }
            }
            else
            {
                // Default FK convention: main.navName_id = nav.id
                var fkColumn = NamingConvention.GetFkColumnName(navName);
                joinCondition = $"{mainAlias}.{fkColumn} = {navAlias}.id";
            }

            // Add tenant filter to JOIN condition to prevent cross-tenant data leaks
            if (targetEntity.TenantScoped && options.TenantId.HasValue)
            {
                var tenantParamName = $"@p_expand_tenant_{navAlias}";
                parameters.Add(new NpgsqlParameter(tenantParamName, options.TenantId.Value));
                joinCondition += $" AND {navAlias}.tenant_id = {tenantParamName}";
            }

            joinClauses.Add($"LEFT JOIN {navTable} {navAlias} ON {joinCondition}");
        }

        // Build SQL
        sqlBuilder.Append($"SELECT {string.Join(", ", selectParts)}");

        // For SeparateTables + asOf, use UNION ALL subquery as FROM source
        if (entity.IsTemporal && entity.TemporalStrategy == TemporalStrategy.SeparateTables && options.AsOf.HasValue)
        {
            var rawTable = _parent.GetRawTableName(entity);
            var historyTable = DynamicSqlBuilder.QuoteQualifiedTableName(rawTable + "_history");
            var asOfParam = $"@p{parameters.Count}";
            parameters.Add(new NpgsqlParameter(asOfParam, options.AsOf.Value));
            sqlBuilder.Append($" FROM (SELECT * FROM {mainTable} WHERE system_start <= {asOfParam} AND {asOfParam} < system_end");
            sqlBuilder.Append($" UNION ALL SELECT * FROM {historyTable} WHERE system_start <= {asOfParam} AND {asOfParam} < system_end) {mainAlias}");
        }
        else
        {
            sqlBuilder.Append($" FROM {mainTable} {mainAlias}");
        }

        foreach (var join in joinClauses)
        {
            sqlBuilder.Append($" {join}");
        }

        // WHERE clause
        var whereClauses = new List<string>();

        if (id.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"{mainAlias}.id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, id.Value));
        }

        if (entity.TenantScoped && options.TenantId.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"{mainAlias}.tenant_id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, options.TenantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(options.Filter))
        {
            var parser = new FilterExpressionParser(entity);
            var (filterSql, filterParams) = parser.Parse(options.Filter);

            // Add main alias to column names in filter to prevent ambiguous references
            var aliasedFilter = AddTableAliasToFilter(filterSql, mainAlias);

            // Build parameter name mapping upfront, then do a single-pass replacement
            // to prevent collisions (e.g., renaming @p0→@p1 when @p1 already exists in SQL)
            var paramMap = new Dictionary<string, string>();
            foreach (var param in filterParams)
            {
                var newName = $"@p{parameters.Count}";
                paramMap[param.ParameterName] = newName;
                parameters.Add(new NpgsqlParameter(newName, param.Value));
            }
            if (paramMap.Count > 0)
            {
                // Match longer names first (@p10 before @p1) to prevent partial matches
                var pattern = string.Join("|",
                    paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                    + @"(?=\W|$)";
                aliasedFilter = Regex.Replace(aliasedFilter, pattern, m => paramMap[m.Value]);
            }
            whereClauses.Add(aliasedFilter);
        }

        if (!options.IncludeDeleted && DynamicSqlBuilder.HasField(entity, "IsDeleted"))
        {
            whereClauses.Add($"{mainAlias}.is_deleted = false");
        }

        // Full-text search (OData $search) — with table alias prefix
        AddSearchFilterWithAlias(whereClauses, entity, options.Search, parameters, mainAlias, options.SearchCaseSensitive);

        if (entity.IsTemporal)
        {
            if (options.AsOf.HasValue && entity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                // InlineHistory + asOf: filter directly in WHERE clause
                var asOfParam = $"@p{parameters.Count}";
                whereClauses.Add($"{mainAlias}.system_start <= {asOfParam} AND {asOfParam} < {mainAlias}.system_end");
                parameters.Add(new NpgsqlParameter(asOfParam, options.AsOf.Value));
            }
            else if (options.CurrentOnly)
            {
                // SeparateTables asOf is handled in FROM subquery; skip for that case
                whereClauses.Add($"{mainAlias}.system_end = 'infinity'::TIMESTAMPTZ");
            }
        }

        if (whereClauses.Count > 0)
        {
            sqlBuilder.Append(" WHERE ");
            sqlBuilder.Append(string.Join(" AND ", whereClauses));
        }

        // ORDER BY
        if (!string.IsNullOrWhiteSpace(options.OrderBy))
        {
            var orderBySql = FilterExpressionParser.ParseOrderBy(options.OrderBy, entity);
            if (!string.IsNullOrEmpty(orderBySql))
            {
                sqlBuilder.Append($" ORDER BY {orderBySql}");
            }
        }

        // LIMIT/OFFSET - use parameterized values for security
        if (options.Top.HasValue)
        {
            var limitValue = Math.Max(0, options.Top.Value);
            var paramName = $"@p_limit_{parameters.Count}";
            sqlBuilder.Append($" LIMIT {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, limitValue));
        }
        if (options.Skip.HasValue && options.Skip.Value > 0)
        {
            var offsetValue = Math.Max(0, options.Skip.Value);
            var paramName = $"@p_offset_{parameters.Count}";
            sqlBuilder.Append($" OFFSET {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, offsetValue));
        }

        return (sqlBuilder.ToString(), parameters, expandedNavs);
    }

    /// <summary>
    /// Add table alias prefix to unqualified column names in a filter expression.
    /// Prevents ambiguous column references when using JOINs with $expand.
    /// </summary>
    internal static string AddTableAliasToFilter(string filterSql, string alias)
    {
        if (string.IsNullOrEmpty(filterSql))
            return filterSql;

        var sqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "IN", "IS", "NULL", "TRUE", "FALSE", "LIKE", "ILIKE",
            "EXISTS", "SELECT", "FROM", "WHERE", "ORDER", "BY", "ASC", "DESC",
            "LIMIT", "OFFSET", "AS", "ON", "LEFT", "RIGHT", "INNER", "JOIN",
            "LENGTH", "LOWER", "UPPER", "TRIM", "SUBSTRING", "POSITION", "CONCAT",
            "ROUND", "FLOOR", "CEILING", "EXTRACT", "NOW", "DATE", "TIME"
        };

        // Match both unquoted identifiers and quoted identifiers ("column_name")
        // Lookbehind excludes '.', word chars, and '@' (for @parameters)
        var pattern = @"(?<![.\w@])(""[^""]+""|\b[a-z_][a-z0-9_]*\b)(?=\s*(=|<>|<|>|<=|>=|\s+IN\s+|\s+IS\s+|\s+LIKE\s+|\s+ILIKE\s+|$|\s+AND\s+|\s+OR\s+|\)|\s*,))";

        var result = Regex.Replace(
            filterSql,
            pattern,
            match =>
            {
                var token = match.Groups[1].Value;
                // Strip quotes for keyword check
                var name = token.StartsWith('"') ? token.Trim('"') : token;
                if (sqlKeywords.Contains(name))
                    return token;
                if (name.StartsWith("@p") || name.StartsWith("p_"))
                    return token;
                return $"{alias}.{token}";
            },
            RegexOptions.IgnoreCase);

        return result;
    }

    /// <summary>
    /// Add ILIKE search filter with table alias prefix (for use in JOIN queries).
    /// Supports OData $search syntax: AND (implicit/explicit), OR, NOT, and quoted phrases.
    /// </summary>
    private void AddSearchFilterWithAlias(
        List<string> whereClauses,
        BmEntity entity,
        string? search,
        List<NpgsqlParameter> parameters,
        string tableAlias,
        bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(search))
            return;

        var searchFields = DynamicSqlBuilder.GetSearchableFields(entity);
        if (searchFields.Count == 0)
            return;

        var likeOperator = caseSensitive ? "LIKE" : "ILIKE";
        var terms = DynamicSqlBuilder.ParseSearchExpression(search.Trim());
        if (terms.Count == 0)
            return;

        var parts = new List<string>();

        foreach (var term in terms)
        {
            var paramName = $"@p{parameters.Count}";
            var escapedValue = EscapeLikeValue(term.Value);
            parameters.Add(new NpgsqlParameter(paramName, $"%{escapedValue}%"));

            var fieldConditions = searchFields
                .Select(f => $"COALESCE({tableAlias}.{f}::text, '') {likeOperator} {paramName}")
                .ToList();

            var termSql = $"({string.Join(" OR ", fieldConditions)})";

            if (term.IsNegated)
                termSql = $"NOT {termSql}";

            if (parts.Count > 0)
            {
                var combiner = term.Operator == DynamicSqlBuilder.SearchOperator.Or ? " OR " : " AND ";
                parts.Add(combiner);
            }
            parts.Add(termSql);
        }

        whereClauses.Add($"({string.Concat(parts)})");
    }

    /// <summary>
    /// Escape LIKE/ILIKE wildcard characters in user-supplied search values.
    /// </summary>
    private static string EscapeLikeValue(string value)
    {
        return value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }
}
