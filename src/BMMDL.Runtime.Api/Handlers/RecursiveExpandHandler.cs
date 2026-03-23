namespace BMMDL.Runtime.Api.Handlers;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Extensions;
using Npgsql;

/// <summary>
/// Handles recursive $expand with $levels at the application layer.
/// Recursively fetches nested navigation properties up to the specified depth.
/// </summary>
public class RecursiveExpandHandler
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<RecursiveExpandHandler> _logger;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    // Maximum allowed depth to prevent infinite loops
    private const int MaxDepth = 10;

    public RecursiveExpandHandler(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<RecursiveExpandHandler> logger)
    {
        _cacheManager = cacheManager;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Apply recursive expansion to results.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="results">Base query results.</param>
    /// <param name="expandOptions">Expand options with Levels.</param>
    /// <param name="tenantId">Tenant ID for scoped queries.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results with nested navigation properties expanded.</returns>
    public async Task<List<Dictionary<string, object?>>> ExpandRecursively(
        BmEntity entity,
        List<Dictionary<string, object?>> results,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct)
    {
        foreach (var (navName, options) in expandOptions)
        {
            var levels = options.Levels ?? 1;
            if (levels == 0) continue;
            
            // Resolve effective depth (-1 means max)
            var effectiveDepth = levels == -1 ? MaxDepth : Math.Min(levels, MaxDepth);
            
            // Find the association
            var assoc = FindAssociation(entity, navName);
            if (assoc == null)
            {
                _logger.LogWarning("Association '{NavName}' not found on entity '{Entity}'", 
                    navName, entity.Name);
                continue;
            }

            var cache = await GetCacheAsync();
            var targetEntity = cache.GetEntity(assoc.TargetEntity);
            if (targetEntity == null)
            {
                _logger.LogWarning("Target entity '{Target}' not found", assoc.TargetEntity);
                continue;
            }

            await ExpandLevel(
                entity, 
                results, 
                navName, 
                assoc, 
                targetEntity, 
                options, 
                effectiveDepth, 
                1,  // currentLevel
                tenantId, 
                ct);
        }

        return results;
    }

    private async Task ExpandLevel(
        BmEntity parentEntity,
        List<Dictionary<string, object?>> parentRecords,
        string navName,
        BmAssociation assoc,
        BmEntity targetEntity,
        ExpandOptions options,
        int maxLevels,
        int currentLevel,
        Guid? tenantId,
        CancellationToken ct)
    {
        if (currentLevel > maxLevels || parentRecords.Count == 0)
            return;

        _logger.LogDebug("Expanding {NavName} level {Level}/{Max}", navName, currentLevel, maxLevels);

        // Collect parent IDs for batch fetch
        var parentIds = parentRecords
            .Select(r => r.GetIdValue())
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (parentIds.Count == 0)
            return;

        // Fetch related records based on cardinality
        List<Dictionary<string, object?>> relatedRecords;

        if (assoc.Cardinality == BmCardinality.ManyToOne || assoc.Cardinality == BmCardinality.OneToOne)
        {
            // FK on parent - get FK values (PascalCase for dictionary lookup)
            var fkFieldName = NamingConvention.GetFkFieldName(navName);
            var fkIds = parentRecords
                .Select(r => r.TryGetValue(fkFieldName, out var fk) ? fk : null)
                .OfType<Guid>()
                .Distinct()
                .ToList();

            if (fkIds.Count == 0)
                return;

            relatedRecords = await FetchByIds(targetEntity, fkIds, tenantId, ct);

            // Assign related records to parents
            foreach (var parent in parentRecords)
            {
                if (parent.TryGetValue(fkFieldName, out var fkValue) && fkValue is Guid fkId)
                {
                    var related = relatedRecords.FirstOrDefault(r => 
                        r.GetIdValue() is Guid rid && rid == fkId);
                    parent[navName] = related;
                }
                else
                {
                    parent[navName] = null;
                }
            }
        }
        else
        {
            // OneToMany - FK on child (snake_case for SQL, PascalCase for dictionary)
            var parentFkColumn = NamingConvention.GetFkColumnName(parentEntity.Name);
            var parentFkField = NamingConvention.GetFkFieldName(parentEntity.Name);
            relatedRecords = await FetchByParentIds(targetEntity, parentFkColumn, parentIds, tenantId, ct);

            // Group by parent FK and assign as arrays
            var grouped = relatedRecords.GroupBy(r =>
            {
                r.TryGetValue(parentFkField, out var pfk);
                return pfk as Guid?;
            }).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var parent in parentRecords)
            {
                if (parent.GetIdValue() is Guid pid)
                {
                    parent[navName] = grouped.GetValueOrDefault(pid, new List<Dictionary<string, object?>>());
                }
                else
                {
                    parent[navName] = new List<Dictionary<string, object?>>();
                }
            }
        }

        // Recurse for next level if same nav exists on target (self-referential)
        if (currentLevel < maxLevels)
        {
            var selfAssoc = FindAssociation(targetEntity, navName);
            if (selfAssoc != null && IsSelfReferential(selfAssoc, targetEntity))
            {
                // Self-referential: continue recursion
                var nestedRecords = parentRecords
                    .SelectMany(p =>
                    {
                        if (p.TryGetValue(navName, out var nav))
                        {
                            return nav switch
                            {
                                Dictionary<string, object?> single => new[] { single },
                                List<Dictionary<string, object?>> list => list,
                                _ => Array.Empty<Dictionary<string, object?>>()
                            };
                        }
                        return Array.Empty<Dictionary<string, object?>>();
                    })
                    .Where(r => r != null)
                    .ToList();

                if (nestedRecords.Count > 0)
                {
                    await ExpandLevel(
                        targetEntity,
                        nestedRecords,
                        navName,
                        selfAssoc,
                        targetEntity,
                        options,
                        maxLevels,
                        currentLevel + 1,
                        tenantId,
                        ct);
                }
            }
        }
    }

    private async Task<List<Dictionary<string, object?>>> FetchByIds(
        BmEntity entity,
        List<Guid> ids,
        Guid? tenantId,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new List<Dictionary<string, object?>>();

        var tableName = _sqlBuilder.GetTableName(entity);
        var sql = $"SELECT * FROM {tableName} WHERE {NamingConvention.QuoteIdentifier("id")} = ANY(@ids)";
        var parameters = new List<NpgsqlParameter>
        {
            new("@ids", ids.ToArray())
        };

        if (entity.TenantScoped && tenantId.HasValue)
        {
            sql += $" AND {NamingConvention.QuoteIdentifier("tenant_id")} = @tenantId";
            parameters.Add(new NpgsqlParameter("@tenantId", tenantId.Value));
        }

        return await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
    }

    private async Task<List<Dictionary<string, object?>>> FetchByParentIds(
        BmEntity entity,
        string parentFkColumn,
        List<Guid> parentIds,
        Guid? tenantId,
        CancellationToken ct)
    {
        if (parentIds.Count == 0)
            return new List<Dictionary<string, object?>>();

        var tableName = _sqlBuilder.GetTableName(entity);
        var sql = $"SELECT * FROM {tableName} WHERE {NamingConvention.QuoteIdentifier(parentFkColumn)} = ANY(@parentIds)";
        var parameters = new List<NpgsqlParameter>
        {
            new("@parentIds", parentIds.ToArray())
        };

        if (entity.TenantScoped && tenantId.HasValue)
        {
            sql += $" AND {NamingConvention.QuoteIdentifier("tenant_id")} = @tenantId";
            parameters.Add(new NpgsqlParameter("@tenantId", tenantId.Value));
        }

        return await _queryExecutor.ExecuteListAsync(sql, parameters, ct);
    }

    private static bool IsSelfReferential(BmAssociation assoc, BmEntity targetEntity)
    {
        var target = assoc.TargetEntity;
        // Check fully-qualified name
        if (target.Equals($"{targetEntity.Namespace}.{targetEntity.Name}", StringComparison.OrdinalIgnoreCase))
            return true;
        // Check simple name (no namespace prefix)
        if (target.Equals(targetEntity.Name, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private BmAssociation? FindAssociation(BmEntity entity, string navName)
    {
        var assoc = entity.Associations.FirstOrDefault(a =>
            a.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));

        if (assoc == null)
        {
            var comp = entity.Compositions.FirstOrDefault(c =>
                c.Name.Equals(navName, StringComparison.OrdinalIgnoreCase));
            if (comp != null)
                return comp;
        }

        return assoc;
    }
}
