namespace BMMDL.Runtime.Api.Handlers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Extensions;
using BMMDL.Runtime.Rules;

/// <summary>
/// Handles OData v4 Deep Insert (nested entity creation in single POST).
/// Detects nested objects in request body and creates parent + children in a transaction.
/// </summary>
/// <example>
/// POST /api/odata/Platform/Order
/// {
///   "orderNumber": "ORD-001",
///   "customer": { "name": "Acme" },          // N:1 - create new customer first
///   "items": [                                // 1:N - create children after parent
///     { "product": "Widget", "qty": 10 }
///   ]
/// }
/// </example>
public class DeepInsertHandler : DeepOperationBase
{
    public DeepInsertHandler(
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<DeepInsertHandler> logger,
        ReferentialIntegrityService? refIntegrity = null,
        IRuleEngine? ruleEngine = null)
        : base(cache, sqlBuilder, queryExecutor, logger, refIntegrity, ruleEngine)
    {
    }

    /// <summary>
    /// Result of deep insert operation containing all created entities.
    /// </summary>
    public record DeepInsertResult
    {
        public required Dictionary<string, object?> RootEntity { get; init; }
        public Dictionary<string, object?> NestedEntities { get; init; } = new();
        public bool HasNestedInserts { get; init; }
    }

    /// <summary>
    /// Execute deep insert - creates all entities in correct order within transaction.
    /// </summary>
    /// <param name="entityDef">Root entity definition.</param>
    /// <param name="data">Request body with potential nested objects.</param>
    /// <param name="tenantId">Tenant ID for scoped entities.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DeepInsertResult> ExecuteAsync(
        BmEntity entityDef,
        Dictionary<string, object?> data,
        Guid? tenantId,
        CancellationToken ct = default,
        EvaluationContext? evalContext = null)
    {
        var nestedInserts = ExtractNestedObjects(entityDef, data);

        if (nestedInserts.Count == 0)
        {
            // No nested objects - return early, caller will do normal insert
            return new DeepInsertResult
            {
                RootEntity = data,
                HasNestedInserts = false
            };
        }

        _logger.LogInformation("Deep insert detected for {Entity} with {Count} nested properties",
            entityDef.Name, nestedInserts.Count);

        // Clone data without nested objects for root insert
        var rootData = new Dictionary<string, object?>(data);
        foreach (var nested in nestedInserts)
        {
            rootData.Remove(nested.NavigationName);
        }

        var createdNested = new Dictionary<string, object?>();

        // Step 1: Insert N:1 (ManyToOne) nested entities FIRST - parent depends on them
        foreach (var nested in nestedInserts.Where(n => n.Cardinality == BmCardinality.ManyToOne))
        {
            var nestedEntity = await InsertNestedEntityAsync(nested, tenantId, ct, evalContext);

            // Set FK on root data
            var nestedId = nestedEntity?.GetIdValue();
            if (nestedEntity != null && nestedId != null)
            {
                var fkFieldName = GetForeignKeyFieldName(entityDef, nested.NavigationName);
                if (fkFieldName != null)
                {
                    rootData[fkFieldName] = nestedId;
                    _logger.LogDebug("Set FK {Field}={Value} from N:1 nested {Nav}",
                        fkFieldName, nestedId, nested.NavigationName);
                }
            }

            createdNested[nested.NavigationName] = nestedEntity;
        }

        // Step 2: Insert root entity
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var (rootSql, rootParams) = _sqlBuilder.BuildInsertQuery(entityDef, rootData, effectiveTenantId);
        var createdRoot = await _queryExecutor.ExecuteReturningAsync(rootSql, rootParams, ct);

        if (createdRoot == null)
        {
            throw new InvalidOperationException("Root entity insert did not return data");
        }

        var rootId = createdRoot.GetValueOrDefault("id") ?? createdRoot.GetValueOrDefault("Id");
        if (rootId == null)
            throw new InvalidOperationException($"Root entity insert for '{entityDef.Name}' returned no ID");

        // Step 3: Insert 1:N (OneToMany) nested entities AFTER - they depend on parent
        foreach (var nested in nestedInserts.Where(n => n.Cardinality == BmCardinality.OneToMany))
        {
            var nestedEntities = await InsertNestedCollectionAsync(nested, entityDef.Name, rootId, tenantId, ct, evalContext);
            createdNested[nested.NavigationName] = nestedEntities;
        }

        // Merge created nested into root response
        foreach (var kvp in createdNested)
        {
            createdRoot[kvp.Key] = kvp.Value;
        }

        return new DeepInsertResult
        {
            RootEntity = createdRoot,
            NestedEntities = createdNested,
            HasNestedInserts = true
        };
    }

    /// <summary>
    /// Insert a single nested entity (for N:1 associations).
    /// </summary>
    private async Task<Dictionary<string, object?>?> InsertNestedEntityAsync(
        NestedOperation nested,
        Guid? tenantId,
        CancellationToken ct,
        EvaluationContext? evalContext = null)
    {
        if (nested.Data is not Dictionary<string, object?> entityData)
            return null;

        var nestedEntityDef = _cache.GetEntity(nested.TargetEntityName);
        if (nestedEntityDef == null)
        {
            _logger.LogWarning("Nested entity {Entity} not found in cache", nested.TargetEntityName);
            return null;
        }

        var effectiveTenantId = nestedEntityDef.TenantScoped ? tenantId : null;

        // Execute "before create" rules for child entity
        await ApplyBeforeCreateRulesAsync(nestedEntityDef, entityData, evalContext,
            $"Deep insert child validation failed for {nested.TargetEntityName}");

        // Validate FK targets exist for nested entity
        if (_refIntegrity != null)
        {
            var fkErrors = await _refIntegrity.ValidateForeignKeysAsync(nestedEntityDef, entityData, effectiveTenantId, ct);
            if (fkErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Deep insert FK validation failed for {nested.TargetEntityName}: {string.Join("; ", fkErrors)}");
            }
        }

        var (sql, parameters) = _sqlBuilder.BuildInsertQuery(nestedEntityDef, entityData, effectiveTenantId);
        var created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);

        // Execute "after create" rules for child entity
        if (created != null)
        {
            await InvokeAfterCreateRulesAsync(nestedEntityDef, created, evalContext);
        }

        return created;
    }

    /// <summary>
    /// Insert multiple nested entities (for 1:N associations).
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> InsertNestedCollectionAsync(
        NestedOperation nested,
        string parentEntityName,
        object? parentId,
        Guid? tenantId,
        CancellationToken ct,
        EvaluationContext? evalContext = null)
    {
        var results = new List<Dictionary<string, object?>>();

        if (nested.Data is not IEnumerable<object> collection)
            return results;

        var nestedEntityDef = _cache.GetEntity(nested.TargetEntityName);
        if (nestedEntityDef == null)
        {
            _logger.LogWarning("Nested entity {Entity} not found in cache", nested.TargetEntityName);
            return results;
        }

        // FK field name derived from parent entity name (e.g. "Customer" -> "CustomerId" -> column "customer_id")
        var fkFieldName = NamingConvention.GetFkFieldName(parentEntityName);

        foreach (var item in collection)
        {
            var itemData = ParseItemData(item);
            if (itemData == null)
                continue;

            // Set FK to parent
            if (fkFieldName != null && parentId != null)
            {
                itemData[fkFieldName] = parentId;
            }

            // Execute "before create" rules for child entity
            await ApplyBeforeCreateRulesAsync(nestedEntityDef, itemData, evalContext,
                $"Deep insert child validation failed for {nested.TargetEntityName}");

            var effectiveTenantId = nestedEntityDef.TenantScoped ? tenantId : null;
            var (sql, parameters) = _sqlBuilder.BuildInsertQuery(nestedEntityDef, itemData, effectiveTenantId);
            var created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);

            if (created != null)
            {
                results.Add(created);

                // Execute "after create" rules for child entity
                await InvokeAfterCreateRulesAsync(nestedEntityDef, created, evalContext);
            }
        }

        return results;
    }
}
