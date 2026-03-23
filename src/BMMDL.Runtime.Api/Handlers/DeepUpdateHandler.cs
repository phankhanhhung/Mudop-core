namespace BMMDL.Runtime.Api.Handlers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Extensions;
using BMMDL.Runtime.Rules;

/// <summary>
/// Handles OData v4 Deep Update (nested entity modification in PATCH request).
/// Detects nested objects and performs UPDATE/CREATE/DELETE operations accordingly.
/// </summary>
/// <example>
/// PATCH /api/odata/Platform/Order('123')
/// {
///   "status": "confirmed",
///   "customer": { "id": "456", "name": "Updated Name" },  // N:1 - update existing
///   "items": [                                             // 1:N collection
///     { "id": "i1", "quantity": 20 },                      // UPDATE existing
///     { "product": "NewWidget", "qty": 5 }                 // CREATE new (no id)
///   ]
/// }
/// </example>
public class DeepUpdateHandler : DeepOperationBase
{
    public DeepUpdateHandler(
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<DeepUpdateHandler> logger,
        ReferentialIntegrityService? refIntegrity = null,
        IRuleEngine? ruleEngine = null)
        : base(cache, sqlBuilder, queryExecutor, logger, refIntegrity, ruleEngine)
    {
    }

    /// <summary>
    /// Result of deep update operation containing all modified entities.
    /// </summary>
    public record DeepUpdateResult
    {
        public required Dictionary<string, object?> RootEntity { get; init; }
        public Dictionary<string, object?> NestedResults { get; init; } = new();
        public bool HasNestedUpdates { get; init; }
        public int UpdatedCount { get; init; }
        public int CreatedCount { get; init; }
        public int DeletedCount { get; init; }
    }

    /// <summary>
    /// Execute deep update - updates root entity and all nested entities in transaction.
    /// </summary>
    /// <param name="entityDef">Root entity definition.</param>
    /// <param name="rootId">ID of the root entity being updated.</param>
    /// <param name="data">PATCH body with potential nested objects.</param>
    /// <param name="currentRecord">Current state of root entity (for merging).</param>
    /// <param name="tenantId">Tenant ID for scoped entities.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DeepUpdateResult> ExecuteAsync(
        BmEntity entityDef,
        Guid rootId,
        Dictionary<string, object?> data,
        Dictionary<string, object?> currentRecord,
        Guid? tenantId,
        CancellationToken ct = default,
        EvaluationContext? evalContext = null)
    {
        var nestedUpdates = ExtractNestedObjects(entityDef, data);

        if (nestedUpdates.Count == 0)
        {
            return new DeepUpdateResult
            {
                RootEntity = currentRecord,
                HasNestedUpdates = false
            };
        }

        _logger.LogInformation("Deep update detected for {Entity} ID {Id} with {Count} nested properties",
            entityDef.Name, rootId, nestedUpdates.Count);

        // Clone data without nested objects for root update
        var rootData = new Dictionary<string, object?>(data);
        foreach (var nested in nestedUpdates)
        {
            rootData.Remove(nested.NavigationName);
        }

        var nestedResults = new Dictionary<string, object?>();
        int updatedCount = 0;
        int createdCount = 0;
        int deletedCount = 0;

        // Step 1: Process N:1 (ManyToOne) nested entities - update them first
        foreach (var nested in nestedUpdates.Where(n => n.Cardinality == BmCardinality.ManyToOne))
        {
            var (result, wasCreated) = await ProcessNestedSingleAsync(nested, tenantId, ct, evalContext);
            nestedResults[nested.NavigationName] = result;

            if (wasCreated) createdCount++;
            else if (result != null) updatedCount++;

            // If nested entity changed, update FK on root
            var nestedId = result?.GetIdValue();
            if (nestedId != null)
            {
                var fkFieldName = GetForeignKeyFieldName(entityDef, nested.NavigationName);
                if (fkFieldName != null)
                {
                    rootData[fkFieldName] = nestedId;
                }
            }
        }

        // Step 2: Update root entity (if there are root-level changes)
        Dictionary<string, object?>? updatedRoot = currentRecord;
        if (rootData.Count > 0)
        {
            var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
            var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, rootId, rootData, effectiveTenantId);
            updatedRoot = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
            updatedCount++;
        }

        if (updatedRoot == null)
        {
            throw new InvalidOperationException($"Root entity update did not return data for {entityDef.Name} ID {rootId}");
        }

        // Step 3: Process 1:N (OneToMany) nested collections - update/create/delete children
        foreach (var nested in nestedUpdates.Where(n => n.Cardinality == BmCardinality.OneToMany))
        {
            var (results, created, updated, deleted) = await ProcessNestedCollectionAsync(
                nested, entityDef.Name, rootId, tenantId, ct, evalContext);
            nestedResults[nested.NavigationName] = results;
            createdCount += created;
            updatedCount += updated;
            deletedCount += deleted;
        }

        // Merge nested results into root response
        foreach (var kvp in nestedResults)
        {
            updatedRoot[kvp.Key] = kvp.Value;
        }

        return new DeepUpdateResult
        {
            RootEntity = updatedRoot,
            NestedResults = nestedResults,
            HasNestedUpdates = true,
            UpdatedCount = updatedCount,
            CreatedCount = createdCount,
            DeletedCount = deletedCount
        };
    }

    /// <summary>
    /// Process a single nested entity (N:1 association).
    /// If ID present: UPDATE. If no ID: CREATE new.
    /// </summary>
    private async Task<(Dictionary<string, object?>? result, bool wasCreated)> ProcessNestedSingleAsync(
        NestedOperation nested,
        Guid? tenantId,
        CancellationToken ct,
        EvaluationContext? evalContext = null)
    {
        if (nested.Data is not Dictionary<string, object?> entityData)
            return (null, false);

        var nestedEntityDef = _cache.GetEntity(nested.TargetEntityName);
        if (nestedEntityDef == null)
        {
            _logger.LogWarning("Nested entity {Entity} not found in cache", nested.TargetEntityName);
            return (null, false);
        }

        var effectiveTenantId = nestedEntityDef.TenantScoped ? tenantId : null;

        // Check if this is an update (has ID) or create (no ID)
        var nestedId = entityData.GetValueOrDefault("id") ?? entityData.GetValueOrDefault("Id");

        if (nestedId != null)
        {
            // UPDATE existing nested entity
            var idGuid = nestedId is Guid g ? g : Guid.Parse(nestedId.ToString()!);
            var updateData = new Dictionary<string, object?>(entityData);
            updateData.Remove("id");
            updateData.Remove("Id");

            if (updateData.Count > 0)
            {
                // Fetch current child data for rule evaluation
                var getOptions = new QueryOptions { TenantId = effectiveTenantId };
                var (getSql, getParams) = _sqlBuilder.BuildSelectQuery(nestedEntityDef, getOptions, idGuid);
                var currentChild = await _queryExecutor.ExecuteSingleAsync(getSql, getParams, ct);

                await ApplyBeforeUpdateRulesAsync(nestedEntityDef, currentChild ?? new(), updateData, evalContext,
                    $"Deep update child validation failed for {nested.TargetEntityName} ID {idGuid}");

                var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(nestedEntityDef, idGuid, updateData, effectiveTenantId);
                var updated = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
                _logger.LogDebug("Updated nested N:1 entity {Entity} ID {Id}", nested.TargetEntityName, idGuid);

                // Execute "after update" rules for child entity
                if (updated != null)
                {
                    await InvokeAfterUpdateRulesAsync(nestedEntityDef, currentChild ?? new(), updated, evalContext);
                }

                return (updated, false);
            }

            // ID present but no changes - just return current data
            return (entityData, false);
        }
        else
        {
            // CREATE new nested entity
            await ApplyBeforeCreateRulesAsync(nestedEntityDef, entityData, evalContext,
                $"Deep update child create validation failed for {nested.TargetEntityName}");

            var (sql, parameters) = _sqlBuilder.BuildInsertQuery(nestedEntityDef, entityData, effectiveTenantId);
            var created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
            _logger.LogDebug("Created new nested N:1 entity {Entity}", nested.TargetEntityName);

            if (created != null)
            {
                await InvokeAfterCreateRulesAsync(nestedEntityDef, created, evalContext);
            }

            return (created, true);
        }
    }

    /// <summary>
    /// Process a nested collection (1:N association).
    /// For each item: If has ID -> UPDATE, else -> CREATE. Orphans are deleted.
    /// </summary>
    private async Task<(List<Dictionary<string, object?>> results, int created, int updated, int deleted)> ProcessNestedCollectionAsync(
        NestedOperation nested,
        string parentEntityName,
        object? parentId,
        Guid? tenantId,
        CancellationToken ct,
        EvaluationContext? evalContext = null)
    {
        var results = new List<Dictionary<string, object?>>();
        int createdCount = 0;
        int updatedCount = 0;

        if (nested.Data is not IEnumerable<object> collection)
            return (results, 0, 0, 0);

        var nestedEntityDef = _cache.GetEntity(nested.TargetEntityName);
        if (nestedEntityDef == null)
        {
            _logger.LogWarning("Nested entity {Entity} not found in cache", nested.TargetEntityName);
            return (results, 0, 0, 0);
        }

        var effectiveTenantId = nestedEntityDef.TenantScoped ? tenantId : null;
        var fkFieldName = NamingConvention.GetFkFieldName(parentEntityName);
        var keepIds = new List<Guid>();

        foreach (var item in collection)
        {
            var itemData = ParseItemData(item);
            if (itemData == null)
                continue;

            var itemId = itemData.GetValueOrDefault("id") ?? itemData.GetValueOrDefault("Id");

            if (itemId != null)
            {
                var idGuid = itemId is Guid g ? g : Guid.Parse(itemId.ToString()!);
                keepIds.Add(idGuid);

                var (updated, wasUpdated) = await UpdateExistingChildAsync(
                    nestedEntityDef, itemData, itemId, effectiveTenantId, nested.TargetEntityName, evalContext, ct);
                if (updated != null) results.Add(updated);
                if (wasUpdated) updatedCount++;
            }
            else
            {
                var created = await CreateNewChildAsync(
                    nestedEntityDef, itemData, fkFieldName, parentId, effectiveTenantId, nested.TargetEntityName, evalContext, ct);
                if (created != null)
                {
                    results.Add(created);
                    createdCount++;
                    TrackCreatedId(created, keepIds);
                }
            }
        }

        // Delete orphaned children
        var deletedCount = await DeleteOrphansAsync(
            nestedEntityDef, parentEntityName, fkFieldName, parentId, keepIds,
            effectiveTenantId, nested.TargetEntityName, evalContext, ct);

        _logger.LogDebug("Processed nested 1:N {Entity}: {Created} created, {Updated} updated, {Deleted} deleted",
            nested.TargetEntityName, createdCount, updatedCount, deletedCount);

        return (results, createdCount, updatedCount, deletedCount);
    }

    /// <summary>
    /// Update an existing child entity in a collection. Returns the updated data and whether update occurred.
    /// </summary>
    private async Task<(Dictionary<string, object?>? result, bool wasUpdated)> UpdateExistingChildAsync(
        BmEntity nestedEntityDef,
        Dictionary<string, object?> itemData,
        object itemId,
        Guid? effectiveTenantId,
        string targetEntityName,
        EvaluationContext? evalContext,
        CancellationToken ct)
    {
        var idGuid = itemId is Guid g ? g : Guid.Parse(itemId.ToString()!);

        var updateData = new Dictionary<string, object?>(itemData);
        updateData.Remove("id");
        updateData.Remove("Id");

        if (updateData.Count == 0)
        {
            // ID present but no changes
            return (itemData, false);
        }

        // Fetch current child data for rule evaluation
        var getOptions = new QueryOptions { TenantId = effectiveTenantId };
        var (getSql, getParams) = _sqlBuilder.BuildSelectQuery(nestedEntityDef, getOptions, idGuid);
        var currentChild = await _queryExecutor.ExecuteSingleAsync(getSql, getParams, ct);

        await ApplyBeforeUpdateRulesAsync(nestedEntityDef, currentChild ?? new(), updateData, evalContext,
            $"Deep update child validation failed for {targetEntityName} ID {idGuid}");

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(nestedEntityDef, idGuid, updateData, effectiveTenantId);
        var updated = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);

        if (updated != null)
        {
            await InvokeAfterUpdateRulesAsync(nestedEntityDef, currentChild ?? new(), updated, evalContext);
        }

        return (updated, updated != null);
    }

    /// <summary>
    /// Create a new child entity in a collection.
    /// </summary>
    private async Task<Dictionary<string, object?>?> CreateNewChildAsync(
        BmEntity nestedEntityDef,
        Dictionary<string, object?> itemData,
        string? fkFieldName,
        object? parentId,
        Guid? effectiveTenantId,
        string targetEntityName,
        EvaluationContext? evalContext,
        CancellationToken ct)
    {
        // Set parent FK
        if (fkFieldName != null && parentId != null)
        {
            itemData[fkFieldName] = parentId;
        }

        // Execute "before create" rules for child entity
        await ApplyBeforeCreateRulesAsync(nestedEntityDef, itemData, evalContext,
            $"Deep update child create validation failed for {targetEntityName}");

        var (sql, parameters) = _sqlBuilder.BuildInsertQuery(nestedEntityDef, itemData, effectiveTenantId);
        var created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);

        if (created != null)
        {
            await InvokeAfterCreateRulesAsync(nestedEntityDef, created, evalContext);
        }

        return created;
    }

    /// <summary>
    /// Delete orphaned children not present in the update payload.
    /// </summary>
    private async Task<int> DeleteOrphansAsync(
        BmEntity nestedEntityDef,
        string parentEntityName,
        string? fkFieldName,
        object? parentId,
        List<Guid> keepIds,
        Guid? effectiveTenantId,
        string targetEntityName,
        EvaluationContext? evalContext,
        CancellationToken ct)
    {
        if (parentId == null)
            return 0;

        try
        {
            // Execute "before delete" rules for each orphan
            var fkCol = NamingConvention.QuoteIdentifier(NamingConvention.GetFkColumnName(parentEntityName));
            var findOrphansSql = $"SELECT * FROM {_sqlBuilder.GetTableName(nestedEntityDef)} WHERE {fkCol} = @p0";
            var findOrphansParams = new List<Npgsql.NpgsqlParameter> { new("@p0", parentId) };

            // Tenant isolation: filter orphan discovery to current tenant
            if (nestedEntityDef.TenantScoped && effectiveTenantId.HasValue)
            {
                findOrphansSql += $" AND {NamingConvention.QuoteIdentifier("tenant_id")} = @pTenant";
                findOrphansParams.Add(new Npgsql.NpgsqlParameter("@pTenant", effectiveTenantId.Value));
            }

            if (keepIds.Count > 0)
            {
                var inList = string.Join(", ", keepIds.Select((_, i) => $"@k{i}"));
                findOrphansSql += $" AND {NamingConvention.QuoteIdentifier("id")} NOT IN ({inList})";
                for (var i = 0; i < keepIds.Count; i++)
                    findOrphansParams.Add(new Npgsql.NpgsqlParameter($"@k{i}", keepIds[i]));
            }
            var orphanRows = await _queryExecutor.ExecuteListAsync(findOrphansSql, findOrphansParams, ct);

            foreach (var orphanRow in orphanRows)
            {
                await ApplyBeforeDeleteRulesAsync(nestedEntityDef, orphanRow, evalContext,
                    $"Deep update child delete validation failed for {targetEntityName}");

                if (orphanRow.GetIdValue() is Guid orphanId)
                {
                    if (_refIntegrity != null)
                        await _refIntegrity.CascadeDeleteAsync(nestedEntityDef, orphanId, effectiveTenantId, false, ct);
                }
            }

            var (deleteSql, deleteParams) = _sqlBuilder.BuildDeleteOrphansQuery(
                nestedEntityDef, fkFieldName, parentId, keepIds, effectiveTenantId);
            var deletedCount = await _queryExecutor.ExecuteNonQueryAsync(deleteSql, deleteParams, ct);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {Count} orphaned {Entity} records for parent {ParentId}",
                    deletedCount, targetEntityName, parentId);
            }

            return deletedCount;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to delete orphaned {Entity} records for parent {ParentId}",
                targetEntityName, parentId);
            return 0;
        }
    }

    /// <summary>
    /// Track a newly created entity's ID in the keepIds list for orphan deletion.
    /// </summary>
    private static void TrackCreatedId(Dictionary<string, object?> created, List<Guid> keepIds)
    {
        var newId = created.GetValueOrDefault("id") ?? created.GetValueOrDefault("Id");
        if (newId is Guid newGuid)
            keepIds.Add(newGuid);
        else if (newId != null && Guid.TryParse(newId.ToString(), out var parsedGuid))
            keepIds.Add(parsedGuid);
    }
}
