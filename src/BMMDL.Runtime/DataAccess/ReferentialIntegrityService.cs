using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using Microsoft.Extensions.Logging;
using BMMDL.Runtime.Extensions;
using Npgsql;

namespace BMMDL.Runtime.DataAccess;

/// <summary>
/// Application-level referential integrity enforcement.
/// Handles FK validation, pre-delete constraint checks, and cascade delete operations
/// that replace database-level FOREIGN KEY constraints.
/// </summary>
public class ReferentialIntegrityService
{
    private readonly IMetaModelCache _cache;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _executor;
    private readonly ILogger<ReferentialIntegrityService> _logger;

    public ReferentialIntegrityService(
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor executor,
        ILogger<ReferentialIntegrityService> logger)
    {
        _cache = cache;
        _sqlBuilder = sqlBuilder;
        _executor = executor;
        _logger = logger;
    }

    /// <summary>Shorthand for quoting a SQL identifier.</summary>
    private static string Q(string identifier) => NamingConvention.QuoteIdentifier(identifier);

    /// <summary>
    /// Quote a schema-qualified table name returned by GetTableName (e.g., "scm.sales_order" → "\"scm\".\"sales_order\"").
    /// </summary>
    private static string QTable(string qualifiedName)
    {
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedName[..dotIndex];
            var table = qualifiedName[(dotIndex + 1)..];
            return $"{Q(schema)}.{Q(table)}";
        }
        return Q(qualifiedName);
    }

    /// <summary>
    /// Validate that all FK values in the data point to existing records.
    /// Called on INSERT and UPDATE to replace DB-level FK constraint checks.
    /// </summary>
    public async Task<List<string>> ValidateForeignKeysAsync(
        BmEntity entityDef,
        Dictionary<string, object?> data,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var errors = new List<string>();

        foreach (var assoc in entityDef.Associations)
        {
            // Only M:1 and 1:1 have FK columns on this entity
            if (assoc.Cardinality != BmCardinality.ManyToOne &&
                assoc.Cardinality != BmCardinality.OneToOne)
                continue;

            var fkColumnName = NamingConvention.GetFkColumnName(assoc.Name);
            // OData sends camelCase (e.g. "warehouseId"), DB column is snake_case ("warehouse_id")
            var fkCamelName = NamingConvention.GetFkFieldName(assoc.Name);

            // Check if FK value is present in the data (try both naming conventions)
            object? fkValue = null;
            if (data.TryGetValue(fkColumnName, out var v1) && v1 != null)
                fkValue = v1;
            else if (data.TryGetValue(fkCamelName, out var v2) && v2 != null)
                fkValue = v2;

            if (fkValue == null)
                continue;

            // Resolve target entity
            var targetEntity = _cache.GetEntity(assoc.TargetEntity);
            if (targetEntity == null)
            {
                _logger.LogWarning("Target entity {Target} not found for association {Assoc}",
                    assoc.TargetEntity, assoc.Name);
                continue;
            }

            // Resolve FK value to a usable type (may be JsonElement from deserialization)
            var resolvedFkValue = fkValue;
            if (fkValue is System.Text.Json.JsonElement je)
            {
                resolvedFkValue = je.ValueKind == System.Text.Json.JsonValueKind.String
                    && Guid.TryParse(je.GetString(), out var g) ? g : je.ToString();
            }
            else if (fkValue is string s && Guid.TryParse(s, out var sg))
            {
                resolvedFkValue = sg;
            }

            // Check if referenced record exists
            // GetTableName already returns a quoted, schema-qualified name
            var targetTable = _sqlBuilder.GetTableName(targetEntity);
            var parameters = new List<NpgsqlParameter>();
            var sql = $"SELECT 1 FROM {targetTable} WHERE {Q("id")} = @p0";
            parameters.Add(new NpgsqlParameter("@p0", resolvedFkValue));

            if (targetEntity.TenantScoped && tenantId.HasValue)
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            // For temporal entities, only check current versions
            if (targetEntity.IsTemporal && targetEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                sql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            sql += " LIMIT 1";

            var exists = await _executor.ExecuteScalarAsync<int?>(sql, parameters, ct);
            if (exists == null)
            {
                errors.Add($"Association '{assoc.Name}': referenced {assoc.TargetEntity} with id '{resolvedFkValue}' does not exist");
            }
        }

        return errors;
    }

    /// <summary>
    /// Check if an entity can be safely deleted by verifying no RESTRICT associations reference it.
    /// Composition references are NOT errors — they'll be cascade-deleted.
    /// </summary>
    public async Task<List<string>> CheckDeleteConstraintsAsync(
        BmEntity entityDef,
        Guid id,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var errors = new List<string>();

        // Check incoming associations (other entities that reference this one)
        var incomingRefs = _cache.GetIncomingReferences(entityDef.Name);
        foreach (var incoming in incomingRefs)
        {
            // Determine effective delete action:
            // - Explicit OnDelete takes priority
            // - Legacy default: compositions=Cascade, associations=Restrict
            var effectiveAction = incoming.OnDelete
                ?? (incoming.IsComposition ? DeleteAction.Cascade : DeleteAction.Restrict);

            // Cascade and SetNull are handled in CascadeDeleteAsync, not errors
            if (effectiveAction == DeleteAction.Cascade || effectiveAction == DeleteAction.SetNull)
                continue;

            // NoAction means skip the check entirely (defer to DB)
            if (effectiveAction == DeleteAction.NoAction)
                continue;

            // M:M junction references — also cascade-cleaned, not an error
            if (incoming.Cardinality == BmCardinality.ManyToMany)
                continue;

            // Restrict: check if referencing rows exist
            var sourceTable = _sqlBuilder.GetTableName(incoming.SourceEntity);
            var parameters = new List<NpgsqlParameter>();
            var sql = $"SELECT COUNT(*) FROM {sourceTable} WHERE {Q(incoming.FkColumnName)} = @p0";
            parameters.Add(new NpgsqlParameter("@p0", id));

            if (incoming.SourceEntity.TenantScoped && tenantId.HasValue)
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            // For temporal entities, only count current versions
            if (incoming.SourceEntity.IsTemporal &&
                incoming.SourceEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                sql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            var count = await _executor.ExecuteScalarAsync<long?>(sql, parameters, ct);
            if (count.HasValue && count.Value > 0)
            {
                errors.Add($"Cannot delete {entityDef.Name}: referenced by {count.Value} {incoming.SourceEntity.Name} record(s) via '{incoming.NavigationName}'");
            }
        }

        // Check own compositions with OnDelete=Restrict (children that must not exist)
        foreach (var comp in entityDef.Compositions)
        {
            if (comp.OnDelete != DeleteAction.Restrict)
                continue;

            var childEntity = _cache.GetEntity(comp.TargetEntity);
            if (childEntity == null) continue;

            var childTable = _sqlBuilder.GetTableName(childEntity);
            var fkColumn = NamingConvention.GetFkColumnName(entityDef.Name);
            var parameters = new List<NpgsqlParameter>();
            var sql = $"SELECT COUNT(*) FROM {childTable} WHERE {Q(fkColumn)} = @p0";
            parameters.Add(new NpgsqlParameter("@p0", id));

            if (childEntity.TenantScoped && tenantId.HasValue)
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            if (childEntity.IsTemporal &&
                childEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                sql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            var count = await _executor.ExecuteScalarAsync<long?>(sql, parameters, ct);
            if (count.HasValue && count.Value > 0)
            {
                errors.Add($"Cannot delete {entityDef.Name}: referenced by {count.Value} {childEntity.Name} record(s) via '{comp.Name}'");
            }
        }

        return errors;
    }

    /// <summary>
    /// Cascade delete all dependent records: compositions (depth-first), junction rows, and localized texts.
    /// Must be called BEFORE deleting the entity itself.
    /// </summary>
    public async Task CascadeDeleteAsync(
        BmEntity entityDef,
        Guid id,
        Guid? tenantId,
        bool soft,
        CancellationToken ct = default)
    {
        // 1. Cascade-delete composition children (depth-first recursive)
        await CascadeDeleteChildrenAsync(entityDef, id, tenantId, soft, ct);

        // 1a. Cascade-delete non-composition associations with OnDelete=Cascade
        var incomingRefs = _cache.GetIncomingReferences(entityDef.Name);
        foreach (var incoming in incomingRefs)
        {
            if (incoming.IsComposition)
                continue; // Already handled above
            if (incoming.OnDelete != DeleteAction.Cascade)
                continue;
            if (incoming.Cardinality == BmCardinality.ManyToMany)
                continue;

            var sourceTable = _sqlBuilder.GetTableName(incoming.SourceEntity);
            var parameters = new List<NpgsqlParameter>();
            var sql = $"DELETE FROM {sourceTable} WHERE {Q(incoming.FkColumnName)} = @p0";
            parameters.Add(new NpgsqlParameter("@p0", id));

            if (incoming.SourceEntity.TenantScoped && tenantId.HasValue)
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            if (incoming.SourceEntity.IsTemporal &&
                incoming.SourceEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                sql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            var deleted = await _executor.ExecuteNonQueryAsync(sql, parameters, ct);
            _logger.LogDebug("Cascade deleted {Count} {Source} records for deleted {Entity} {Id} via '{Nav}'",
                deleted, incoming.SourceEntity.Name, entityDef.Name, id, incoming.NavigationName);
        }

        // 1b. Handle SET NULL for associations/compositions that specify OnDelete=SetNull
        await SetNullReferencesAsync(entityDef, id, tenantId, ct);

        // 2. Clean up junction table rows (M:M associations)
        await CleanupJunctionRowsAsync(entityDef, id, tenantId, ct);

        // 3. Clean up localized texts
        await CleanupLocalizedTextsAsync(entityDef, id, tenantId, ct);
    }

    private async Task CascadeDeleteChildrenAsync(
        BmEntity parentEntity,
        Guid parentId,
        Guid? tenantId,
        bool soft,
        CancellationToken ct)
    {
        foreach (var comp in parentEntity.Compositions)
        {
            // Only cascade if the effective action is Cascade (default for compositions when OnDelete is null)
            var effectiveAction = comp.OnDelete ?? DeleteAction.Cascade;
            if (effectiveAction != DeleteAction.Cascade)
                continue;

            var childEntity = _cache.GetEntity(comp.TargetEntity);
            if (childEntity == null)
            {
                _logger.LogWarning("Composition target {Target} not found", comp.TargetEntity);
                continue;
            }

            var childTable = _sqlBuilder.GetTableName(childEntity);
            var fkColumn = NamingConvention.GetFkColumnName(parentEntity.Name);

            // Find all child IDs first (for recursive cascade)
            var findParams = new List<NpgsqlParameter>();
            var findSql = $"SELECT {Q("id")} FROM {childTable} WHERE {Q(fkColumn)} = @p0";
            findParams.Add(new NpgsqlParameter("@p0", parentId));

            if (childEntity.TenantScoped && tenantId.HasValue)
            {
                findSql += $" AND {Q("tenant_id")} = @p1";
                findParams.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            if (childEntity.IsTemporal && childEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                findSql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            var childRows = await _executor.ExecuteListAsync(findSql, findParams, ct);

            // Recursively cascade-delete each child (depth-first)
            foreach (var childRow in childRows)
            {
                if (childRow.GetIdValue() is Guid childId)
                {
                    await CascadeDeleteAsync(childEntity, childId, tenantId, soft, ct);
                }
            }

            // Now delete all children of this parent
            var deleteParams = new List<NpgsqlParameter>();
            string deleteSql;

            if (childEntity.IsTemporal && childEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                deleteSql = $"UPDATE {childTable} SET {Q("system_end")} = now() WHERE {Q(fkColumn)} = @p0 AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }
            else if (soft && childEntity.Fields.Any(f => f.Name.Equals("IsDeleted", StringComparison.OrdinalIgnoreCase)))
            {
                deleteSql = $"UPDATE {childTable} SET {Q("is_deleted")} = true WHERE {Q(fkColumn)} = @p0";
            }
            else
            {
                deleteSql = $"DELETE FROM {childTable} WHERE {Q(fkColumn)} = @p0";
            }
            deleteParams.Add(new NpgsqlParameter("@p0", parentId));

            if (childEntity.TenantScoped && tenantId.HasValue)
            {
                deleteSql += $" AND {Q("tenant_id")} = @p1";
                deleteParams.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            var deleted = await _executor.ExecuteNonQueryAsync(deleteSql, deleteParams, ct);
            _logger.LogDebug("Cascade deleted {Count} {Child} records for parent {Parent} {Id}",
                deleted, childEntity.Name, parentEntity.Name, parentId);
        }
    }

    /// <summary>
    /// Set FK columns to NULL for incoming references that have OnDelete=SetNull.
    /// </summary>
    private async Task SetNullReferencesAsync(
        BmEntity entityDef,
        Guid id,
        Guid? tenantId,
        CancellationToken ct)
    {
        var incomingRefs = _cache.GetIncomingReferences(entityDef.Name);
        foreach (var incoming in incomingRefs)
        {
            if (incoming.OnDelete != DeleteAction.SetNull)
                continue;

            // M:M junctions are handled separately
            if (incoming.Cardinality == BmCardinality.ManyToMany)
                continue;

            var sourceTable = _sqlBuilder.GetTableName(incoming.SourceEntity);
            var quotedFk = Q(incoming.FkColumnName);
            var parameters = new List<NpgsqlParameter>();
            var sql = $"UPDATE {sourceTable} SET {quotedFk} = NULL WHERE {quotedFk} = @p0";
            parameters.Add(new NpgsqlParameter("@p0", id));

            if (incoming.SourceEntity.TenantScoped && tenantId.HasValue)
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            if (incoming.SourceEntity.IsTemporal &&
                incoming.SourceEntity.TemporalStrategy == TemporalStrategy.InlineHistory)
            {
                sql += $" AND {Q("system_end")} = 'infinity'::TIMESTAMPTZ";
            }

            var updated = await _executor.ExecuteNonQueryAsync(sql, parameters, ct);
            _logger.LogDebug("Set NULL on {Count} {Source} records for deleted {Entity} {Id} via '{Nav}'",
                updated, incoming.SourceEntity.Name, entityDef.Name, id, incoming.NavigationName);
        }
    }

    private async Task CleanupJunctionRowsAsync(
        BmEntity entityDef,
        Guid id,
        Guid? tenantId,
        CancellationToken ct)
    {
        // Clean junction rows for M:M associations declared on this entity
        foreach (var assoc in entityDef.Associations)
        {
            if (assoc.Cardinality != BmCardinality.ManyToMany)
                continue;

            var targetEntity = _cache.GetEntity(assoc.TargetEntity);
            if (targetEntity == null) continue;

            var junctionTable = GetJunctionTableName(entityDef, targetEntity);
            var thisFk = NamingConvention.GetFkColumnName(entityDef.Name);

            var sql = $"DELETE FROM {junctionTable} WHERE {Q(thisFk)} = @p0";
            var parameters = new List<NpgsqlParameter> { new("@p0", id) };

            // Add tenant isolation for junction tables (generated with tenant_id when either entity is tenant-scoped)
            if (tenantId.HasValue && (entityDef.TenantScoped || targetEntity.TenantScoped))
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            await _executor.ExecuteNonQueryAsync(sql, parameters, ct);
            _logger.LogDebug("Cleaned junction rows from {Junction} for {Entity} {Id}",
                junctionTable, entityDef.Name, id);
        }

        // Also clean junction rows where this entity is the TARGET of another entity's M:M
        var incomingRefs = _cache.GetIncomingReferences(entityDef.Name);
        foreach (var incoming in incomingRefs)
        {
            if (incoming.Cardinality != BmCardinality.ManyToMany)
                continue;

            var junctionTable = GetJunctionTableName(incoming.SourceEntity, entityDef);
            var targetFk = NamingConvention.GetFkColumnName(entityDef.Name);

            var sql = $"DELETE FROM {junctionTable} WHERE {Q(targetFk)} = @p0";
            var parameters = new List<NpgsqlParameter> { new("@p0", id) };

            // Add tenant isolation for junction tables (generated with tenant_id when either entity is tenant-scoped)
            if (tenantId.HasValue && (incoming.SourceEntity.TenantScoped || entityDef.TenantScoped))
            {
                sql += $" AND {Q("tenant_id")} = @p1";
                parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
            }

            await _executor.ExecuteNonQueryAsync(sql, parameters, ct);
        }
    }

    private async Task CleanupLocalizedTextsAsync(
        BmEntity entityDef,
        Guid id,
        Guid? tenantId,
        CancellationToken ct)
    {
        if (!entityDef.Fields.Any(f => f.TypeRef is BmLocalizedType))
            return;

        var tableName = _sqlBuilder.GetTableName(entityDef);
        // The texts table is the entity table name + "_texts" suffix
        // We need to split the qualified name to quote properly
        var dotIndex = tableName.IndexOf('.');
        string textsTable;
        if (dotIndex >= 0)
        {
            var schema = tableName[..dotIndex];
            var table = tableName[(dotIndex + 1)..];
            textsTable = $"{Q(schema)}.{Q(table + "_texts")}";
        }
        else
        {
            textsTable = Q(tableName + "_texts");
        }

        var sql = $"DELETE FROM {textsTable} WHERE {Q("id")} = @p0";
        var parameters = new List<NpgsqlParameter> { new("@p0", id) };

        if (entityDef.TenantScoped && tenantId.HasValue)
        {
            sql += $" AND {Q("tenant_id")} = @p1";
            parameters.Add(new NpgsqlParameter("@p1", tenantId.Value));
        }

        await _executor.ExecuteNonQueryAsync(sql, parameters, ct);
        _logger.LogDebug("Cleaned localized texts for {Entity} {Id}", entityDef.Name, id);
    }

    /// <summary>
    /// Get the quoted, schema-qualified junction table name for a M:M association.
    /// </summary>
    private string GetJunctionTableName(BmEntity entity1, BmEntity entity2)
    {
        // Canonical junction table name: alphabetical order
        var schema = !string.IsNullOrEmpty(entity1.Namespace)
            ? NamingConvention.GetSchemaName(entity1.Namespace)
            : "public";

        var names = new[] { entity1.Name.ToLowerInvariant(), entity2.Name.ToLowerInvariant() };
        Array.Sort(names);
        return $"{Q(schema)}.{Q(names[0] + "_" + names[1])}";
    }
}
