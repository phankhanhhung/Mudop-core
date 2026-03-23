namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Extensions;
using BMMDL.Runtime.Rules;

/// <summary>
/// Result object for entity list queries.
/// </summary>
public class EntityListResult
{
    public List<Dictionary<string, object?>> Items { get; init; } = new();
    public int? TotalCount { get; init; }
}

/// <summary>
/// Entity query logic: List, GetById, expand helpers, computed field evaluation.
/// </summary>
public class EntityQueryService : IEntityQueryService
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IRuleEngine _ruleEngine;
    private readonly IFieldRestrictionApplier _fieldRestrictionApplier;
    private readonly Handlers.RecursiveExpandHandler _recursiveExpandHandler;
    private readonly IRuntimeExpressionEvaluator _expressionEvaluator;
    private readonly ILogger<EntityQueryService> _logger;

    private static string Q(string id) => NamingConvention.QuoteIdentifier(id);

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public EntityQueryService(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IFieldRestrictionApplier fieldRestrictionApplier,
        Handlers.RecursiveExpandHandler recursiveExpandHandler,
        IRuntimeExpressionEvaluator expressionEvaluator,
        ILogger<EntityQueryService> logger)
    {
        _cacheManager = cacheManager;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _ruleEngine = ruleEngine;
        _fieldRestrictionApplier = fieldRestrictionApplier;
        _recursiveExpandHandler = recursiveExpandHandler;
        _expressionEvaluator = expressionEvaluator;
        _logger = logger;
    }

    /// <summary>
    /// Execute the main query for a list of entities. Handles $expand, $apply, polymorphic, inheritance.
    /// Returns raw items + expanded navigations.
    /// </summary>
    public async Task<(List<Dictionary<string, object?>> Items, List<string> ExpandedNavs)> ExecuteListQueryAsync(
        BmEntity entityDef, QueryOptions options,
        Dictionary<string, ExpandOptions>? expandOptions,
        string? apply,
        CancellationToken ct)
    {
        List<Dictionary<string, object?>> items;
        List<string> expandedNavs;

        if (!string.IsNullOrWhiteSpace(apply))
        {
            var applyParser = new ApplyExpressionParser();
            var tableName = _sqlBuilder.GetTableName(entityDef);
            var paramList = new List<Npgsql.NpgsqlParameter>();

            // Build additional WHERE clauses for tenant, soft-delete, and $filter
            var additionalClauses = new List<string>();

            if (entityDef.TenantScoped && options.TenantId != Guid.Empty && options.TenantId != null)
            {
                var tenantParamName = "@tenant_filter";
                paramList.Add(new Npgsql.NpgsqlParameter(tenantParamName, options.TenantId));
                additionalClauses.Add($"tenant_id = {tenantParamName}");
            }

            // Fix: Soft-deleted records must be excluded from $apply results
            if (!options.IncludeDeleted &&
                entityDef.Fields.Any(f => f.Name.Equals("IsDeleted", StringComparison.OrdinalIgnoreCase)))
            {
                additionalClauses.Add("is_deleted = false");
            }

            // Fix: $filter must be applied before $apply per OData v4 spec
            if (!string.IsNullOrWhiteSpace(options.Filter))
            {
                var filterParser = new FilterExpressionParser();
                var (filterSql, filterParams) = filterParser.Parse(options.Filter);
                additionalClauses.Add($"({filterSql})");
                paramList.AddRange(filterParams);
            }

            string? additionalWhereClause = additionalClauses.Count > 0
                ? string.Join(" AND ", additionalClauses)
                : null;

            var (applySql, applyParams, columns) = applyParser.Parse(apply, tableName, additionalWhereClause);
            paramList.AddRange(applyParams);

            _logger.LogDebug("APPLY SQL: {Sql}", applySql);
            items = await _queryExecutor.ExecuteListAsync(applySql, paramList, ct);
            expandedNavs = new List<string>();
        }
        else if (expandOptions != null && expandOptions.Count > 0)
        {
            var (sql, parameters, navs) = _sqlBuilder.BuildSelectWithExpand(entityDef, options);
            _logger.LogDebug("EXPAND SQL: {Sql}", sql);
            var flatItems = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
            expandedNavs = navs;
            items = TransformExpandedResults(flatItems, expandedNavs);
        }
        else if (entityDef.DerivedEntities != null && entityDef.DerivedEntities.Count > 0)
        {
            var (sql, parameters) = _sqlBuilder.BuildPolymorphicSelectQuery(
                entityDef, entityDef.DerivedEntities, options);
            _logger.LogDebug("POLYMORPHIC SELECT SQL: {Sql}", sql);
            items = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
            expandedNavs = new List<string>();
        }
        else
        {
            var (sql, parameters) = BuildInheritanceAwareSelectQuery(entityDef, options);
            _logger.LogDebug("SELECT SQL: {Sql}", sql);
            items = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
            expandedNavs = new List<string>();
        }

        return (items, expandedNavs);
    }

    /// <summary>
    /// Expand OneToMany/ManyToMany and recursive navigations for a list of items.
    /// </summary>
    public async Task ExpandNavigationsAsync(
        BmEntity entityDef,
        List<Dictionary<string, object?>> items,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct)
    {
        if (items.Count == 0) return;

        var recursiveExpands = expandOptions
            .Where(e => e.Value.Levels.HasValue)
            .ToDictionary(e => e.Key, e => e.Value);
        var regularExpands = expandOptions
            .Where(e => !e.Value.Levels.HasValue)
            .ToDictionary(e => e.Key, e => e.Value);

        if (regularExpands.Count > 0)
            await ExpandOneToManyPropertiesBatched(entityDef, items, regularExpands, tenantId, ct);

        if (recursiveExpands.Count > 0)
        {
            _logger.LogDebug("Applying recursive $levels expansion for {Count} navigation(s)", recursiveExpands.Count);
            await _recursiveExpandHandler.ExpandRecursively(entityDef, items, recursiveExpands, tenantId, ct);
        }
    }

    /// <summary>
    /// Expand OneToMany navigations for a single item.
    /// </summary>
    public async Task ExpandNavigationsForSingleAsync(
        BmEntity entityDef,
        Dictionary<string, object?> result,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct)
    {
        var recursiveExpands = expandOptions
            .Where(e => e.Value.Levels.HasValue)
            .ToDictionary(e => e.Key, e => e.Value);
        var regularExpands = expandOptions
            .Where(e => !e.Value.Levels.HasValue)
            .ToDictionary(e => e.Key, e => e.Value);

        if (regularExpands.Count > 0)
            await ExpandOneToManyProperties(entityDef, result, regularExpands, tenantId, ct);

        if (recursiveExpands.Count > 0)
        {
            _logger.LogDebug("Applying recursive $levels expansion for {Count} navigation(s)", recursiveExpands.Count);
            await _recursiveExpandHandler.ExpandRecursively(
                entityDef, new List<Dictionary<string, object?>> { result }, recursiveExpands, tenantId, ct);
        }
    }

    /// <summary>
    /// Get total count for pagination.
    /// </summary>
    public async Task<int> GetCountAsync(BmEntity entityDef, QueryOptions options, CancellationToken ct)
    {
        var (countSql, countParams) = _sqlBuilder.BuildCountQuery(entityDef, options with { Top = null, Skip = null });
        _logger.LogDebug("COUNT SQL: {Sql}", countSql);
        return await _queryExecutor.ExecuteScalarAsync<int>(countSql, countParams, ct);
    }

    /// <summary>
    /// Execute GetById query with optional $expand.
    /// </summary>
    public async Task<Dictionary<string, object?>?> GetByIdAsync(
        BmEntity entityDef, Guid id, QueryOptions options,
        Dictionary<string, ExpandOptions>? expandOptions,
        CancellationToken ct)
    {
        Dictionary<string, object?>? result;

        if (expandOptions != null && expandOptions.Count > 0)
        {
            var (sql, parameters, navs) = _sqlBuilder.BuildSelectWithExpand(entityDef, options, id);
            var flatItems = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
            var transformed = TransformExpandedResults(flatItems, navs);
            result = transformed.FirstOrDefault();
        }
        else if (entityDef.ParentEntity != null)
        {
            var (sql, parameters) = _sqlBuilder.BuildInheritanceSelectQuery(entityDef, options, id);
            _logger.LogDebug("INHERITANCE SELECT SQL: {Sql}", sql);
            result = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        }
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options, id);
            result = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        }

        return result;
    }

    /// <summary>
    /// Evaluate application-level computed fields (Virtual/Application strategy) post-query.
    /// </summary>
    public void EvaluateComputedFields(BmEntity entityDef, List<Dictionary<string, object?>> results)
    {
        var appComputedFields = entityDef.Fields
            .Where(f => f.IsComputed && f.ComputedExpr != null &&
                        (f.ComputedStrategy == BMMDL.MetaModel.Enums.ComputedStrategy.Virtual ||
                         f.ComputedStrategy == BMMDL.MetaModel.Enums.ComputedStrategy.Application))
            .ToList();

        if (appComputedFields.Count == 0) return;

        foreach (var row in results)
        {
            foreach (var field in appComputedFields)
            {
                try
                {
                    var value = _expressionEvaluator.Evaluate(field.ComputedExpr!, row);
                    var columnName = NamingConvention.GetColumnName(field.Name);
                    row[columnName] = value;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evaluate computed field {Field} on {Entity}: {Message}",
                        field.Name, entityDef.Name, ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Apply field-level restrictions (hidden/masked fields) to a list of items.
    /// </summary>
    public void ApplyFieldRestrictions(
        BmEntity entityDef,
        List<Dictionary<string, object?>> items,
        RequestContext context)
    {
        if (context.UserContext == null) return;

        var evalContext = context.ToEvaluationContext();
        for (int i = 0; i < items.Count; i++)
        {
            items[i] = _fieldRestrictionApplier.ApplyRestrictions(entityDef, items[i], context.UserContext, evalContext);
        }
    }

    /// <summary>
    /// Apply field-level restrictions to a single result.
    /// </summary>
    public Dictionary<string, object?> ApplyFieldRestrictions(
        BmEntity entityDef,
        Dictionary<string, object?> result,
        RequestContext context)
    {
        if (context.UserContext == null) return result;

        var evalContext = context.ToEvaluationContext();
        return _fieldRestrictionApplier.ApplyRestrictions(entityDef, result, context.UserContext, evalContext);
    }

    /// <summary>
    /// Apply service projection IncludeFields/ExcludeFields filtering.
    /// </summary>
    public static void ApplyProjectionFiltering(BmEntity entityDef, List<Dictionary<string, object?>> items)
    {
        if ((entityDef.IncludeFields == null || entityDef.IncludeFields.Count == 0) &&
            (entityDef.ExcludeFields == null || entityDef.ExcludeFields.Count == 0))
            return;

        foreach (var item in items)
            ApplyProjectionFiltering(entityDef, item);
    }

    /// <summary>
    /// Apply service projection IncludeFields/ExcludeFields filtering to a single item.
    /// </summary>
    public static void ApplyProjectionFiltering(BmEntity entityDef, Dictionary<string, object?> result)
    {
        if (entityDef.IncludeFields != null && entityDef.IncludeFields.Count > 0)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in entityDef.IncludeFields)
                allowed.Add(f);
            foreach (var f in entityDef.Fields.Where(f => f.IsKey))
                allowed.Add(f.Name);

            foreach (var key in result.Keys.ToList())
            {
                if (key.StartsWith("@odata.")) continue;
                if (!allowed.Contains(key))
                    result.Remove(key);
            }
        }
        else if (entityDef.ExcludeFields != null && entityDef.ExcludeFields.Count > 0)
        {
            var excluded = new HashSet<string>(entityDef.ExcludeFields, StringComparer.OrdinalIgnoreCase);
            foreach (var key in result.Keys.ToList())
            {
                if (key.StartsWith("@odata.")) continue;
                if (excluded.Contains(key))
                    result.Remove(key);
            }
        }
    }

    /// <summary>
    /// Transform flat JOIN results to nested JSON structure.
    /// </summary>
    public static List<Dictionary<string, object?>> TransformExpandedResults(
        List<Dictionary<string, object?>> flatItems,
        List<string> expandedNavs)
    {
        if (expandedNavs.Count == 0)
            return flatItems;

        var navPrefixes = expandedNavs
            .Where(nav => !string.IsNullOrEmpty(nav))
            .Select(nav => new
        {
            Nav = nav,
            SnakePrefix = nav + "_",
            PascalPrefix = char.ToUpper(nav[0]) + nav.Substring(1)
        }).ToList();

        var result = new List<Dictionary<string, object?>>();

        foreach (var flatItem in flatItems)
        {
            var transformedItem = new Dictionary<string, object?>();
            var nestedObjects = new Dictionary<string, Dictionary<string, object?>>();

            foreach (var kvp in flatItem)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                bool isExpanded = false;
                foreach (var np in navPrefixes)
                {
                    string? actualKey = null;

                    if (key.StartsWith(np.SnakePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        actualKey = key.Substring(np.SnakePrefix.Length);
                    }
                    else if (key.StartsWith(np.PascalPrefix, StringComparison.Ordinal)
                             && key.Length > np.PascalPrefix.Length
                             && char.IsUpper(key[np.PascalPrefix.Length]))
                    {
                        actualKey = key.Substring(np.PascalPrefix.Length);
                    }

                    if (actualKey != null)
                    {
                        if (!nestedObjects.TryGetValue(np.Nav, out var nestedObj))
                        {
                            nestedObj = new Dictionary<string, object?>();
                            nestedObjects[np.Nav] = nestedObj;
                        }
                        nestedObj[actualKey] = value;

                        if (actualKey.Equals("id", StringComparison.OrdinalIgnoreCase))
                            transformedItem[key] = value;

                        isExpanded = true;
                        break;
                    }
                }

                if (!isExpanded)
                    transformedItem[key] = value;
            }

            foreach (var nested in nestedObjects)
            {
                var hasId = nested.Value.Any(kv =>
                    kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase) && kv.Value != null);
                transformedItem[nested.Key] = hasId ? nested.Value : null;
            }

            result.Add(transformedItem);
        }

        return result;
    }

    /// <summary>
    /// Find a OneToMany composition or association by navigation name.
    /// </summary>
    public static BmAssociation? FindOneToManyAssociation(BmEntity entityDef, string navName)
    {
        var comp = entityDef.Compositions.FirstOrDefault(c =>
            c.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));
        if (comp != null && comp.Cardinality == BmCardinality.OneToMany)
            return comp;

        var assoc = entityDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));
        if (assoc != null && (assoc.Cardinality == BmCardinality.OneToMany || assoc.Cardinality == BmCardinality.ManyToMany))
            return assoc;

        return null;
    }

    /// <summary>
    /// Resolve the child entity definition from a composition/association target.
    /// </summary>
    public async Task<BmEntity?> ResolveChildEntityAsync(BmAssociation assoc)
    {
        var cache = await GetCacheAsync();
        return cache.GetEntity(assoc.TargetEntity);
    }

    private async Task ExpandOneToManyProperties(
        BmEntity entityDef,
        Dictionary<string, object?> result,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct)
    {
        foreach (var (navName, navOptions) in expandOptions)
        {
            var assoc = FindOneToManyAssociation(entityDef, navName);
            if (assoc == null) continue;

            var childEntity = await ResolveChildEntityAsync(assoc);
            if (childEntity == null) continue;

            var parentId = result.GetIdValue();
            if (parentId == null) continue;

            if (assoc.Cardinality == BmCardinality.ManyToMany)
            {
                var childRows = await ExpandManyToManyProperty(entityDef, childEntity, parentId, tenantId, navOptions, ct);
                result[navName] = childRows;
            }
            else
            {
                var parentFkColumn = NamingConvention.GetFkColumnName(entityDef.Name);
                var childFilter = $"{Q(parentFkColumn)} eq '{parentId}'";

                if (!string.IsNullOrWhiteSpace(navOptions.Filter))
                    childFilter = $"({childFilter}) and ({navOptions.Filter})";

                var childOptions = new QueryOptions
                {
                    TenantId = childEntity.TenantScoped ? tenantId : null,
                    Filter = childFilter,
                    OrderBy = navOptions.OrderBy,
                    Select = navOptions.Select,
                    Top = navOptions.Top,
                    Skip = navOptions.Skip
                };

                var (sql, parameters) = _sqlBuilder.BuildSelectQuery(childEntity, childOptions);

                if (!string.IsNullOrEmpty(assoc.OnConditionString) && IsOnConditionSafe(assoc.OnConditionString, assoc.Name))
                {
                    var onCondition = assoc.OnConditionString
                        .Replace("$self.", $"{NamingConvention.ToSnakeCase(entityDef.Name)}.", StringComparison.OrdinalIgnoreCase);
                    // Insert onCondition into WHERE clause, before ORDER BY/LIMIT/OFFSET
                    var insertPos = sql.IndexOf(" ORDER BY", StringComparison.OrdinalIgnoreCase);
                    if (insertPos < 0) insertPos = sql.IndexOf(" LIMIT", StringComparison.OrdinalIgnoreCase);
                    if (insertPos < 0) insertPos = sql.IndexOf(" OFFSET", StringComparison.OrdinalIgnoreCase);
                    if (insertPos >= 0)
                        sql = sql.Insert(insertPos, $" AND ({onCondition})");
                    else
                        sql += $" AND ({onCondition})";
                }

                var childRows = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
                result[navName] = childRows;
            }
        }
    }

    private async Task<List<Dictionary<string, object?>>> ExpandManyToManyProperty(
        BmEntity sourceEntity, BmEntity targetEntity, object parentId,
        Guid? tenantId, ExpandOptions navOptions, CancellationToken ct)
    {
        var names = new[] { sourceEntity.Name.ToLowerInvariant(), targetEntity.Name.ToLowerInvariant() };
        Array.Sort(names);
        var junctionName = $"{names[0]}_{names[1]}";

        var schema = !string.IsNullOrEmpty(sourceEntity.Namespace)
            ? NamingConvention.GetSchemaName(sourceEntity.Namespace) : null;
        var qualifiedJunction = schema != null ? $"\"{schema}\".\"{junctionName}\"" : $"\"{junctionName}\"";

        var targetTable = _sqlBuilder.GetTableName(targetEntity);
        var sourceFk = NamingConvention.GetFkColumnName(sourceEntity.Name);
        var targetFk = NamingConvention.GetFkColumnName(targetEntity.Name);

        var selectColumns = BuildExpandSelectColumns(targetEntity, navOptions.Select, "t");

        var sql = $"SELECT {selectColumns} FROM {targetTable} t INNER JOIN {qualifiedJunction} j ON j.{Q(targetFk)} = t.{Q("id")} WHERE j.{Q(sourceFk)} = @p0";
        var paramList = new List<Npgsql.NpgsqlParameter> { new("@p0", parentId) };

        // Add tenant filter for ManyToMany junction table
        if (tenantId.HasValue)
        {
            sql += $" AND j.{Q("tenant_id")} = @pTenant";
            paramList.Add(new Npgsql.NpgsqlParameter("@pTenant", tenantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(navOptions.Filter))
        {
            var nestedParser = new FilterExpressionParser();
            var (nestedWhere, nestedParams) = nestedParser.Parse(navOptions.Filter);
            sql += $" AND ({nestedWhere})";
            paramList.AddRange(nestedParams);
        }

        if (!string.IsNullOrWhiteSpace(navOptions.OrderBy))
        {
            var orderByClause = FilterExpressionParser.ParseOrderBy(navOptions.OrderBy);
            if (!string.IsNullOrEmpty(orderByClause))
                sql += $" ORDER BY {orderByClause}";
        }

        if (navOptions.Top.HasValue && navOptions.Top.Value > 0)
            sql += $" LIMIT {navOptions.Top.Value}";
        if (navOptions.Skip.HasValue && navOptions.Skip.Value > 0)
            sql += $" OFFSET {navOptions.Skip.Value}";

        return await _queryExecutor.ExecuteListAsync(sql, paramList, ct);
    }

    private async Task ExpandOneToManyPropertiesBatched(
        BmEntity entityDef,
        List<Dictionary<string, object?>> items,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct)
    {
        foreach (var (navName, navOptions) in expandOptions)
        {
            var assoc = FindOneToManyAssociation(entityDef, navName);
            if (assoc == null) continue;

            var childEntity = await ResolveChildEntityAsync(assoc);
            if (childEntity == null) continue;

            var parentIds = new List<Guid>();
            foreach (var item in items)
            {
                var idVal = item.GetIdValue();
                if (idVal is Guid g) parentIds.Add(g);
                else if (idVal != null && Guid.TryParse(idVal.ToString(), out var parsed)) parentIds.Add(parsed);
            }

            if (parentIds.Count == 0) continue;

            var inValues = string.Join(",", parentIds.Select((pid, i) => $"@pChild{i}"));
            var parameters = new List<Npgsql.NpgsqlParameter>();
            for (var i = 0; i < parentIds.Count; i++)
                parameters.Add(new Npgsql.NpgsqlParameter($"@pChild{i}", parentIds[i]));

            string sql;
            string groupByFkColumn;

            if (assoc.Cardinality == BmCardinality.ManyToMany)
            {
                var names = new[] { entityDef.Name.ToLowerInvariant(), childEntity.Name.ToLowerInvariant() };
                Array.Sort(names);
                var junctionName = $"{names[0]}_{names[1]}";

                var schema = !string.IsNullOrEmpty(entityDef.Namespace)
                    ? NamingConvention.GetSchemaName(entityDef.Namespace) : null;
                var qualifiedJunction = schema != null ? $"\"{schema}\".\"{junctionName}\"" : $"\"{junctionName}\"";

                var targetTable = _sqlBuilder.GetTableName(childEntity);
                var sourceFk = NamingConvention.GetFkColumnName(entityDef.Name);
                var targetFk = NamingConvention.GetFkColumnName(childEntity.Name);

                var selectColumns = BuildExpandSelectColumns(childEntity, navOptions.Select, "t");
                sql = $"SELECT {selectColumns}, j.{Q(sourceFk)} AS __parent_fk FROM {targetTable} t INNER JOIN {qualifiedJunction} j ON j.{Q(targetFk)} = t.{Q("id")} WHERE j.{Q(sourceFk)} IN ({inValues})";

                if (tenantId.HasValue)
                {
                    sql += $" AND j.{Q("tenant_id")} = @pChildTenant";
                    parameters.Add(new Npgsql.NpgsqlParameter("@pChildTenant", tenantId.Value));
                }

                groupByFkColumn = "__parent_fk";
            }
            else
            {
                var parentFkColumn = NamingConvention.GetFkColumnName(entityDef.Name);
                var tableName = _sqlBuilder.GetTableName(childEntity);

                var selectColumns = BuildExpandSelectColumnsWithFk(childEntity, navOptions.Select, parentFkColumn);
                sql = $"SELECT {selectColumns} FROM {tableName} WHERE {Q(parentFkColumn)} IN ({inValues})";

                if (childEntity.TenantScoped && tenantId.HasValue)
                {
                    sql += $" AND {Q("tenant_id")} = @pChildTenant";
                    parameters.Add(new Npgsql.NpgsqlParameter("@pChildTenant", tenantId.Value));
                }

                if (!string.IsNullOrEmpty(assoc.OnConditionString) && IsOnConditionSafe(assoc.OnConditionString, assoc.Name))
                {
                    var onCondition = assoc.OnConditionString
                        .Replace("$self.", $"{NamingConvention.ToSnakeCase(entityDef.Name)}.", StringComparison.OrdinalIgnoreCase);
                    sql += $" AND ({onCondition})";
                }

                groupByFkColumn = parentFkColumn;
            }

            if (!string.IsNullOrWhiteSpace(navOptions.Filter))
            {
                var nestedParser = new FilterExpressionParser();
                var (nestedWhere, nestedParams) = nestedParser.Parse(navOptions.Filter);
                sql += $" AND ({nestedWhere})";
                parameters.AddRange(nestedParams);
            }

            if (!string.IsNullOrWhiteSpace(navOptions.OrderBy))
            {
                var orderByClause = FilterExpressionParser.ParseOrderBy(navOptions.OrderBy);
                if (!string.IsNullOrEmpty(orderByClause))
                    sql += $" ORDER BY {orderByClause}";
            }

            var allChildren = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);

            var grouped = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in allChildren)
            {
                var pascalFkColumn = NamingConvention.ToPascalCase(groupByFkColumn);
                var fkVal = child.TryGetValue(groupByFkColumn, out var fk) ? fk?.ToString()
                    : child.TryGetValue(pascalFkColumn, out fk) ? fk?.ToString() : null;
                if (fkVal == null) continue;

                if (assoc.Cardinality == BmCardinality.ManyToMany)
                {
                    child.Remove("__parent_fk");
                    child.Remove("__Parent_Fk");
                }

                if (!grouped.TryGetValue(fkVal, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    grouped[fkVal] = list;
                }
                list.Add(child);
            }

            foreach (var item in items)
            {
                var idVal = item.GetIdValue();
                var idStr = idVal?.ToString();
                IEnumerable<Dictionary<string, object?>> childList =
                    idStr != null && grouped.TryGetValue(idStr, out var children)
                        ? children
                        : new List<Dictionary<string, object?>>();

                if (navOptions.Skip.HasValue && navOptions.Skip.Value > 0)
                    childList = childList.Skip(navOptions.Skip.Value);
                if (navOptions.Top.HasValue && navOptions.Top.Value > 0)
                    childList = childList.Take(navOptions.Top.Value);

                item[navName] = childList.ToList();
            }
        }
    }

    private static string BuildExpandSelectColumns(BmEntity entity, string? selectClause, string alias)
    {
        if (string.IsNullOrWhiteSpace(selectClause))
            return $"{alias}.*";

        var requestedFields = selectClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columns = new List<string> { $"{alias}.id" };

        foreach (var field in entity.Fields)
        {
            var snakeName = NamingConvention.ToSnakeCase(field.Name);
            if (requestedFields.Contains(field.Name) || requestedFields.Contains(snakeName))
            {
                if (!snakeName.Equals("id", StringComparison.OrdinalIgnoreCase))
                    columns.Add($"{alias}.{snakeName}");
            }
        }

        return string.Join(", ", columns);
    }

    private static string BuildExpandSelectColumnsWithFk(BmEntity entity, string? selectClause, string fkColumn)
    {
        if (string.IsNullOrWhiteSpace(selectClause))
            return "*";

        var requestedFields = selectClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", fkColumn };

        foreach (var field in entity.Fields)
        {
            var snakeName = NamingConvention.ToSnakeCase(field.Name);
            if (requestedFields.Contains(field.Name) || requestedFields.Contains(snakeName))
                columns.Add(snakeName);
        }

        return string.Join(", ", columns);
    }

    private (string Sql, IReadOnlyList<Npgsql.NpgsqlParameter> Parameters) BuildInheritanceAwareSelectQuery(
        BmEntity entityDef, QueryOptions options, Guid? id = null)
    {
        if (entityDef.ParentEntity != null)
            return _sqlBuilder.BuildInheritanceSelectQuery(entityDef, options, id);
        return _sqlBuilder.BuildSelectQuery(entityDef, options, id);
    }

    /// <summary>
    /// Defense-in-depth validation for OnConditionString before SQL concatenation.
    /// Although OnConditionString originates from compiled BMMDL meta-model (not user input),
    /// we reject strings containing suspicious SQL patterns to prevent injection if
    /// the meta-model is ever corrupted or tampered with.
    /// </summary>
    private bool IsOnConditionSafe(string onCondition, string associationName)
    {
        // Reject obvious injection patterns: semicolons, comments, DDL/DML keywords
        ReadOnlySpan<string> forbidden =
        [
            ";", "--", "/*", "*/", "xp_", "EXEC ", "EXECUTE ",
            "DROP ", "ALTER ", "CREATE ", "TRUNCATE ",
            "DELETE ", "INSERT ", "UPDATE ",
            "GRANT ", "REVOKE ",
            "UNION ", "INTO ",
            "COPY ", "\\\\",
        ];

        foreach (var pattern in forbidden)
        {
            if (onCondition.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Skipping OnConditionString for association {Association}: " +
                    "contains forbidden SQL pattern {Pattern}. Value: {Value}",
                    associationName, pattern.Trim(), onCondition);
                return false;
            }
        }

        return true;
    }
}
