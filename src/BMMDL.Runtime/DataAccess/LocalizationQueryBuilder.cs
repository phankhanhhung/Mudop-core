namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Generates SQL for localized (_texts table) queries with COALESCE-based locale resolution.
/// Internal sub-builder created by DynamicSqlBuilder.
/// </summary>
internal class LocalizationQueryBuilder
{
    private readonly DynamicSqlBuilder _parent;

    internal LocalizationQueryBuilder(DynamicSqlBuilder parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Build an UPSERT query for the _texts companion table.
    /// Inserts a new translation row or updates existing one for the given locale.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildTextsUpsertQuery(
        BmEntity entity,
        Guid entityId,
        string locale,
        Dictionary<string, object?> localizedData,
        Guid? tenantId = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var tableName = _parent.GetTableName(entity) + "_texts";

        var columns = new List<string> { "id", "locale" };
        var values = new List<string>();
        var updateSets = new List<string>();

        var idParam = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(idParam, entityId));
        values.Add(idParam);

        var localeParam = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(localeParam, locale));
        values.Add(localeParam);

        if (entity.TenantScoped && tenantId.HasValue)
        {
            columns.Add(SchemaConstants.TenantIdColumn);
            var tenantParam = $"@p{parameters.Count}";
            parameters.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            values.Add(tenantParam);
        }

        // Add localized field columns
        foreach (var kvp in localizedData)
        {
            var columnName = NamingConvention.GetColumnName(kvp.Key);
            var paramName = $"@p{parameters.Count}";
            columns.Add(columnName);
            values.Add(paramName);
            updateSets.Add($"{columnName} = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, kvp.Value ?? DBNull.Value));
        }

        // INSERT ... ON CONFLICT (id, locale) DO UPDATE SET ...
        var sql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) " +
                  $"VALUES ({string.Join(", ", values)}) " +
                  $"ON CONFLICT (id, locale) DO UPDATE SET {string.Join(", ", updateSets)}";

        return (sql, parameters);
    }

    /// <summary>
    /// Check if an entity has any localized fields.
    /// </summary>
    public static bool HasLocalizedFields(BmEntity entity)
    {
        return entity.Fields.Any(f => f.TypeRef is BmLocalizedType);
    }

    /// <summary>
    /// Get the names of localized fields for an entity.
    /// </summary>
    public static HashSet<string> GetLocalizedFieldNames(BmEntity entity)
    {
        return entity.Fields
            .Where(f => f.TypeRef is BmLocalizedType)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build SELECT columns with COALESCE for localized fields.
    /// Uses aliases: m = main table, t = _texts table.
    /// </summary>
    internal static string BuildLocalizedSelectColumns(
        BmEntity entity,
        List<BmField> localizedFields,
        string? selectClause)
    {
        var localizedNames = new HashSet<string>(
            localizedFields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        var columns = new List<string>();

        if (string.IsNullOrWhiteSpace(selectClause))
        {
            // All columns with COALESCE for localized ones
            columns.Add("m.id");

            foreach (var field in entity.Fields)
            {
                if (field.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    continue;

                var colName = NamingConvention.GetColumnName(field.Name);
                if (localizedNames.Contains(field.Name))
                {
                    columns.Add($"COALESCE(t.{colName}, m.{colName}) AS {colName}");
                }
                else
                {
                    columns.Add($"m.{colName}");
                }
            }

            // Add tenant_id if entity is tenant-scoped
            if (entity.TenantScoped)
            {
                columns.Add("m.tenant_id");
            }
        }
        else
        {
            // Specific columns requested
            var requestedFields = selectClause
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            columns.Add("m.id");

            foreach (var field in entity.Fields)
            {
                if (!requestedFields.Contains(field.Name))
                    continue;

                var colName = NamingConvention.GetColumnName(field.Name);
                if (localizedNames.Contains(field.Name))
                {
                    columns.Add($"COALESCE(t.{colName}, m.{colName}) AS {colName}");
                }
                else
                {
                    columns.Add($"m.{colName}");
                }
            }

            if (entity.TenantScoped && !columns.Contains("m.tenant_id"))
            {
                columns.Add("m.tenant_id");
            }
        }

        return string.Join(", ", columns.Distinct());
    }

    /// <summary>
    /// Build WHERE clauses with locale awareness for localized fields.
    /// </summary>
    internal List<string> BuildLocaleAwareWhereClauses(
        BmEntity entity,
        HashSet<string> localizedFieldNames,
        QueryOptions options,
        Guid? id,
        List<NpgsqlParameter> parameters)
    {
        var whereClauses = new List<string>();

        if (id.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"m.id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, id.Value));
        }

        if (entity.TenantScoped && options.TenantId.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"m.tenant_id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, options.TenantId.Value));
        }

        // Custom filter with locale-aware column references
        if (!string.IsNullOrWhiteSpace(options.Filter))
        {
            var parser = new FilterExpressionParser(entity);
            var (filterSql, filterParams) = parser.Parse(options.Filter);

            // Replace localized column references with COALESCE and add "m." to others
            var localizedFilter = ApplyLocaleToFilter(filterSql, localizedFieldNames);

            // Single-pass parameter renaming to prevent collision
            var paramMap = new Dictionary<string, string>();
            foreach (var param in filterParams)
            {
                var newName = $"@p{parameters.Count}";
                paramMap[param.ParameterName] = newName;
                parameters.Add(new NpgsqlParameter(newName, param.Value));
            }
            if (paramMap.Count > 0)
            {
                var pattern = string.Join("|",
                    paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                    + @"(?=\W|$)";
                localizedFilter = Regex.Replace(localizedFilter, pattern, m => paramMap[m.Value]);
            }
            whereClauses.Add(localizedFilter);
        }

        // Search with locale awareness
        AddSearchFilterLocaleAware(whereClauses, entity, localizedFieldNames, options.Search, parameters, options.SearchCaseSensitive);

        if (!options.IncludeDeleted && DynamicSqlBuilder.HasField(entity, "IsDeleted"))
        {
            whereClauses.Add("m.is_deleted = false");
        }

        // Temporal filters with alias
        if (entity.IsTemporal)
        {
            if (options.AsOf.HasValue)
            {
                var paramName = $"@p{parameters.Count}";
                whereClauses.Add($"m.system_start <= {paramName} AND {paramName} < m.system_end");
                parameters.Add(new NpgsqlParameter(paramName, options.AsOf.Value));
            }
            else if (options.CurrentOnly)
            {
                whereClauses.Add("m.system_end = 'infinity'::TIMESTAMPTZ");
            }

            if (options.ValidAt.HasValue && entity.HasValidTime)
            {
                var validFromCol = !string.IsNullOrEmpty(entity.ValidTimeFromColumn)
                    ? NamingConvention.GetColumnName(entity.ValidTimeFromColumn)
                    : "valid_from";
                var validToCol = !string.IsNullOrEmpty(entity.ValidTimeToColumn)
                    ? NamingConvention.GetColumnName(entity.ValidTimeToColumn)
                    : "valid_to";

                var paramName = $"@p{parameters.Count}";
                whereClauses.Add($"m.{validFromCol} <= {paramName} AND {paramName} < m.{validToCol}");
                parameters.Add(new NpgsqlParameter(paramName, options.ValidAt.Value));
            }
        }

        return whereClauses;
    }

    /// <summary>
    /// Replace column references in filter SQL with locale-aware COALESCE for localized fields
    /// and "m." prefix for non-localized fields.
    /// </summary>
    internal static string ApplyLocaleToFilter(string filterSql, HashSet<string> localizedFieldNames)
    {
        if (string.IsNullOrEmpty(filterSql))
            return filterSql;

        // Build set of localized column names (snake_case)
        var localizedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in localizedFieldNames)
        {
            localizedColumns.Add(NamingConvention.GetColumnName(name));
        }

        var sqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "IN", "IS", "NULL", "TRUE", "FALSE", "LIKE", "ILIKE",
            "EXISTS", "SELECT", "FROM", "WHERE", "ORDER", "BY", "ASC", "DESC",
            "LIMIT", "OFFSET", "AS", "ON", "LEFT", "RIGHT", "INNER", "JOIN",
            "LENGTH", "LOWER", "UPPER", "TRIM", "SUBSTRING", "POSITION", "CONCAT",
            "ROUND", "FLOOR", "CEILING", "EXTRACT", "NOW", "DATE", "TIME", "COALESCE",
            "ANY", "ALL", "ARRAY"
        };

        // Match unqualified column names (snake_case identifiers not preceded by a dot or @)
        var pattern = @"(?<![.\w@])([a-z_][a-z0-9_]*)(?=\s*(=|<>|<|>|<=|>=|\s+IN\s+|\s+IS\s+|\s+LIKE\s+|\s+ILIKE\s+|$|\s+AND\s+|\s+OR\s+|\)|\s*,))";

        return Regex.Replace(filterSql, pattern, match =>
        {
            var columnName = match.Groups[1].Value;
            if (sqlKeywords.Contains(columnName) || columnName.StartsWith("@p"))
                return columnName;
            if (localizedColumns.Contains(columnName))
                return $"COALESCE(t.{columnName}, m.{columnName})";
            return $"m.{columnName}";
        }, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Add search filter with locale awareness.
    /// </summary>
    private void AddSearchFilterLocaleAware(
        List<string> whereClauses,
        BmEntity entity,
        HashSet<string> localizedFieldNames,
        string? search,
        List<NpgsqlParameter> parameters,
        bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(search))
            return;

        var searchFields = DynamicSqlBuilder.GetSearchableFields(entity);
        if (searchFields.Count == 0)
            return;

        // Build set of localized column names (snake_case)
        var localizedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in localizedFieldNames)
        {
            localizedColumns.Add(NamingConvention.GetColumnName(name));
        }

        var searchParamName = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(searchParamName, $"%{search.Trim()}%"));

        var likeOperator = caseSensitive ? "LIKE" : "ILIKE";
        var likeConditions = searchFields.Select(f =>
        {
            if (localizedColumns.Contains(f))
                return $"COALESCE(COALESCE(t.{f}, m.{f})::text, '') {likeOperator} {searchParamName}";
            return $"COALESCE(m.{f}::text, '') {likeOperator} {searchParamName}";
        }).ToList();

        whereClauses.Add($"({string.Join(" OR ", likeConditions)})");
    }
}
