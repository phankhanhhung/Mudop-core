namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Plugins.Contexts;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text;

/// <summary>
/// Generates parameterized INSERT, UPDATE, DELETE SQL statements
/// including soft-delete, temporal InlineHistory, and orphan cleanup.
/// Supports an optional plugin path via <see cref="PlatformFeatureRegistry"/>
/// that runs alongside (and replaces) the legacy hardcoded cross-cutting logic.
/// </summary>
internal class DmlBuilder
{
    private readonly DynamicSqlBuilder _parent;
    private readonly PlatformFeatureRegistry? _featureRegistry;
    private readonly ILogger _logger;

    /// <summary>Shorthand for <see cref="NamingConvention.QuoteIdentifier"/>.</summary>
    private static string Q(string identifier) => NamingConvention.QuoteIdentifier(identifier);

    internal DmlBuilder(DynamicSqlBuilder parent, PlatformFeatureRegistry? featureRegistry = null, ILogger? logger = null)
    {
        _parent = parent;
        _featureRegistry = featureRegistry;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    // ── INSERT ──────────────────────────────────────────────────────

    internal (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInsertQuery(
        BmEntity entity,
        Dictionary<string, object?> data,
        Guid? tenantId = null,
        Guid? userId = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var columns = new List<string>();
        var values = new List<string>();

        // Ensure ID is set
        if (!data.ContainsKey("Id") && !data.ContainsKey("id"))
        {
            data["Id"] = Guid.NewGuid();
        }

        // Build a lookup for field types (case-insensitive)
        var fieldTypes = entity.Fields.ToDictionary(f => f.Name, f => f.TypeString ?? "", StringComparer.OrdinalIgnoreCase);

        // Apply default values for fields not provided in data
        foreach (var field in entity.Fields)
        {
            if (field.IsComputed || field.IsVirtual)
                continue;

            // Skip if already provided (case-insensitive)
            if (data.Keys.Any(k => k.Equals(field.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Skip system/audit fields that are auto-managed
            if (field.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("createdBy", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("modifiedAt", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("modifiedBy", StringComparison.OrdinalIgnoreCase))
                continue;

            if (field.DefaultExpr != null || !string.IsNullOrEmpty(field.DefaultValueString))
            {
                data[field.Name] = EvaluateDefaultValue(field, userId, tenantId);
            }
        }

        // Build columns and parameters from data
        foreach (var kvp in data)
        {
            var columnName = NamingConvention.GetColumnName(kvp.Key);
            var paramName = $"@p{parameters.Count}";

            columns.Add(Q(columnName));
            values.Add(paramName);

            fieldTypes.TryGetValue(kvp.Key, out var fieldType);
            parameters.Add(new NpgsqlParameter(paramName, DynamicSqlBuilder.ConvertValueTyped(kvp.Value, fieldType) ?? DBNull.Value));
        }

        // Cross-cutting INSERT contributions via plugin pipeline (tenant_id, temporal columns, etc.)
        ApplyFeatureInsertContributions(entity, columns, values, parameters, data, tenantId, userId);

        var tableName = _parent.GetTableName(entity);
        var sql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}) RETURNING *";

        return (sql, parameters);
    }

    // ── UPDATE ──────────────────────────────────────────────────────

    internal (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildUpdateQuery(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var setClauses = new List<string>();

        // Build a lookup for field types (case-insensitive)
        var fieldTypes = entity.Fields.ToDictionary(f => f.Name, f => f.TypeString ?? "", StringComparer.OrdinalIgnoreCase);

        // Build SET clauses from data (excluding system/audit fields — plugins handle those)
        foreach (var kvp in data)
        {
            // Skip ID field
            if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip tenant_id
            if (kvp.Key.Equals("TenantId", StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip audit fields — AuditFieldFeature handles these via plugin pipeline
            if (kvp.Key.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("ModifiedAt", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("UpdatedBy", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("ModifiedBy", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("CreatedBy", StringComparison.OrdinalIgnoreCase))
                continue;

            var columnName = NamingConvention.GetColumnName(kvp.Key);
            var paramName = $"@p{parameters.Count}";

            setClauses.Add($"{Q(columnName)} = {paramName}");
            fieldTypes.TryGetValue(kvp.Key, out var fieldType);
            parameters.Add(new NpgsqlParameter(paramName, DynamicSqlBuilder.ConvertValueTyped(kvp.Value, fieldType) ?? DBNull.Value));
        }

        if (setClauses.Count == 0)
        {
            throw new ArgumentException("No fields to update.", nameof(data));
        }

        // Cross-cutting UPDATE contributions via plugin pipeline (audit timestamps, tenant WHERE, etc.)
        var additionalWhereClauses = new List<string>();
        ApplyFeatureUpdateContributions(
            entity, setClauses, parameters, additionalWhereClauses, data, tenantId, userId: null);

        var tableName = _parent.GetTableName(entity);
        var sql = new StringBuilder();
        sql.Append($"UPDATE {tableName} SET {string.Join(", ", setClauses)}");

        // WHERE clause
        var idParamName = $"@p{parameters.Count}";
        sql.Append($" WHERE {Q("id")} = {idParamName}");
        parameters.Add(new NpgsqlParameter(idParamName, id));

        // Append WHERE clauses contributed by feature plugins
        foreach (var whereClause in additionalWhereClauses)
        {
            sql.Append($" AND {whereClause}");
        }

        sql.Append(" RETURNING *");

        return (sql.ToString(), parameters);
    }

    // ── DELETE ──────────────────────────────────────────────────────

    internal (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteQuery(
        BmEntity entity,
        Guid id,
        Guid? tenantId = null,
        bool softDelete = false)
    {
        var parameters = new List<NpgsqlParameter>();
        var tableName = _parent.GetTableName(entity);

        // DELETE strategy via plugin pipeline (soft-delete, temporal close, etc.)
        // If a plugin claims the operation, its SQL is used. Otherwise, hard delete.
        if (_featureRegistry != null)
        {
            var deleteResult = ApplyFeatureDeleteStrategy(entity, tableName, id, tenantId);
            if (deleteResult.HasValue)
                return deleteResult.Value;
        }

        return BuildHardDeleteWithTenantFilter(tableName, entity, id, tenantId);
    }

    // ── DELETE ORPHANS ──────────────────────────────────────────────

    internal (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildDeleteOrphansQuery(
        BmEntity childEntity,
        string fkColumnName,
        object parentId,
        IReadOnlyList<Guid> keepIds,
        Guid? tenantId = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var tableName = _parent.GetTableName(childEntity);
        var fkColumn = NamingConvention.ToSnakeCase(fkColumnName);

        // Build the WHERE clause: fk = @parent [AND id NOT IN (...)] [AND tenant_id = @tenant]
        string BuildWhereClause(StringBuilder sql)
        {
            var parentParam = $"@p{parameters.Count}";
            sql.Append($" WHERE {Q(fkColumn)} = {parentParam}");
            parameters.Add(new NpgsqlParameter(parentParam, parentId));

            if (keepIds.Count > 0)
            {
                var inParams = new List<string>();
                foreach (var keepId in keepIds)
                {
                    var paramName = $"@p{parameters.Count}";
                    inParams.Add(paramName);
                    parameters.Add(new NpgsqlParameter(paramName, keepId));
                }
                sql.Append($" AND {Q("id")} NOT IN ({string.Join(", ", inParams)})");
            }

            if (childEntity.TenantScoped && tenantId.HasValue)
            {
                var tenantParam = $"@p{parameters.Count}";
                sql.Append($" AND {Q(SchemaConstants.TenantIdColumn)} = {tenantParam}");
                parameters.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            }

            return sql.ToString();
        }

        // Temporal InlineHistory: close records by setting system_end = now()
        if (childEntity.IsTemporal && childEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
        {
            var sql = new StringBuilder();
            sql.Append($"UPDATE {tableName} SET {Q("system_end")} = now()");
            BuildWhereClause(sql);
            sql.Append($" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ");
            return (sql.ToString(), parameters);
        }

        // Soft delete
        if (DynamicSqlBuilder.HasField(childEntity, "IsDeleted"))
        {
            var sql = new StringBuilder();
            sql.Append($"UPDATE {tableName} SET {Q("is_deleted")} = true");

            if (DynamicSqlBuilder.HasField(childEntity, "DeletedAt"))
            {
                var paramName = $"@p{parameters.Count}";
                sql.Append($", {Q("deleted_at")} = {paramName}");
                parameters.Add(new NpgsqlParameter(paramName, DateTime.UtcNow));
            }

            BuildWhereClause(sql);
            return (sql.ToString(), parameters);
        }

        // Hard delete
        {
            var sql = new StringBuilder();
            sql.Append($"DELETE FROM {tableName}");
            BuildWhereClause(sql);
            return (sql.ToString(), parameters);
        }
    }

    // ── DEFAULT VALUE EVALUATION ────────────────────────────────────

    internal static object? EvaluateDefaultValue(BmField field, Guid? userId = null, Guid? tenantId = null)
    {
        // If there's a parsed AST expression, evaluate it via RuntimeExpressionEvaluator
        if (field.DefaultExpr != null)
        {
            var evaluator = new Expressions.RuntimeExpressionEvaluator();
            return evaluator.Evaluate(field.DefaultExpr, new Dictionary<string, object?>());
        }

        var raw = field.DefaultValueString;
        if (string.IsNullOrEmpty(raw))
            return null;

        // Enum reference: #Active → "Active"
        if (raw.StartsWith('#'))
            return raw[1..];

        // Built-in variables (case-insensitive)
        var lower = raw.ToLowerInvariant();
        if (lower is "$now" or "now()")
            return DateTime.UtcNow;
        if (lower is "$today" or "today()")
            return DateTime.UtcNow.Date;
        if (lower is "$uuid" or "uuid()")
            return Guid.NewGuid();
        if (lower is "$user")
            return userId;
        if (lower is "$tenant")
            return tenantId;

        // Boolean literals
        if (lower is "true")
            return true;
        if (lower is "false")
            return false;

        // Numeric literals
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intVal))
            return intVal;
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var decVal))
            return decVal;

        // Quoted string: 'hello' → hello
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            return raw[1..^1];

        // Fallback: return raw string (backward compatible)
        return raw;
    }

    // ── Plugin integration helpers ──────────────────────────────────

    /// <summary>
    /// Run the INSERT contributor waterfall from the feature registry.
    /// Returns true if plugins were applied (and legacy path should be skipped).
    /// </summary>
    private bool ApplyFeatureInsertContributions(
        BmEntity entity,
        List<string> columns,
        List<string> values,
        List<NpgsqlParameter> parameters,
        Dictionary<string, object?> data,
        Guid? tenantId,
        Guid? userId)
    {
        if (_featureRegistry == null)
            return false;

        var contributors = _featureRegistry.GetInsertContributorsFor(entity).ToList();
        if (contributors.Count == 0)
            return false;

        var ctx = new InsertContext
        {
            Data = data,
            TenantId = tenantId,
            UserId = userId
        };

        foreach (var contributor in contributors)
        {
            var colSnapshot = ctx.Columns.Count;
            var valSnapshot = ctx.ValuePlaceholders.Count;
            var paramSnapshot = ctx.Parameters.Count;
            try
            {
                ctx = contributor.ContributeInsert(entity, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature '{FeatureName}' failed during insert contribution for entity '{EntityName}', skipping",
                    contributor.Name, entity.Name);
                // Rollback partial contributions to prevent column/value/param count mismatch
                if (ctx.Columns.Count > colSnapshot)
                    ctx.Columns.RemoveRange(colSnapshot, ctx.Columns.Count - colSnapshot);
                if (ctx.ValuePlaceholders.Count > valSnapshot)
                    ctx.ValuePlaceholders.RemoveRange(valSnapshot, ctx.ValuePlaceholders.Count - valSnapshot);
                if (ctx.Parameters.Count > paramSnapshot)
                    ctx.Parameters.RemoveRange(paramSnapshot, ctx.Parameters.Count - paramSnapshot);
            }
        }

        // Merge contributed columns/values/parameters into the builder's lists
        columns.AddRange(ctx.Columns);
        values.AddRange(ctx.ValuePlaceholders);
        parameters.AddRange(ctx.Parameters);

        return true;
    }

    /// <summary>
    /// Run the UPDATE contributor waterfall from the feature registry.
    /// Plugins may contribute SET clauses and WHERE clauses (prefixed with "WHERE:").
    /// Returns true if plugins were applied (and legacy path should be skipped).
    /// </summary>
    private bool ApplyFeatureUpdateContributions(
        BmEntity entity,
        List<string> setClauses,
        List<NpgsqlParameter> parameters,
        List<string> additionalWhereClauses,
        Dictionary<string, object?> data,
        Guid? tenantId,
        Guid? userId)
    {
        if (_featureRegistry == null)
            return false;

        var contributors = _featureRegistry.GetUpdateContributorsFor(entity).ToList();
        if (contributors.Count == 0)
            return false;

        var ctx = new UpdateContext
        {
            Data = data,
            TenantId = tenantId,
            UserId = userId
        };

        foreach (var contributor in contributors)
        {
            var setSnapshot = ctx.SetClauses.Count;
            var paramSnapshot = ctx.Parameters.Count;
            try
            {
                ctx = contributor.ContributeUpdate(entity, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature '{FeatureName}' failed during update contribution for entity '{EntityName}', skipping",
                    contributor.Name, entity.Name);
                // Rollback partial contributions to prevent SET clause/param count mismatch
                if (ctx.SetClauses.Count > setSnapshot)
                    ctx.SetClauses.RemoveRange(setSnapshot, ctx.SetClauses.Count - setSnapshot);
                if (ctx.Parameters.Count > paramSnapshot)
                    ctx.Parameters.RemoveRange(paramSnapshot, ctx.Parameters.Count - paramSnapshot);
            }
        }

        // Separate SET clauses from WHERE clauses (convention: "WHERE:" prefix)
        foreach (var clause in ctx.SetClauses)
        {
            if (clause.StartsWith("WHERE:", StringComparison.Ordinal))
            {
                additionalWhereClauses.Add(clause["WHERE:".Length..]);
            }
            else
            {
                setClauses.Add(clause);
            }
        }

        parameters.AddRange(ctx.Parameters);

        return true;
    }

    /// <summary>
    /// Run the DELETE strategy bail chain from the feature registry.
    /// First plugin returning non-null wins. Returns null if no plugin claimed the operation.
    /// </summary>
    private (string Sql, IReadOnlyList<NpgsqlParameter> Parameters)? ApplyFeatureDeleteStrategy(
        BmEntity entity,
        string tableName,
        Guid id,
        Guid? tenantId)
    {
        // Build the PK condition including tenant filter
        var pkParams = new List<NpgsqlParameter>();
        var pkCondition = $"{Q("id")} = @p_delete_id";
        pkParams.Add(new NpgsqlParameter("@p_delete_id", id));

        if (entity.TenantScoped && tenantId.HasValue)
        {
            pkCondition += $" AND {Q(SchemaConstants.TenantIdColumn)} = @p_delete_tenant";
            pkParams.Add(new NpgsqlParameter("@p_delete_tenant", tenantId.Value));
        }

        var ctx = new DeleteContext
        {
            TableName = tableName,
            PkCondition = pkCondition,
            TenantId = tenantId
        };

        foreach (var strategy in _featureRegistry!.GetDeleteStrategiesFor(entity))
        {
            try
            {
                var op = strategy.GetDeleteOperation(entity, ctx);
                if (op != null)
                {
                    // Bail: first non-null wins. Merge PK params with strategy params.
                    var allParams = new List<NpgsqlParameter>(pkParams);
                    allParams.AddRange(op.Parameters);
                    return (op.SqlTemplate, allParams);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature '{FeatureName}' failed during delete strategy for entity '{EntityName}', skipping",
                    strategy.Name, entity.Name);
            }
        }

        // No strategy claimed it
        return null;
    }

    /// <summary>
    /// Build a hard DELETE statement with tenant isolation WHERE clause.
    /// Shared by both the plugin path fallback and the legacy hard-delete path.
    /// </summary>
    private static (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildHardDeleteWithTenantFilter(
        string tableName,
        BmEntity entity,
        Guid id,
        Guid? tenantId)
    {
        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();
        sql.Append($"DELETE FROM {tableName}");

        var idParamName = $"@p{parameters.Count}";
        sql.Append($" WHERE {Q("id")} = {idParamName}");
        parameters.Add(new NpgsqlParameter(idParamName, id));

        if (entity.TenantScoped && tenantId.HasValue)
        {
            var tenantParamName = $"@p{parameters.Count}";
            sql.Append($" AND {Q(SchemaConstants.TenantIdColumn)} = {tenantParamName}");
            parameters.Add(new NpgsqlParameter(tenantParamName, tenantId.Value));
        }

        return (sql.ToString(), parameters);
    }

}
