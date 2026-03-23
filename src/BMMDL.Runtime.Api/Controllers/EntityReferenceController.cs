using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Api.Helpers;
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
/// Controller for OData $ref operations (relationship management).
/// Routes: POST/PUT/DELETE /api/odata/{module}/{entity}/{id}/{navProperty}/$ref
/// </summary>
[Tags("Entity References")]
public class EntityReferenceController : EntityControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public EntityReferenceController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IFieldRestrictionApplier fieldRestrictionApplier,
        IUnitOfWork unitOfWork,
        BmmdlMetrics metrics,
        ILogger<EntityReferenceController> logger,
        IPermissionChecker permissionChecker,
        IEntityResolver entityResolver)
        : base(cacheManager, sqlBuilder, queryExecutor, ruleEngine, eventPublisher, fieldRestrictionApplier, metrics, logger, permissionChecker, entityResolver)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Create a reference link between entities (POST $ref).
    /// OData v4: POST /api/odata/{module}/{entity}/{id}/{navProperty}/$ref
    /// Body: { "@odata.id": "/api/odata/{module}/{targetEntity}('{targetId}')" }
    /// </summary>
    [HttpPost("{id:guid}/{navProperty}/$ref")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRef(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string navProperty,
        [FromBody] Dictionary<string, object?> body,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check: creating references requires update permission
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update, body);
        if (permissionResult != null) return permissionResult;

        Logger.LogInformation("Creating $ref link: {Module}.{Entity}/{Id}/{NavProperty}",
            module, entity, id, navProperty);

        // Find the association for this navigation property
        var association = entityDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(navProperty, StringComparison.OrdinalIgnoreCase));

        if (association == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "NAVIGATION_PROPERTY_NOT_FOUND",
                $"Navigation property '{navProperty}' not found on entity '{entity}'",
                $"{module}.{entity}.{navProperty}"));
        }

        // Parse @odata.id from body (handle both string and JsonElement types)
        if (!body.TryGetValue("@odata.id", out var odataIdObj))
        {
            return BadRequest(ODataErrorResponse.FromException(
                "INVALID_REF_BODY",
                "Request body must contain '@odata.id' property with target entity reference",
                $"{module}.{entity}.{navProperty}"));
        }

        // Convert odataIdObj to string (may be string or JsonElement)
        string? odataId = odataIdObj switch
        {
            string s => s,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String => je.GetString(),
            _ => null
        };

        if (string.IsNullOrEmpty(odataId))
        {
            return BadRequest(ODataErrorResponse.FromException(
                "INVALID_ODATA_ID",
                "'@odata.id' must be a non-empty string",
                $"{module}.{entity}.{navProperty}"));
        }

        // Extract target entity ID from OData reference URL
        var targetId = ODataUrlParser.ExtractEntityIdFromODataReference(odataId);
        if (targetId == null)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "INVALID_ODATA_ID",
                $"Could not parse entity ID from '@odata.id': {odataId}",
                odataId));
        }

        // Resolve target entity definition before entering UoW scope to avoid
        // calling RollbackAsync inside the lambda (which corrupts UoW state).
        BmEntity? resolvedTargetEntityDef = null;
        if (association.Cardinality == BmCardinality.ManyToMany)
        {
            resolvedTargetEntityDef = (await GetCacheAsync()).GetEntity(association.TargetEntity);
            if (resolvedTargetEntityDef == null)
            {
                return NotFound(ODataErrorResponse.FromException(
                    "TARGET_ENTITY_NOT_FOUND",
                    $"Target entity '{association.TargetEntity}' not found",
                    association.TargetEntity));
            }
        }

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            // ManyToMany: insert into junction table
            if (association.Cardinality == BmCardinality.ManyToMany)
            {
                var tenantColumn = entityDef.TenantScoped || resolvedTargetEntityDef!.TenantScoped;
                var effectiveTenantId = tenantColumn ? GetTenantId() : null;

                var (sql, parameters) = SqlBuilder.BuildJunctionInsertQuery(
                    entityDef, resolvedTargetEntityDef!, id, targetId!.Value, effectiveTenantId);
                await QueryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);

                Logger.LogInformation("Created M:M $ref link: {Entity}/{Id}/{NavProperty} -> {TargetId}",
                    entity, id, navProperty, targetId);

                return NoContent();
            }

            // Verify source entity exists
            await VerifyEntityExistsAsync(entityDef, module, entity, id, ct);

            // Derive the foreign key field name from association name (convention: {navProperty}Id)
            var fkField = NamingConvention.GetFkCamelFieldName(navProperty);
            var updateData = new Dictionary<string, object?> { [fkField] = targetId.Value };

            var effectiveTenantId2 = GetEffectiveTenantId(entityDef);
            var (updateSql, updateParams) = SqlBuilder.BuildUpdateQuery(entityDef, id, updateData, effectiveTenantId2);
            await QueryExecutor.ExecuteNonQueryAsync(updateSql, updateParams, ct);

            Logger.LogInformation("Created $ref link: {Entity}/{Id}/{NavProperty} -> {TargetId}",
                entity, id, navProperty, targetId);

            return NoContent();
        }, ct);
    }

    /// <summary>
    /// Update a reference link between entities (PUT $ref).
    /// OData v4: PUT /api/odata/{module}/{entity}/{id}/{navProperty}/$ref
    /// </summary>
    [HttpPut("{id:guid}/{navProperty}/$ref")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRef(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string navProperty,
        [FromBody] Dictionary<string, object?> body,
        CancellationToken ct = default)
    {
        // PUT $ref is semantically the same as POST $ref for single-valued navigation
        return await CreateRef(module, entity, id, navProperty, body, ct);
    }

    /// <summary>
    /// Delete a reference link between entities (DELETE $ref).
    /// OData v4: DELETE /api/odata/{module}/{entity}/{id}/{navProperty}/$ref
    /// For collection-valued: DELETE .../$ref?$id={targetEntityId}
    /// </summary>
    [HttpDelete("{id:guid}/{navProperty}/$ref")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRef(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string navProperty,
        [FromQuery(Name = "$id")] string? targetIdParam = null,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check: deleting references requires update permission
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update);
        if (permissionResult != null) return permissionResult;

        Logger.LogInformation("Deleting $ref link: {Module}.{Entity}/{Id}/{NavProperty}",
            module, entity, id, navProperty);

        // Find the association for this navigation property
        var association = entityDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(navProperty, StringComparison.OrdinalIgnoreCase));

        if (association == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "NAVIGATION_PROPERTY_NOT_FOUND",
                $"Navigation property '{navProperty}' not found on entity '{entity}'",
                $"{module}.{entity}.{navProperty}"));
        }

        // ManyToMany: delete from junction table
        if (association.Cardinality == BmCardinality.ManyToMany)
        {
            if (string.IsNullOrEmpty(targetIdParam))
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "MISSING_TARGET_ID",
                    "ManyToMany $ref DELETE requires '$id' query parameter with target entity reference",
                    $"{module}.{entity}.{navProperty}"));
            }

            var targetId = ODataUrlParser.ExtractEntityIdFromODataReference(targetIdParam);
            if (targetId == null && Guid.TryParse(targetIdParam, out var parsedGuid))
                targetId = parsedGuid;

            if (targetId == null)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "INVALID_TARGET_ID",
                    $"Could not parse target entity ID from '$id': {targetIdParam}",
                    targetIdParam));
            }

            var targetEntityDef = (await GetCacheAsync()).GetEntity(association.TargetEntity);
            if (targetEntityDef == null)
                return NotFound(ODataErrorResponse.FromException(
                    "TARGET_ENTITY_NOT_FOUND",
                    $"Target entity '{association.TargetEntity}' not found",
                    association.TargetEntity));

            await _unitOfWork.BeginAsync(ct);
            try
            {
                var tenantColumn = entityDef.TenantScoped || targetEntityDef.TenantScoped;
                var effectiveTenantIdMm = tenantColumn ? GetTenantId() : null;

                var (sql, parameters) = SqlBuilder.BuildJunctionDeleteQuery(
                    entityDef, targetEntityDef, id, targetId.Value, effectiveTenantIdMm);
                await QueryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);

                await _unitOfWork.CommitAsync(ct);

                Logger.LogInformation("Deleted M:M $ref link: {Entity}/{Id}/{NavProperty} -> {TargetId}",
                    entity, id, navProperty, targetId);

                return NoContent();
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        // Verify source entity exists
        await VerifyEntityExistsAsync(entityDef, module, entity, id, ct);

        await _unitOfWork.BeginAsync(ct);
        try
        {
            // Set FK to null to break the relationship
            var fkField = NamingConvention.GetFkCamelFieldName(navProperty);
            var updateData = new Dictionary<string, object?> { [fkField] = null };

            var effectiveTenantId = GetEffectiveTenantId(entityDef);
            var (updateSql, updateParams) = SqlBuilder.BuildUpdateQuery(entityDef, id, updateData, effectiveTenantId);
            await QueryExecutor.ExecuteNonQueryAsync(updateSql, updateParams, ct);

            await _unitOfWork.CommitAsync(ct);

            Logger.LogInformation("Deleted $ref link: {Entity}/{Id}/{NavProperty}",
                entity, id, navProperty);

            return NoContent();
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }
}
