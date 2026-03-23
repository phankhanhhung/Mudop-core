using BMMDL.MetaModel.Utilities;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Observability;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Rules;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Controller for OData navigation property operations (contained entities).
/// Routes: GET/POST /api/odata/{module}/{entity}/{id}/{navProperty}
/// </summary>
[Tags("Entity Navigation")]
public class EntityNavigationController : EntityControllerBase
{
    public EntityNavigationController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IFieldRestrictionApplier fieldRestrictionApplier,
        BmmdlMetrics metrics,
        ILogger<EntityNavigationController> logger,
        IPermissionChecker permissionChecker,
        IEntityResolver entityResolver)
        : base(cacheManager, sqlBuilder, queryExecutor, ruleEngine, eventPublisher, fieldRestrictionApplier, metrics, logger, permissionChecker, entityResolver)
    {
    }


    /// <summary>
    /// Get single contained entity via parent navigation.
    /// OData v4: GET /api/odata/{module}/{parent}/{id}/{navProperty}/{childId}
    /// </summary>
    [HttpGet("{id:guid}/{navProperty}/{childId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContainedEntity(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string navProperty,
        [FromRoute] Guid childId,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$expand")] string? expand = null,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var parentDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check on parent entity
        var permissionResult = await CheckPermissionAsync(parentDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        // Find the composition/association
        var composition = parentDef.Compositions.FirstOrDefault(c =>
            c.Name.Equals(navProperty, StringComparison.OrdinalIgnoreCase));
        
        var assoc = composition ?? parentDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(navProperty, StringComparison.OrdinalIgnoreCase));

        if (assoc == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "NAVIGATION_PROPERTY_NOT_FOUND",
                $"Navigation property '{navProperty}' not found on entity '{entity}'",
                $"{module}.{entity}.{navProperty}"));
        }

        // Get target entity definition
        var targetEntity = (await GetCacheAsync()).GetEntity(assoc.TargetEntity);
        if (targetEntity == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "TARGET_ENTITY_NOT_FOUND",
                $"Target entity '{assoc.TargetEntity}' not found",
                assoc.TargetEntity));
        }

        // Verify parent entity exists
        var effectiveTenantId = GetEffectiveTenantId(parentDef);
        var parentOptions = new QueryOptions { TenantId = effectiveTenantId };
        var (parentSql, parentParams) = SqlBuilder.BuildSelectQuery(parentDef, parentOptions, id);
        var parentEntity = await QueryExecutor.ExecuteSingleAsync(parentSql, parentParams, ct);

        if (parentEntity == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", id);
        }

        // C3: Verify parent entity's tenant matches request tenant to prevent cross-tenant access
        if (parentDef.TenantScoped && parentEntity.TryGetValue("TenantId", out var parentTenant))
        {
            var parentTenantId = parentTenant is Guid g2 ? g2 : (Guid.TryParse(parentTenant?.ToString(), out var parsed2) ? parsed2 : Guid.Empty);
            if (parentTenantId != tenantId) return NotFound();
        }

        // Get the child entity and verify it belongs to the parent
        var childOptions = new QueryOptions
        {
            TenantId = targetEntity.TenantScoped ? tenantId : null,
            Select = select,
            Expand = expand
        };

        var (childSql, childParams) = SqlBuilder.BuildSelectQuery(targetEntity, childOptions, childId);
        var childEntity = await QueryExecutor.ExecuteSingleAsync(childSql, childParams, ct);

        if (childEntity == null)
        {
            throw new EntityNotFoundException(assoc.TargetEntity, childId);
        }

        // Verify child belongs to parent (use PascalCase FK field name for dictionary lookup)
        var parentFkField = NamingConvention.GetFkFieldName(entity);
        if (childEntity.TryGetValue(parentFkField, out var fkValue))
        {
            var parentIdFromChild = fkValue switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => Guid.Empty
            };

            if (parentIdFromChild != id)
            {
                return NotFound(ODataErrorResponse.FromException(
                    "CHILD_NOT_BELONGS_TO_PARENT",
                    $"Entity {childId} does not belong to parent {id}",
                    $"{module}.{entity}.{navProperty}"));
            }
        }

        return Ok(childEntity);
    }

    // NOTE: CreateContainedEntity has been moved to EntityActionController.HandlePostToEntityMember
    // to resolve route ambiguity with bound actions (both use POST {id:guid}/{name})
}
