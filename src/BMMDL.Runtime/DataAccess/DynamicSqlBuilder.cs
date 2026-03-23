namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Plugins.Contexts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Generates parameterized SQL queries from BMMDL entity metadata.
/// Supports SELECT, INSERT, UPDATE, DELETE with tenant isolation.
/// Delegates specialized SQL generation to focused sub-builders.
/// </summary>
public class DynamicSqlBuilder : IDynamicSqlBuilder
{
    private readonly IMetaModelCache _cache;
    private readonly QueryPlanCache? _queryCache;
    private readonly string? _defaultSchema;
    private readonly PlatformFeatureRegistry? _featureRegistry;
    private readonly IFeatureFilterState? _filterState;
    private readonly ILogger<DynamicSqlBuilder> _logger;

    private readonly ExpandQueryBuilder _expandBuilder;
    private readonly TemporalQueryBuilder _temporalBuilder;
    private readonly InheritanceQueryBuilder _inheritanceBuilder;
    private readonly LocalizationQueryBuilder _localizationBuilder;
    private readonly SearchQueryBuilder _searchBuilder;
    private readonly DmlBuilder _dmlBuilder;

    /// <summary>
    /// Create a new SQL builder.
    /// </summary>
    /// <param name="cache">Meta-model cache for entity lookups.</param>
    /// <param name="queryCache">Optional query plan cache for SQL caching.</param>
    /// <param name="defaultSchema">Default schema if entity has no namespace.</param>
    /// <param name="featureRegistry">Optional plugin registry for feature-based query filtering.</param>
    /// <param name="filterState">Optional per-request filter state controlling which features are active.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public DynamicSqlBuilder(
        IMetaModelCache cache,
        QueryPlanCache? queryCache = null,
        string? defaultSchema = null,
        PlatformFeatureRegistry? featureRegistry = null,
        IFeatureFilterState? filterState = null,
        ILogger<DynamicSqlBuilder>? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _queryCache = queryCache;
        _defaultSchema = defaultSchema;
        _featureRegistry = featureRegistry;
        _filterState = filterState;
        _logger = logger ?? NullLogger<DynamicSqlBuilder>.Instance;

        _expandBuilder = new ExpandQueryBuilder(this, cache);
        _temporalBuilder = new TemporalQueryBuilder(this);
        _inheritanceBuilder = new InheritanceQueryBuilder(this);
        _localizationBuilder = new LocalizationQueryBuilder(this);
        _searchBuilder = new SearchQueryBuilder();
        _dmlBuilder = new DmlBuilder(this, featureRegistry, _logger);
    }

    /// <summary>
    /// Build a SELECT query for an entity.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="options">Query options (filter, order, pagination).</param>
    /// <param name="id">Optional ID for single record lookup.</param>
    /// <returns>SQL query and parameters.</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildSelectQuery(
        BmEntity entity,
        QueryOptions? options = null,
        Guid? id = null)
    {
        options ??= QueryOptions.Default;

        // Try cache lookup first (only for queries without dynamic ID)
        string? cacheKey = null;
        if (_queryCache != null && !id.HasValue)
        {
            cacheKey = BuildCacheKey("SELECT", entity.Name, options);
            var cached = _queryCache.TryGet(cacheKey);
            if (cached.HasValue)
            {
                return cached.Value;
            }
        }

        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();

        var tableName = GetTableName(entity);

        // Reset plugin FROM override before building WHERE clauses
        _lastFromOverride = null;

        // Build WHERE clauses first when using plugin path, so _lastFromOverride
        // is populated before we determine the FROM source.
        // For locale-aware queries, WHERE clauses are built separately below.
        var localizedFields = entity.Fields.Where(f => f.TypeRef is BmLocalizedType).ToList();
        var useLocale = !string.IsNullOrEmpty(options.Locale) && localizedFields.Count > 0;

        List<string> whereClauses;
        if (!useLocale)
        {
            // Pre-build WHERE clauses so plugin FROM override is available
            whereClauses = BuildWhereClauses(entity, options, id, parameters);
        }
        else
        {
            whereClauses = null!; // Will be built below after FROM
        }

        // Determine FROM source: plugin override takes priority, then legacy temporal
        string fromSource;
        bool isTemporalSubquery;

        if (_lastFromOverride != null)
        {
            // Plugin contributed a FROM override (e.g., temporal UNION ALL)
            // Resolve {TABLE}_history first (before {TABLE}) so that suffix gets proper quoting
            var rawName = GetRawTableName(entity);
            var historyTable = QuoteQualifiedTableName(rawName + "_history");
            fromSource = _lastFromOverride
                .Replace("{TABLE}_history", historyTable)
                .Replace("{TABLE}", tableName);
            isTemporalSubquery = true;
        }
        else
        {
            // Legacy path: use TemporalQueryBuilder for SeparateTables + asOf
            // TemporalQueryBuilder expects unquoted table name (it does its own quoting)
            var rawTableName = GetRawTableName(entity);
            (fromSource, isTemporalSubquery) = _temporalBuilder.GetTemporalFromSource(
                entity, options, rawTableName, parameters);
            // If not a temporal subquery, the builder returned the raw name — re-quote it
            if (!isTemporalSubquery)
                fromSource = tableName; // already quoted
        }

        if (useLocale)
        {
            // Locale-aware SELECT: use COALESCE(t.field, m.field) for localized fields
            var columns = LocalizationQueryBuilder.BuildLocalizedSelectColumns(entity, localizedFields, options.Select);
            sql.Append($"SELECT {columns}");
            sql.Append($" FROM {fromSource} m");

            // LEFT JOIN _texts table filtered by locale
            var quotedTextsTable = QuoteQualifiedTableName(GetRawTableName(entity) + "_texts");
            var localeParam = $"@p{parameters.Count}";
            parameters.Add(new NpgsqlParameter(localeParam, options.Locale));
            sql.Append($" LEFT JOIN {quotedTextsTable} t ON t.id = m.id AND t.locale = {localeParam}");

            // Build WHERE clauses for locale path (plugin override already determined)
            var localizedFieldNames = LocalizationQueryBuilder.GetLocalizedFieldNames(entity);
            whereClauses = _localizationBuilder.BuildLocaleAwareWhereClauses(entity, localizedFieldNames, options, id, parameters);
        }
        else
        {
            // Standard SELECT (no locale)
            var columns = BuildSelectColumns(entity, options.Select);
            sql.Append($"SELECT {columns}");
            sql.Append($" FROM {fromSource}");
            if (isTemporalSubquery)
                sql.Append(" AS _temporal");
        }

        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereClauses));
        }

        // ORDER BY clause
        AppendOrderByClause(sql, options.OrderBy, entity);

        // LIMIT/OFFSET (pagination) - use parameterized values for security
        AppendPaginationClauses(sql, options.Top, options.Skip, parameters);

        var result = (sql.ToString(), (IReadOnlyList<NpgsqlParameter>)parameters);

        // Store in cache for future use
        if (_queryCache != null && cacheKey != null)
        {
            _queryCache.Set(cacheKey, result.Item1, result.Item2);
        }

        return result;
    }

    /// <summary>
    /// Build a SELECT query with expanded navigation properties (LEFT JOINs).
    /// Returns flat result set with prefixed column names for expanded entities.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters, List<string> ExpandedNavs) BuildSelectWithExpand(
        BmEntity entity,
        QueryOptions options,
        Guid? id = null)
        => _expandBuilder.BuildSelectWithExpand(entity, options, id);

    /// <summary>
    /// Build an INSERT query for an entity.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="data">Data to insert.</param>
    /// <param name="tenantId">Optional tenant ID for tenant-scoped entities.</param>
    /// <param name="userId">Optional user ID for resolving $user default values.</param>
    /// <returns>SQL query and parameters.</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInsertQuery(
        BmEntity entity,
        Dictionary<string, object?> data,
        Guid? tenantId = null,
        Guid? userId = null)
        => _dmlBuilder.BuildInsertQuery(entity, data, tenantId, userId);

    /// <summary>
    /// Build an UPDATE query for an entity.
    /// For non-temporal entities, returns a single UPDATE statement.
    /// For temporal entities, use BuildTemporalUpdateStatements instead.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="id">Record ID to update.</param>
    /// <param name="data">Data to update.</param>
    /// <param name="tenantId">Optional tenant ID for tenant-scoped entities.</param>
    /// <returns>SQL query and parameters.</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildUpdateQuery(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
        => _dmlBuilder.BuildUpdateQuery(entity, id, data, tenantId);

    /// <summary>
    /// Build an UPSERT query for the _texts companion table.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildTextsUpsertQuery(
        BmEntity entity,
        Guid entityId,
        string locale,
        Dictionary<string, object?> localizedData,
        Guid? tenantId = null)
        => _localizationBuilder.BuildTextsUpsertQuery(entity, entityId, locale, localizedData, tenantId);

    /// <summary>
    /// Check if an entity has any localized fields.
    /// </summary>
    public bool HasLocalizedFields(BmEntity entity)
        => LocalizationQueryBuilder.HasLocalizedFields(entity);

    /// <summary>
    /// Get the names of localized fields for an entity.
    /// </summary>
    public HashSet<string> GetLocalizedFieldNames(BmEntity entity)
        => LocalizationQueryBuilder.GetLocalizedFieldNames(entity);

    /// <summary>
    /// Build temporal UPDATE statements for entities with @Temporal annotation.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildTemporalUpdateStatements(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
        => _temporalBuilder.BuildTemporalUpdateStatements(entity, id, data, tenantId);

    /// <summary>
    /// Build a DELETE query for an entity.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="id">Record ID to delete.</param>
    /// <param name="tenantId">Optional tenant ID for tenant-scoped entities.</param>
    /// <param name="softDelete">If true, set is_deleted = true instead of hard delete.</param>
    /// <returns>SQL query and parameters.</returns>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteQuery(
        BmEntity entity,
        Guid id,
        Guid? tenantId = null,
        bool softDelete = false)
        => _dmlBuilder.BuildDeleteQuery(entity, id, tenantId, softDelete);

    // ============================================================
    // Inheritance-Aware Query Helpers (delegated to InheritanceQueryBuilder)
    // ============================================================

    /// <summary>
    /// Build a SELECT query for a child entity that JOINs the parent table to get inherited fields.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInheritanceSelectQuery(
        BmEntity entity,
        QueryOptions? options = null,
        Guid? id = null)
        => _inheritanceBuilder.BuildInheritanceSelectQuery(entity, options, id);

    /// <summary>
    /// Build a polymorphic SELECT for a parent entity that LEFT JOINs all child tables.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildPolymorphicSelectQuery(
        BmEntity parentEntity,
        List<BmEntity> derivedEntities,
        QueryOptions? options = null,
        Guid? id = null)
        => _inheritanceBuilder.BuildPolymorphicSelectQuery(parentEntity, derivedEntities, options, id);

    /// <summary>
    /// Build INSERT queries for a child entity in a table-per-type hierarchy.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceInsertQueries(
        BmEntity entity,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
        => _inheritanceBuilder.BuildInheritanceInsertQueries(entity, data, tenantId);

    /// <summary>
    /// Build UPDATE queries for a child entity in a table-per-type hierarchy.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceUpdateQueries(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
        => _inheritanceBuilder.BuildInheritanceUpdateQueries(entity, id, data, tenantId);

    /// <summary>
    /// Build DELETE queries for a child entity in a table-per-type hierarchy.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceDeleteQueries(
        BmEntity entity,
        Guid id,
        Guid? tenantId = null,
        bool softDelete = false)
        => _inheritanceBuilder.BuildInheritanceDeleteQueries(entity, id, tenantId, softDelete);

    /// <summary>
    /// Builds a query to delete (or soft-delete/temporal-close) orphaned children
    /// that are NOT in the keepIds list.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteOrphansQuery(
        BmEntity childEntity,
        string fkColumnName,
        object parentId,
        IReadOnlyList<Guid> keepIds,
        Guid? tenantId = null)
        => _dmlBuilder.BuildDeleteOrphansQuery(childEntity, fkColumnName, parentId, keepIds, tenantId);

    /// <summary>
    /// Build a COUNT query for an entity.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildCountQuery(
        BmEntity entity,
        QueryOptions? options = null)
    {
        options ??= QueryOptions.Default;
        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();

        var tableName = GetTableName(entity);
        var hasParent = entity.ParentEntity != null;

        // Reset plugin FROM override
        _lastFromOverride = null;

        // Build WHERE clauses first so plugin FROM override is populated
        var whereClauses = BuildWhereClauses(entity, options, id: null, parameters);

        // Determine FROM source: plugin override takes priority, then legacy temporal
        string fromSource;
        bool isTemporalSubquery;

        if (_lastFromOverride != null)
        {
            var rawName = GetRawTableName(entity);
            var historyTable = QuoteQualifiedTableName(rawName + "_history");
            fromSource = _lastFromOverride
                .Replace("{TABLE}_history", historyTable)
                .Replace("{TABLE}", tableName);
            isTemporalSubquery = true;
        }
        else
        {
            var rawTableName = GetRawTableName(entity);
            (fromSource, isTemporalSubquery) = _temporalBuilder.GetTemporalFromSource(
                entity, options, rawTableName, parameters);
            if (!isTemporalSubquery)
                fromSource = tableName; // already quoted
        }

        if (hasParent)
        {
            var parentTable = GetTableName(entity.ParentEntity!);
            sql.Append($"SELECT COUNT(*) FROM {fromSource} c INNER JOIN {parentTable} p ON c.id = p.id");
        }
        else
        {
            sql.Append($"SELECT COUNT(*) FROM {fromSource}");
            if (isTemporalSubquery)
                sql.Append(" AS _temporal");
        }

        // Inheritance-specific overrides for soft-delete (parent table alias)
        if (hasParent)
        {
            // Replace the unqualified soft-delete clause with parent-qualified one.
            // Uses the tracked clause index from plugin pipeline instead of fragile string matching.
            if (_lastSoftDeleteClauseIndex >= 0 && _lastSoftDeleteClauseIndex < whereClauses.Count)
            {
                if (HasField(entity.ParentEntity!, "IsDeleted"))
                    whereClauses[_lastSoftDeleteClauseIndex] = "p.is_deleted = false";
            }

            // Qualify unqualified clauses with parent alias for inheritance queries
            for (int i = 0; i < whereClauses.Count; i++)
            {
                var clause = whereClauses[i];
                // Only add prefix if not already prefixed with a table alias
                if (clause.StartsWith($"{SchemaConstants.TenantIdColumn} =") &&
                    !clause.TrimStart().StartsWith("p.", StringComparison.OrdinalIgnoreCase) &&
                    !clause.TrimStart().StartsWith("c.", StringComparison.OrdinalIgnoreCase) &&
                    !clause.TrimStart().StartsWith("t.", StringComparison.OrdinalIgnoreCase))
                {
                    whereClauses[i] = "p." + clause;
                }
            }
        }

        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereClauses));
        }

        return (sql.ToString(), parameters);
    }

    /// <summary>
    /// Build an EXISTS query for checking if a record exists.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildExistsQuery(
        BmEntity entity,
        Guid id,
        Guid? tenantId = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();
        var hasParent = entity.ParentEntity != null;

        var idParamName = $"@p{parameters.Count}";

        if (hasParent)
        {
            var childTable = GetTableName(entity);
            var parentTable = GetTableName(entity.ParentEntity!);
            sql.Append($"SELECT 1 FROM {childTable} c INNER JOIN {parentTable} p ON c.id = p.id WHERE c.id = {idParamName}");
        }
        else
        {
            var tableName = GetTableName(entity);
            sql.Append($"SELECT 1 FROM {tableName} WHERE id = {idParamName}");
        }
        parameters.Add(new NpgsqlParameter(idParamName, id));

        if (entity.TenantScoped && tenantId.HasValue)
        {
            var tenantParamName = $"@p{parameters.Count}";
            var col = hasParent ? $"p.{SchemaConstants.TenantIdColumn}" : SchemaConstants.TenantIdColumn;
            sql.Append($" AND {col} = {tenantParamName}");
            parameters.Add(new NpgsqlParameter(tenantParamName, tenantId.Value));
        }

        sql.Append(" LIMIT 1");

        return (sql.ToString(), parameters);
    }

    /// <summary>
    /// Get the fully qualified and quoted table name for an entity.
    /// Returns e.g., "platform"."sales_order" — safe to embed directly in SQL.
    /// </summary>
    public string GetTableName(BmEntity entity)
    {
        var schema = !string.IsNullOrEmpty(entity.Namespace)
            ? NamingConvention.GetSchemaName(entity.Namespace)
            : _defaultSchema;

        var raw = NamingConvention.GetTableName(entity.Name, schema);
        return QuoteQualifiedTableName(raw);
    }

    /// <summary>
    /// Get the unquoted fully qualified table name for an entity.
    /// Used when the raw name is needed (e.g., to append suffixes before quoting).
    /// </summary>
    internal string GetRawTableName(BmEntity entity)
    {
        var schema = !string.IsNullOrEmpty(entity.Namespace)
            ? NamingConvention.GetSchemaName(entity.Namespace)
            : _defaultSchema;

        return NamingConvention.GetTableName(entity.Name, schema);
    }

    /// <summary>
    /// Quote a potentially schema-qualified table name, quoting each part separately.
    /// E.g., "platform.sales_order" -> "\"platform\".\"sales_order\""
    /// </summary>
    internal static string QuoteQualifiedTableName(string tableName)
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

    /// <inheritdoc />
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildJunctionInsertQuery(
        BmEntity sourceEntity, BmEntity targetEntity, Guid sourceId, Guid targetId, Guid? tenantId = null)
    {
        var (qualifiedJunction, sourceFk, targetFk) = GetJunctionTableInfo(sourceEntity, targetEntity);
        var hasTenant = tenantId.HasValue;

        var sql = hasTenant
            ? $"INSERT INTO {qualifiedJunction} ({sourceFk}, {targetFk}, tenant_id) VALUES (@p0, @p1, @pTenant) ON CONFLICT DO NOTHING"
            : $"INSERT INTO {qualifiedJunction} ({sourceFk}, {targetFk}) VALUES (@p0, @p1) ON CONFLICT DO NOTHING";

        var parameters = new List<NpgsqlParameter>
        {
            new("@p0", sourceId),
            new("@p1", targetId)
        };
        if (hasTenant)
            parameters.Add(new("@pTenant", tenantId!.Value));

        return (sql, parameters);
    }

    /// <inheritdoc />
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildJunctionDeleteQuery(
        BmEntity sourceEntity, BmEntity targetEntity, Guid sourceId, Guid targetId, Guid? tenantId = null)
    {
        var (qualifiedJunction, sourceFk, targetFk) = GetJunctionTableInfo(sourceEntity, targetEntity);
        var hasTenant = tenantId.HasValue;

        var sql = hasTenant
            ? $"DELETE FROM {qualifiedJunction} WHERE {sourceFk} = @p0 AND {targetFk} = @p1 AND tenant_id = @pTenant"
            : $"DELETE FROM {qualifiedJunction} WHERE {sourceFk} = @p0 AND {targetFk} = @p1";

        var parameters = new List<NpgsqlParameter>
        {
            new("@p0", sourceId),
            new("@p1", targetId)
        };
        if (hasTenant)
            parameters.Add(new("@pTenant", tenantId!.Value));

        return (sql, parameters);
    }

    private static (string QualifiedJunction, string SourceFk, string TargetFk) GetJunctionTableInfo(
        BmEntity sourceEntity, BmEntity targetEntity)
    {
        var names = new[] { sourceEntity.Name.ToLowerInvariant(), targetEntity.Name.ToLowerInvariant() };
        Array.Sort(names);
        var junctionName = $"{names[0]}_{names[1]}";

        var schema = !string.IsNullOrEmpty(sourceEntity.Namespace)
            ? NamingConvention.GetSchemaName(sourceEntity.Namespace)
            : null;
        var qualifiedJunction = schema != null ? $"\"{schema}\".\"{junctionName}\"" : $"\"{junctionName}\"";

        var sourceFk = NamingConvention.GetFkColumnName(sourceEntity.Name);
        var targetFk = NamingConvention.GetFkColumnName(targetEntity.Name);

        return (qualifiedJunction, sourceFk, targetFk);
    }

    // ============================================================
    // Shared Helpers — internal for use by sub-builders
    // ============================================================

    /// <summary>
    /// Build SELECT columns from entity fields.
    /// </summary>
    internal static string BuildSelectColumns(BmEntity entity, string? selectClause)
    {
        if (string.IsNullOrWhiteSpace(selectClause))
        {
            // Select all columns
            return "*";
        }

        // Parse selected fields
        var requestedFields = selectClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columns = new List<string>();

        // Always include ID (quoted for safety with reserved keywords)
        columns.Add(NamingConvention.QuoteIdentifier("id"));

        foreach (var field in entity.Fields)
        {
            if (requestedFields.Contains(field.Name))
            {
                columns.Add(NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(field.Name)));
            }
        }

        // Add tenant_id if entity is tenant-scoped and not already included
        var quotedTenantCol = NamingConvention.QuoteIdentifier(SchemaConstants.TenantIdColumn);
        if (entity.TenantScoped && !columns.Contains(quotedTenantCol))
        {
            columns.Add(quotedTenantCol);
        }

        return string.Join(", ", columns.Distinct());
    }

    /// <summary>
    /// Check if entity has a specific field.
    /// </summary>
    internal static bool HasField(BmEntity entity, string fieldName)
    {
        return entity.Fields.Any(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get list of searchable text field column names.
    /// </summary>
    internal static List<string> GetSearchableFields(BmEntity entity)
        => SearchQueryBuilder.GetSearchableFields(entity);

    /// <summary>
    /// Convert values from JSON deserialization to native types.
    /// </summary>
    internal static object? ConvertValue(object? value)
    {
        if (value == null)
            return null;

        // Handle JsonElement from ASP.NET Core JSON deserialization
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when jsonElement.TryGetInt32(out var i) => i,
                JsonValueKind.Number when jsonElement.TryGetInt64(out var l) => l,
                JsonValueKind.Number when jsonElement.TryGetDecimal(out var d) => d,
                JsonValueKind.Number => jsonElement.GetDouble(),
                JsonValueKind.String when jsonElement.TryGetGuid(out var g) => g,
                JsonValueKind.String when jsonElement.TryGetDateTime(out var dt) => dt,
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Object => jsonElement.GetRawText(), // Store as JSON string
                JsonValueKind.Array => jsonElement.GetRawText(),  // Store as JSON string
                _ => value.ToString()
            };
        }

        return value;
    }

    /// <summary>
    /// Type-aware value conversion using the BMMDL field type string.
    /// </summary>
    internal static object? ConvertValueTyped(object? value, string? fieldType)
    {
        // Handle array types before generic conversion — we need the raw JsonElement
        if (!string.IsNullOrEmpty(fieldType) && fieldType.StartsWith("Array<", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertArrayValue(value, fieldType, fieldName: null);
        }

        var converted = ConvertValue(value);
        if (converted == null || string.IsNullOrEmpty(fieldType))
            return converted;

        // If already the right .NET type, return as-is
        if (converted is not string strVal)
            return converted;

        // Resolve known type aliases and check if we need numeric/boolean conversion
        var normalizedType = fieldType.TrimEnd('?').ToLowerInvariant();
        // Strip type parameters: "Decimal(15,2)" → "decimal", "String(100)" → "string"
        var parenIdx = normalizedType.IndexOf('(');
        if (parenIdx > 0) normalizedType = normalizedType[..parenIdx];

        return normalizedType switch
        {
            "integer" or "int" when int.TryParse(strVal, out var i) => i,
            "integer" or "int" when long.TryParse(strVal, out var l) => l,
            "decimal" or "amount" or "quantity" or "money" or "percentage" when decimal.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            "boolean" or "bool" when bool.TryParse(strVal, out var b) => b,
            _ => converted
        };
    }

    /// <summary>
    /// Append ORDER BY clause to SQL if specified.
    /// </summary>
    internal static void AppendOrderByClause(StringBuilder sql, string? orderBy, BmEntity? entityDef = null)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return;

        var orderBySql = FilterExpressionParser.ParseOrderBy(orderBy, entityDef);
        if (!string.IsNullOrEmpty(orderBySql))
        {
            sql.Append($" ORDER BY {orderBySql}");
        }
    }

    /// <summary>
    /// Append LIMIT/OFFSET clauses to SQL for pagination using parameterized values.
    /// </summary>
    internal static void AppendPaginationClauses(StringBuilder sql, int? top, int? skip, List<NpgsqlParameter>? parameters = null)
    {
        // Validate bounds to prevent negative values and ensure reasonable limits
        if (top.HasValue)
        {
            var limitValue = Math.Max(0, top.Value);
            if (parameters != null)
            {
                var paramName = $"@p_limit_{parameters.Count}";
                sql.Append(" LIMIT ").Append(paramName);
                parameters.Add(new NpgsqlParameter(paramName, limitValue));
            }
            else
            {
                sql.Append(" LIMIT ").Append(limitValue);
            }
        }
        if (skip.HasValue && skip.Value > 0)
        {
            var offsetValue = Math.Max(0, skip.Value);
            if (parameters != null)
            {
                var paramName = $"@p_offset_{parameters.Count}";
                sql.Append(" OFFSET ").Append(paramName);
                parameters.Add(new NpgsqlParameter(paramName, offsetValue));
            }
            else
            {
                sql.Append(" OFFSET ").Append(offsetValue);
            }
        }
    }

    /// <summary>
    /// Add custom OData $filter clause to WHERE conditions.
    /// </summary>
    internal void AddCustomFilter(
        List<string> whereClauses,
        string? filter,
        List<NpgsqlParameter> parameters,
        BmEntity? entity = null)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return;

        var parser = entity != null ? new FilterExpressionParser(entity) : new FilterExpressionParser();
        var (filterSql, filterParams) = parser.Parse(filter);

        // Build parameter name mapping upfront, then do a single-pass replacement
        // to prevent collisions (e.g., renaming @p0→@p1 when @p1 already exists in SQL)
        var reindexedSql = filterSql;
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
            reindexedSql = Regex.Replace(reindexedSql, pattern, m => paramMap[m.Value]);
        }
        whereClauses.Add(reindexedSql);
    }

    // ============================================================
    // Private Helpers
    // ============================================================

    /// <summary>
    /// Build WHERE clause conditions for a SELECT query.
    /// Uses the plugin waterfall path when a PlatformFeatureRegistry is available,
    /// falling back to the legacy hardcoded path otherwise.
    /// </summary>
    internal List<string> BuildWhereClauses(
        BmEntity entity,
        QueryOptions options,
        Guid? id,
        List<NpgsqlParameter> parameters)
    {
        var whereClauses = new List<string>();

        // Reset soft-delete clause tracking for this query build
        _lastSoftDeleteClauseIndex = -1;

        // ID filter (for single record lookup) — always applies regardless of plugin path
        if (id.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, id.Value));
        }

        // Cross-cutting filters via plugin pipeline (tenant, soft-delete, temporal, etc.)
        // When no registry is available, no cross-cutting filters are applied.
        ApplyFeatureFilters(entity, options, whereClauses, parameters);

        // Custom filter (OData-style) — always applies, not a cross-cutting concern
        AddCustomFilter(whereClauses, options.Filter, parameters, entity);

        // Full-text search (OData $search) — always applies, not a cross-cutting concern
        AddSearchFilter(whereClauses, entity, options.Search, parameters, options.SearchCaseSensitive);

        return whereClauses;
    }

    /// <summary>
    /// Run the plugin waterfall chain for query filters.
    /// Returns true if plugins were applied (i.e., registry is available and at least one filter matched),
    /// false if legacy path should be used.
    /// </summary>
    private bool ApplyFeatureFilters(
        BmEntity entity,
        QueryOptions options,
        List<string> whereClauses,
        List<NpgsqlParameter> parameters)
    {
        if (_featureRegistry == null || _filterState == null)
            return false;

        var filters = _featureRegistry.GetFiltersFor(entity).ToList();
        if (filters.Count == 0)
            return false;

        // When IncludeDeleted is true, temporarily disable SoftDelete so the
        // plugin waterfall skips the is_deleted filter.
        // Use 'using var' to ensure disposal even if the waterfall throws.
        using var softDeleteScope = options.IncludeDeleted
            ? _filterState.Disable("SoftDelete")
            : null;

        var ctx = new QueryFilterContext
        {
            TenantId = options.TenantId,
            UserId = null, // QueryOptions does not carry UserId; extend if needed
            Locale = options.Locale,
            AsOf = options.AsOf,
            ValidAt = options.ValidAt,
            FilterState = _filterState
        };

        foreach (var filter in filters)
        {
            if (!_filterState.IsEnabled(filter.Name))
                continue;
            var whereSnapshot = ctx.WhereClauses.Count;
            var joinSnapshot = ctx.JoinClauses.Count;
            var paramSnapshot = ctx.Parameters.Count;
            var fromOverride = ctx.FromOverride;
            try
            {
                ctx = filter.ApplyFilter(entity, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature '{FeatureName}' failed during query filter for entity '{EntityName}', skipping",
                    filter.Name, entity.Name);
                // Rollback partial filter modifications to prevent clause/param mismatch
                if (ctx.WhereClauses.Count > whereSnapshot)
                    ctx.WhereClauses.RemoveRange(whereSnapshot, ctx.WhereClauses.Count - whereSnapshot);
                if (ctx.JoinClauses.Count > joinSnapshot)
                    ctx.JoinClauses.RemoveRange(joinSnapshot, ctx.JoinClauses.Count - joinSnapshot);
                if (ctx.Parameters.Count > paramSnapshot)
                    ctx.Parameters.RemoveRange(paramSnapshot, ctx.Parameters.Count - paramSnapshot);
                ctx.FromOverride = fromOverride;
            }
        }

        // Transfer accumulated WHERE clauses and parameters from the plugin context.
        // Track the index of the SoftDelete clause for inheritance query rewriting.
        foreach (var clause in ctx.WhereClauses)
        {
            if (string.Equals(clause.Source, "SoftDelete", StringComparison.OrdinalIgnoreCase))
                _lastSoftDeleteClauseIndex = whereClauses.Count;
            whereClauses.Add(clause.Expression);
        }
        parameters.AddRange(ctx.Parameters);

        // Store FromOverride for use by BuildSelectQuery (temporal UNION ALL source)
        _lastFromOverride = ctx.FromOverride;

        return true;
    }

    /// <summary>
    /// Stores the FROM override produced by the most recent plugin waterfall run.
    /// Used by BuildSelectQuery to replace the default FROM source when temporal
    /// plugins need a UNION ALL subquery (SeparateTables strategy).
    /// Reset on each BuildSelectQuery/BuildCountQuery call.
    /// </summary>
    private string? _lastFromOverride;

    /// <summary>
    /// Tracks the index (within the WHERE clauses list) of the soft-delete clause
    /// contributed by <see cref="BMMDL.Runtime.Plugins.Features.SoftDeleteFeature"/>.
    /// Used by BuildCountQuery to re-qualify the clause for inheritance queries
    /// without fragile string matching. -1 means no soft-delete clause was added.
    /// Reset on each BuildWhereClauses call.
    /// </summary>
    private int _lastSoftDeleteClauseIndex = -1;

    /// <summary>
    /// Add search clause to WHERE conditions.
    /// Supports OData $search syntax: AND (implicit/explicit), OR, NOT, and quoted phrases.
    /// </summary>
    private void AddSearchFilter(
        List<string> whereClauses,
        BmEntity entity,
        string? search,
        List<NpgsqlParameter> parameters,
        bool caseSensitive = false)
        => _searchBuilder.AddSearchFilter(whereClauses, entity, search, parameters, caseSensitive);

    /// <summary>
    /// <summary>
    /// Parse an OData $search expression into structured terms with operators.
    /// Delegates to <see cref="SearchQueryBuilder"/>.
    /// </summary>
    internal static List<SearchQueryBuilder.SearchTerm> ParseSearchExpression(string search)
        => SearchQueryBuilder.ParseSearchExpression(search);

    // ReadNextToken — moved to SearchQueryBuilder

    // BuildSearchTermsSql — moved to SearchQueryBuilder

    // Backward-compatible aliases — canonical types live in SearchQueryBuilder
    internal static class SearchOperator
    {
        internal static SearchQueryBuilder.SearchOperator And => SearchQueryBuilder.SearchOperator.And;
        internal static SearchQueryBuilder.SearchOperator Or => SearchQueryBuilder.SearchOperator.Or;
    }

    /// <summary>
    /// Convert a value to a .NET array for PostgreSQL array column storage.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="fieldType">The BMMDL field type string (e.g., "Array&lt;Integer&gt;").</param>
    /// <param name="fieldName">Optional field name for error context in diagnostics.</param>
    private static object? ConvertArrayValue(object? value, string fieldType, string? fieldName = null)
    {
        if (value == null)
            return null;

        // Extract element type from "Array<ElementType>"
        var elementType = fieldType[6..^1].Trim(); // Strip "Array<" and ">"
        var normalizedElement = elementType.TrimEnd('?').ToLowerInvariant();
        var elementParenIdx = normalizedElement.IndexOf('(');
        if (elementParenIdx > 0) normalizedElement = normalizedElement[..elementParenIdx];

        // Get array elements from the value
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null)
                return null;
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                return ConvertJsonArrayElements(jsonElement.EnumerateArray(), normalizedElement);
            }
        }
        else if (value is string jsonString && jsonString.StartsWith("["))
        {
            // Raw JSON string — parse and convert within the same scope
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                return ConvertJsonArrayElements(doc.RootElement.EnumerateArray(), normalizedElement);
            }
            catch (JsonException ex)
            {
                var fieldContext = !string.IsNullOrEmpty(fieldName) ? $" for field '{fieldName}'" : "";
                throw new ArgumentException($"Invalid JSON array value{fieldContext}: {ex.Message}", ex);
            }
        }

        return value; // Not an array — return as-is
    }

    /// <summary>
    /// Convert a sequence of JsonElements to the appropriate .NET typed array.
    /// </summary>
    private static object ConvertJsonArrayElements(JsonElement.ArrayEnumerator elements, string normalizedElementType)
    {
        var list = elements.ToList();
        return normalizedElementType switch
        {
            "integer" or "int" => list.Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!)).ToArray(),
            "long" => list.Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt64() : long.Parse(e.GetString()!)).ToArray(),
            "decimal" or "amount" or "quantity" or "money" or "percentage" => list.Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDecimal() : decimal.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            "boolean" or "bool" => list.Select(e => e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => bool.Parse(e.GetString()!) }).ToArray(),
            "uuid" => list.Select(e => e.TryGetGuid(out var g) ? g : Guid.Parse(e.GetString()!)).ToArray(),
            "date" or "datetime" or "timestamp" => list.Select(e => e.TryGetDateTime(out var dt) ? dt : DateTime.Parse(e.GetString()!)).ToArray(),
            _ => list.Select(e => e.GetString() ?? "").ToArray() // Default: string[] (covers String, Text, etc.)
        };
    }

    /// <summary>
    /// Build a cache key for query plan caching.
    /// </summary>
    private static string BuildCacheKey(string operation, string entityName, QueryOptions options)
    {
        var parts = new List<string>
        {
            operation,
            entityName,
            options.Filter ?? "",
            options.OrderBy ?? "",
            options.Select ?? "",
            options.Search ?? "",
            options.Top?.ToString() ?? "",
            options.Skip?.ToString() ?? "",
            options.TenantId?.ToString() ?? "",
            options.AsOf?.ToString("O") ?? "",
            options.ValidAt?.ToString("O") ?? "",
            options.CurrentOnly.ToString(),
            options.IncludeDeleted.ToString()
        };
        return string.Join("|", parts);
    }

    /// <summary>
    /// Evaluate a field's default value, converting DSL expressions to proper .NET values.
    /// </summary>
    /// <param name="field">The field whose default value to evaluate.</param>
    /// <param name="userId">Current user ID for resolving $user.</param>
    /// <param name="tenantId">Current tenant ID for resolving $tenant.</param>
    // EvaluateDefaultValue — moved to DmlBuilder
}
