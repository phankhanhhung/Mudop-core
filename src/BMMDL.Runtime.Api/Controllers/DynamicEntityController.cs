namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Helpers;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Observability;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Extensions;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

/// <summary>
/// Dynamic REST controller for CRUD operations on any entity.
/// Routes: /api/odata/{module}/{entity}
/// Requires JWT authentication.
/// Thin HTTP facade — delegates business logic to EntityWriteService, EntityQueryService, etc.
/// </summary>
[Tags("Entities")]
public class DynamicEntityController : EntityControllerBase
{
    private readonly IActionExecutor _actionExecutor;
    private readonly IDeltaTokenService _deltaTokenService;
    private readonly IUnitOfWork _unitOfWork;

    private readonly ExpandExpressionParser _expandParser;

    // Extracted services
    private readonly IEntityWriteService _writeService;
    private readonly IEntityQueryService _queryService;
    private readonly IEntityValidationService _validationService;
    private readonly IMediaStreamService _mediaService;
    private readonly IPropertyValueService _propertyService;

    public DynamicEntityController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IActionExecutor actionExecutor,
        IEventPublisher eventPublisher,
        IFieldRestrictionApplier fieldRestrictionApplier,
        IPermissionChecker permissionChecker,
        BmmdlMetrics metrics,
        ILogger<DynamicEntityController> logger,
        IDeltaTokenService deltaTokenService,
        IUnitOfWork unitOfWork,
        ExpandExpressionParser expandParser,
        IEntityWriteService writeService,
        IEntityQueryService queryService,
        IEntityValidationService validationService,
        IMediaStreamService mediaService,
        IPropertyValueService propertyService,
        IEntityResolver entityResolver)
        : base(cacheManager, sqlBuilder, queryExecutor, ruleEngine, eventPublisher,
               fieldRestrictionApplier, metrics, logger, permissionChecker, entityResolver)
    {
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _deltaTokenService = deltaTokenService ?? throw new ArgumentNullException(nameof(deltaTokenService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _expandParser = expandParser ?? throw new ArgumentNullException(nameof(expandParser));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _propertyService = propertyService ?? throw new ArgumentNullException(nameof(propertyService));
    }

    #region Query Operations

    /// <summary>
    /// List entities with optional filtering, sorting, and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ODataCollectionResponse<Dictionary<string, object?>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$orderby")] string? orderBy = null,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$expand")] string? expand = null,
        [FromQuery(Name = "$search")] string? search = null,
        [FromQuery(Name = "$top")] int top = QueryConstants.DefaultPageSize,
        [FromQuery(Name = "$skip")] int skip = 0,
        [FromQuery(Name = "$count")] bool count = false,
        [FromQuery(Name = "$apply")] string? apply = null,
        [FromQuery(Name = "$compute")] string? compute = null,
        [FromQuery] DateTimeOffset? asOf = null,
        [FromQuery] DateTime? validAt = null,
        [FromQuery] bool includeHistory = false,
        [FromQuery(Name = "$deltatoken")] string? deltaToken = null,
        [FromQuery] bool trackChanges = false,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top < 0 || top > QueryConstants.MaxPageSize)
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                $"$top must be between 0 and {QueryConstants.MaxPageSize}"));
        if (skip < 0)
            return BadRequest(ODataErrorResponse.FromException("INVALID_SKIP",
                "$skip must be non-negative"));

        var sw = Stopwatch.StartNew();
        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);
        var requestContext = BuildRequestContext();

        // Access control check
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        // Execute before-read rules
        {
            var beforeReadCtx = requestContext.ToEvaluationContext();
            var beforeReadResult = await RuleEngine.ExecuteBeforeReadAsync(entityDef, beforeReadCtx);
            if (!beforeReadResult.Success)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "ValidationFailed",
                    string.Join("; ", beforeReadResult.Errors.Select(e => e.Message))));
            }
        }

        // Singleton entity: return single record directly
        if (entityDef.HasAnnotation(ODataConstants.Annotations.Singleton))
            return await GetSingletonInternal(entityDef, module, entity, tenantId, select, expand, ct);

        // Respect Prefer odata.maxpagesize / odata.track-changes
        var (_, _, preferMaxPageSize, preferTrackChanges) = ODataResponseHelper.ParsePreferHeader(Request);
        if (preferMaxPageSize.HasValue)
        {
            top = Math.Min(top, preferMaxPageSize.Value);
            Response.Headers[ODataConstants.Headers.PreferenceApplied] = $"odata.maxpagesize={preferMaxPageSize.Value}";
        }

        if (preferTrackChanges)
        {
            trackChanges = true;
            var existing = Response.Headers[ODataConstants.Headers.PreferenceApplied].ToString();
            Response.Headers[ODataConstants.Headers.PreferenceApplied] = string.IsNullOrEmpty(existing)
                ? "odata.track-changes"
                : $"{existing}, odata.track-changes";
        }

        // Clamp pagination
        top = Math.Clamp(top, QueryConstants.MinPageSize, QueryConstants.MaxPageSize);
        skip = Math.Max(0, skip);

        // Parse $expand
        Dictionary<string, ExpandOptions>? expandOptions = null;
        if (!string.IsNullOrWhiteSpace(expand))
        {
            expandOptions = _expandParser.Parse(expand);
        }

        // Delta token filter injection
        DeltaTokenPayload? deltaPayload = null;
        if (!string.IsNullOrWhiteSpace(deltaToken))
        {
            deltaPayload = _deltaTokenService.ParseToken(deltaToken);
            if (deltaPayload != null)
            {
                var sinceUtc = deltaPayload.Timestamp.UtcDateTime.ToString("O");
                var deltaFilter = $"updatedAt ge {sinceUtc}";
                filter = string.IsNullOrWhiteSpace(filter) ? deltaFilter : $"({filter}) and {deltaFilter}";
                Logger.LogDebug("Delta token injected SQL filter: updatedAt >= {Since}", sinceUtc);
            }
            else
            {
                Logger.LogWarning("Invalid delta token provided, ignoring");
            }
        }

        var options = new QueryOptions
        {
            Filter = filter,
            OrderBy = orderBy,
            Select = select,
            Top = top,
            Skip = skip,
            TenantId = entityDef.TenantScoped ? tenantId : null,
            Expand = expand,
            ExpandOptions = expandOptions,
            Search = search,
            AsOf = asOf,
            ValidAt = validAt,
            IncludeHistory = includeHistory,
            Locale = GetRequestLocale()
        };

        Response.Headers["X-Debug-TenantId"] = tenantId?.ToString() ?? "null";
        Logger.LogDebug(
            "Listing {Module}.{Entity} for tenant {TenantId}. Filter: {Filter}, OrderBy: {OrderBy}, Top: {Top}, Skip: {Skip}, Expand: {Expand}",
            module, entity, tenantId, filter, orderBy, top, skip, expand);

        // Execute query via service
        var (items, expandedNavs) = await _queryService.ExecuteListQueryAsync(entityDef, options, expandOptions, apply, ct);

        // Expand OneToMany/ManyToMany navigations
        if (expandOptions != null && items.Count > 0)
            await _queryService.ExpandNavigationsAsync(entityDef, items, expandOptions, tenantId, ct);

        // Handle $compute
        if (!string.IsNullOrWhiteSpace(compute))
        {
            var computeValidationError = EntityValidationService.ValidateComputeFieldReferences(entityDef, compute);
            if (computeValidationError != null)
            {
                return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidCompute, computeValidationError, entity));
            }
            items = ODataComputeHelper.ApplyComputedProperties(items, compute);
        }

        // Evaluate application-level computed fields
        if (items.Count > 0)
            _queryService.EvaluateComputedFields(entityDef, items);

        // Execute after-read rules
        if (items.Count > 0)
        {
            var afterReadCtx = requestContext.ToEvaluationContext();
            await RuleEngine.ExecuteAfterReadAsync(entityDef, items, afterReadCtx);
        }

        // Get total count
        int? totalCount = null;
        if (count || items.Count >= top)
            totalCount = await _queryService.GetCountAsync(entityDef, options, ct);

        // Build response
        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";

        // Generate nextLink
        string? nextLink = null;
        if (totalCount.HasValue)
        {
            var nextSkip = skip + top;
            if (nextSkip < totalCount.Value)
            {
                var queryParams = new List<string> { $"$skip={nextSkip}", $"$top={top}" };
                if (!string.IsNullOrEmpty(filter)) queryParams.Add($"$filter={Uri.EscapeDataString(filter)}");
                if (!string.IsNullOrEmpty(orderBy)) queryParams.Add($"$orderby={Uri.EscapeDataString(orderBy)}");
                if (!string.IsNullOrEmpty(select)) queryParams.Add($"$select={Uri.EscapeDataString(select)}");
                nextLink = $"{baseUrl}?{string.Join("&", queryParams)}";
            }
        }

        // Delta link
        string? deltaLink = null;
        if (trackChanges || !string.IsNullOrWhiteSpace(deltaToken))
        {
            var effectiveTenantIdForDelta = entityDef.TenantScoped ? tenantId : (Guid?)null;
            deltaLink = _deltaTokenService.GenerateDeltaLink(
                $"{Request.Scheme}://{Request.Host}", module, entity, effectiveTenantIdForDelta, filter);
        }

        // Add @odata.id to each item
        foreach (var item in items)
        {
            var itemId = item.GetIdValue();
            if (itemId != null)
                item[ODataConstants.JsonProperties.Id] = ODataResponseHelper.BuildODataId(Request, module, entity, itemId);
        }

        // HasStream annotations
        ODataResponseHelper.InjectHasStreamAnnotationsBatch(items, Request, module, entity, entityDef);

        // Apply field-level restrictions
        _queryService.ApplyFieldRestrictions(entityDef, items, requestContext);

        // Apply service projection filtering
        EntityQueryService.ApplyProjectionFiltering(entityDef, items);

        // M12: Delta @removed — include soft-deleted records since last delta token
        if (deltaPayload != null && entityDef.Fields.Any(f => f.Name.Equals("IsDeleted", StringComparison.OrdinalIgnoreCase)))
        {
            var deletedItems = await QueryDeletedSinceDeltaAsync(
                entityDef, deltaPayload.Timestamp.UtcDateTime, tenantId, ct);
            foreach (var deletedItem in deletedItems)
            {
                items.Add(deletedItem);
            }
        }

        sw.Stop();
        Metrics.RecordCrudOperation("list", $"{module}_{entity}", sw.Elapsed.TotalMilliseconds);

        return Ok(new ODataCollectionResponse<Dictionary<string, object?>>
        {
            Context = ODataResponseHelper.BuildEntitySetContext(Request, module, entity),
            Count = count ? totalCount : null,
            NextLink = nextLink,
            DeltaLink = deltaLink,
            Value = items
        });
    }

    /// <summary>
    /// Get a single entity by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$expand")] string? expand = null,
        [FromQuery] DateTimeOffset? asOf = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);
        var requestContext = BuildRequestContext();

        // Access control check
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        // Execute before-read rules
        {
            var beforeReadCtx = requestContext.ToEvaluationContext();
            var beforeReadResult = await RuleEngine.ExecuteBeforeReadAsync(entityDef, beforeReadCtx);
            if (!beforeReadResult.Success)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "ValidationFailed",
                    string.Join("; ", beforeReadResult.Errors.Select(e => e.Message))));
            }
        }

        // Parse $expand
        Dictionary<string, ExpandOptions>? expandOptions = null;
        if (!string.IsNullOrWhiteSpace(expand))
        {
            expandOptions = _expandParser.Parse(expand);
        }

        var options = new QueryOptions
        {
            Select = select,
            TenantId = entityDef.TenantScoped ? tenantId : null,
            AsOf = asOf,
            Expand = expand,
            ExpandOptions = expandOptions,
            Locale = GetRequestLocale()
        };

        Logger.LogDebug("Getting {Module}.{Entity} with ID {Id} for tenant {TenantId}{AsOf}",
            module, entity, id, tenantId, asOf.HasValue ? $" AsOf {asOf.Value}" : "");

        var result = await _queryService.GetByIdAsync(entityDef, id, options, expandOptions, ct);

        if (result == null)
            throw new EntityNotFoundException($"{module}.{entity}", id);

        // Expand OneToMany navigations
        if (expandOptions != null)
            await _queryService.ExpandNavigationsForSingleAsync(entityDef, result, expandOptions, tenantId, ct);

        // Evaluate computed fields
        _queryService.EvaluateComputedFields(entityDef, new List<Dictionary<string, object?>> { result });

        // Execute after-read rules
        {
            var afterReadCtx = requestContext.ToEvaluationContext();
            await RuleEngine.ExecuteAfterReadAsync(entityDef, new List<Dictionary<string, object?>> { result }, afterReadCtx);
        }

        // Add OData annotations (context, id, etag)
        ODataResponseHelper.InjectEntityAnnotations(result, Response, Request, module, entity, id);

        // HasStream annotations
        ODataResponseHelper.InjectHasStreamAnnotations(result, Request, module, entity, entityDef);

        // Apply field-level restrictions
        result = _queryService.ApplyFieldRestrictions(entityDef, result, requestContext);

        // Apply projection filtering
        EntityQueryService.ApplyProjectionFiltering(entityDef, result);

        return Ok(result);
    }

    /// <summary>
    /// Get all historical versions of an entity by ID.
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(ODataCollectionResponse<Dictionary<string, object?>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        if (!entityDef.IsTemporal)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "ENTITY_NOT_TEMPORAL",
                $"Entity {module}.{entity} is not temporal. Only temporal entities have version history.",
                $"{module}.{entity}"));
        }

        var options = new QueryOptions
        {
            TenantId = entityDef.TenantScoped ? tenantId : null,
            IncludeHistory = true,
            OrderBy = "system_start desc"
        };

        Logger.LogDebug("Getting versions of {Module}.{Entity} with ID {Id} for tenant {TenantId}",
            module, entity, id, tenantId);

        var (sql, parameters) = SqlBuilder.BuildSelectQuery(entityDef, options, id);
        var items = await QueryExecutor.ExecuteListAsync(sql, parameters, ct);

        // Execute after-read rules
        if (items.Count > 0)
        {
            var requestContext = BuildRequestContext();
            var afterReadCtx = requestContext.ToEvaluationContext();
            await RuleEngine.ExecuteAfterReadAsync(entityDef, items, afterReadCtx);
        }

        return Ok(new ODataCollectionResponse<Dictionary<string, object?>>
        {
            Context = ODataResponseHelper.BuildEntitySetContext(Request, module, entity),
            Count = items.Count,
            Value = items
        });
    }

    /// <summary>
    /// OData v4 $count endpoint — returns plain-text count of entities.
    /// </summary>
    [HttpGet("$count")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Count(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$search")] string? search = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        var options = new QueryOptions
        {
            Filter = filter,
            TenantId = entityDef.TenantScoped ? tenantId : null,
            Search = search
        };

        var totalCount = await _queryService.GetCountAsync(entityDef, options, ct);
        return Content(totalCount.ToString(), "text/plain");
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Create a new entity.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromBody] Dictionary<string, object?> data,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (data == null || data.Count == 0)
            throw new Middleware.ValidationException("Request body cannot be empty");

        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Create, data);
        if (permissionResult != null) return permissionResult;

        var requestContext = BuildRequestContext();

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            var result = await _writeService.CreateAsync(entityDef, module, entity, data, requestContext, ct);

            if (!result.IsSuccess)
            {
                await _unitOfWork.RollbackAsync(ct);
                return StatusCode(result.StatusCode, ODataErrorResponse.FromException(
                    result.ErrorCode!, result.ErrorMessage!));
            }

            var created = result.Data!;
            var createdId = created.GetValueOrDefault("Id");

            // Add OData annotations (context, id, etag)
            ODataResponseHelper.InjectEntityAnnotations(created, Response, Request, module, entity, createdId);
            ODataResponseHelper.InjectRuleMessages(created, result);

            sw.Stop();
            Metrics.RecordCrudOperation("create", $"{module}_{entity}", sw.Elapsed.TotalMilliseconds);

            // Respect Prefer header
            var (returnMinimal, returnRepresentation, _, _) = ODataResponseHelper.ParsePreferHeader(Request);
            var odataEntityId = ODataResponseHelper.BuildODataId(Request, module, entity, createdId);
            return ODataResponseHelper.ApplyPreferReturn(this,
                created, returnMinimal, StatusCodes.Status201Created,
                nameof(GetById), new { tenantId, module, entity, id = createdId },
                odataEntityId, returnRepresentation);
        }, ct);
    }

    /// <summary>
    /// Update an existing entity (partial update).
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromBody] Dictionary<string, object?> data,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (data == null || data.Count == 0)
            throw new Middleware.ValidationException("Request body cannot be empty");

        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update, data);
        if (permissionResult != null) return permissionResult;

        // ACL-based readonly field restrictions
        var userCtx = HttpContext.GetUserContext();
        if (userCtx != null)
        {
            var readonlyFields = FieldRestrictionApplier.GetReadonlyFieldNames(entityDef, userCtx, data);
            if (readonlyFields.Count > 0)
            {
                var strippedReadonly = new List<string>();
                foreach (var key in data.Keys.ToList())
                {
                    if (readonlyFields.Contains(key))
                    {
                        data.Remove(key);
                        strippedReadonly.Add(key);
                    }
                }
                if (strippedReadonly.Count > 0)
                    Logger.LogDebug("Stripped ACL readonly fields from update: {Fields}", string.Join(", ", strippedReadonly));
            }

            if (data.Count == 0)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "ReadonlyFields", "All provided fields are readonly and cannot be updated"));
            }
        }

        var requestContext = BuildRequestContext();
        var ifMatch = Request.Headers.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal.ToString() : null;

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            var result = await _writeService.UpdateAsync(entityDef, module, entity, id, data, requestContext, ifMatch, ct);

            if (!result.IsSuccess)
            {
                await _unitOfWork.RollbackAsync(ct);
                if (result.StatusCode == 412)
                {
                    return StatusCode(StatusCodes.Status412PreconditionFailed,
                        ODataErrorResponse.PreconditionFailed(result.Data?["ETag"]?.ToString() ?? ""));
                }
                return StatusCode(result.StatusCode, ODataErrorResponse.FromException(
                    result.ErrorCode!, result.ErrorMessage!));
            }

            var updated = result.Data!;

            ODataResponseHelper.InjectEntityAnnotations(updated, Response, Request, module, entity, id);
            ODataResponseHelper.InjectRuleMessages(updated, result);

            sw.Stop();
            Metrics.RecordCrudOperation("update", $"{module}_{entity}", sw.Elapsed.TotalMilliseconds);

            var (returnMinimal, returnRepresentation, _, _) = ODataResponseHelper.ParsePreferHeader(Request);
            var odataEntityId = ODataResponseHelper.BuildODataId(Request, module, entity, id);
            return ODataResponseHelper.ApplyPreferReturn(this, updated, returnMinimal,
                odataEntityId: odataEntityId, returnRepresentation: returnRepresentation);
        }, ct);
    }

    /// <summary>
    /// OData v4: Full replace of an entity (PUT).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replace(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromBody] Dictionary<string, object?> data,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (data == null || data.Count == 0)
            throw new Middleware.ValidationException("Request body cannot be empty for PUT");

        var tenantId = HttpContext.GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update, data);
        if (permissionResult != null) return permissionResult;

        var requestContext = BuildRequestContext();
        var ifMatch = Request.Headers.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal.ToString() : null;

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            var result = await _writeService.ReplaceAsync(entityDef, module, entity, id, data, requestContext, ifMatch, ct);

            if (!result.IsSuccess)
            {
                await _unitOfWork.RollbackAsync(ct);
                if (result.StatusCode == 412)
                {
                    return StatusCode(StatusCodes.Status412PreconditionFailed,
                        ODataErrorResponse.PreconditionFailed(result.Data?["ETag"]?.ToString() ?? ""));
                }
                return StatusCode(result.StatusCode, ODataErrorResponse.FromException(
                    result.ErrorCode!, result.ErrorMessage!));
            }

            var updated = result.Data!;

            ODataResponseHelper.InjectEntityAnnotations(updated, Response, Request, module, entity, id);
            ODataResponseHelper.InjectRuleMessages(updated, result);

            sw.Stop();
            Metrics.RecordCrudOperation("put", $"{module}_{entity}", sw.Elapsed.TotalMilliseconds);

            var (returnMinimal, returnRepresentation, _, _) = ODataResponseHelper.ParsePreferHeader(Request);
            var odataEntityId = ODataResponseHelper.BuildODataId(Request, module, entity, id);
            return ODataResponseHelper.ApplyPreferReturn(this, updated, returnMinimal,
                odataEntityId: odataEntityId, returnRepresentation: returnRepresentation);
        }, ct);
    }

    /// <summary>
    /// Delete an entity.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromQuery] bool soft = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Delete);
        if (permissionResult != null) return permissionResult;

        var requestContext = BuildRequestContext();
        var ifMatch = Request.Headers.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal.ToString() : null;

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            var result = await _writeService.DeleteAsync(entityDef, module, entity, id, requestContext, soft, ifMatch, ct);

            if (!result.IsSuccess)
            {
                await _unitOfWork.RollbackAsync(ct);
                if (result.StatusCode == 412)
                {
                    return StatusCode(StatusCodes.Status412PreconditionFailed,
                        ODataErrorResponse.PreconditionFailed(result.Data?["ETag"]?.ToString() ?? ""));
                }
                return StatusCode(result.StatusCode, ODataErrorResponse.FromException(
                    result.ErrorCode!, result.ErrorMessage!));
            }

            sw.Stop();
            Metrics.RecordCrudOperation("delete", $"{module}_{entity}", sw.Elapsed.TotalMilliseconds);

            return NoContent();
        }, ct);
    }

    #endregion

    #region Singleton Operations

    /// <summary>
    /// OData v4 Singleton: PATCH without ID — updates the single instance for the tenant.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchSingleton(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromBody] Dictionary<string, object?> data,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        if (!entityDef.HasAnnotation(ODataConstants.Annotations.Singleton))
        {
            return BadRequest(ODataErrorResponse.FromException(
                "NOT_SINGLETON", $"Entity '{entity}' is not a singleton. PATCH requires an ID.", entity));
        }

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update, data);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : (Guid?)null;

        EntityValidationService.StripComputedFields(entityDef, data, isUpdate: true);

        // Find the singleton instance
        var options = new QueryOptions { TenantId = effectiveTenantId, Top = 1 };
        var (selectSql, selectParams) = SqlBuilder.BuildSelectQuery(entityDef, options);
        var items = await QueryExecutor.ExecuteListAsync(selectSql, selectParams, ct);
        var current = items.FirstOrDefault();

        if (current == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SingletonNotFound, $"Singleton instance of '{entity}' not found", entity));

        Guid singletonId;
        var idObj = current.GetIdValue();
        if (idObj is Guid g)
            singletonId = g;
        else if (idObj != null && Guid.TryParse(idObj.ToString(), out var parsed))
            singletonId = parsed;
        else if (idObj != null)
            return BadRequest(ODataErrorResponse.FromException("INVALID_ID", "Invalid singleton ID format"));
        else
            return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SingletonNoKey, $"Singleton instance of '{entity}' has no ID field", entity));

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            var evalContext = BuildRequestContext().ToEvaluationContext();
            var ruleResult = await RuleEngine.ExecuteBeforeUpdateAsync(entityDef, current, data, evalContext);

            if (!ruleResult.Success)
            {
                Logger.LogWarning("Singleton update validation failed for {Entity}: {ErrorCount} errors",
                    entity, ruleResult.Errors.Count);
                await _unitOfWork.RollbackAsync(ct);
                return BadRequest(ODataErrorResponse.FromException(
                    "ValidationFailed",
                    string.Join("; ", ruleResult.Errors.Select(e => e.Message))));
            }

            foreach (var (field, value) in ruleResult.ComputedValues)
                data[field] = value;

            var (updateSql, updateParams) = SqlBuilder.BuildUpdateQuery(entityDef, singletonId, data, effectiveTenantId);
            var updated = await QueryExecutor.ExecuteReturningAsync(updateSql, updateParams, ct);

            if (updated == null)
            {
                await _unitOfWork.RollbackAsync(ct);
                return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SingletonNotFound, $"Singleton instance of '{entity}' not found during update", entity));
            }

            _unitOfWork.EnqueueEvent(new DomainEvent
            {
                EventName = $"{entity}Updated",
                EntityName = entity,
                EntityId = singletonId,
                Payload = new Dictionary<string, object?>(updated),
                TenantId = effectiveTenantId,
                UserId = GetCurrentUserId(),
                SourceModule = module
            });

            try { await RuleEngine.ExecuteAfterUpdateAsync(entityDef, current, updated, evalContext); }
            catch (Exception ex) { Logger.LogWarning(ex, "After-update rules failed for singleton {Entity}", entity); }

            ODataResponseHelper.InjectEntityAnnotations(updated, Response, Request, module, entity, singletonId);

            return Ok(updated);
        }, ct);
    }

    private async Task<IActionResult> GetSingletonInternal(
        BmEntity entityDef, string module, string entity,
        Guid? tenantId, string? select, string? expand,
        CancellationToken ct)
    {
        Dictionary<string, ExpandOptions>? expandOptions = null;
        if (!string.IsNullOrWhiteSpace(expand))
        {
            expandOptions = _expandParser.Parse(expand);
        }

        var options = new QueryOptions
        {
            Select = select,
            TenantId = entityDef.TenantScoped ? tenantId : null,
            Expand = expand,
            ExpandOptions = expandOptions,
            Top = 1
        };

        var (items, _) = await _queryService.ExecuteListQueryAsync(entityDef, options, expandOptions, null, ct);

        var result = items.FirstOrDefault();
        if (result == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.SingletonNotFound, $"Singleton instance of '{entity}' not found", entity));

        if (expandOptions != null)
            await _queryService.ExpandNavigationsForSingleAsync(entityDef, result, expandOptions, tenantId, ct);

        // Execute after-read rules
        {
            var requestContext = BuildRequestContext();
            var afterReadCtx = requestContext.ToEvaluationContext();
            await RuleEngine.ExecuteAfterReadAsync(entityDef, new List<Dictionary<string, object?>> { result }, afterReadCtx);
        }

        // Singleton context is entitySet-level (no /$entity suffix)
        result[ODataConstants.JsonProperties.Context] = ODataResponseHelper.BuildEntitySetContext(Request, module, entity);
        var etag = ETagGenerator.GenerateWeakETag(result);
        Response.Headers.ETag = etag;
        result[ODataConstants.JsonProperties.Etag] = etag;

        return Ok(result);
    }

    #endregion

    #region OData v4: Entity-Level Media Streams (HasStream)

    [HttpGet("{id:guid}/$value")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaStream(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);
        if (!entityDef.HasStream)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotMediaEntity, $"Entity '{entity}' is not a media entity (missing @HasStream annotation)", entity));

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required for scoped entity", entity));

        var ifNoneMatch = Request.Headers.TryGetValue("If-None-Match", out var inmVal) ? inmVal.ToString() : null;
        var result = await _mediaService.GetMediaStreamAsync(entityDef, id, tenantId, ifNoneMatch, ct);

        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));
        if (result.NotModified)
        {
            Response.Headers["ETag"] = $"\"{result.ETag}\"";
            return StatusCode(StatusCodes.Status304NotModified);
        }
        if (result.StatusCode == 204)
            return NoContent();

        if (!string.IsNullOrEmpty(result.ETag))
            Response.Headers["ETag"] = $"\"{result.ETag}\"";

        return File(result.Content!, result.ContentType!);
    }

    [HttpPut("{id:guid}/$value")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UpdateMediaStream(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);
        if (!entityDef.HasStream)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotMediaEntity, $"Entity '{entity}' is not a media entity", entity));

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required", entity));

        // Early Content-Length check (still useful for fast rejection)
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > entityDef.MaxMediaSize)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, ODataErrorResponse.FromException(
                "MEDIA_TOO_LARGE", $"Media content exceeds maximum size of {entityDef.MaxMediaSize} bytes (Content-Length: {Request.ContentLength.Value})", entity));

        // Read body with size limit to prevent OOM from chunked requests without Content-Length
        var maxSize = entityDef.MaxMediaSize > 0 ? entityDef.MaxMediaSize : 10 * 1024 * 1024; // default 10MB
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > maxSize)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, ODataErrorResponse.FromException(
                    "MEDIA_TOO_LARGE", $"Media content exceeds maximum size of {maxSize} bytes", entity));
            ms.Write(buffer, 0, bytesRead);
        }
        var content = ms.ToArray();

        var contentType = Request.ContentType ?? "application/octet-stream";
        var ifMatch = Request.Headers.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal.ToString() : null;

        var result = await _mediaService.UpdateMediaStreamAsync(entityDef, id, content, contentType, tenantId, ifMatch, ct);

        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));

        if (!string.IsNullOrEmpty(result.ETag))
            Response.Headers["ETag"] = $"\"{result.ETag}\"";

        return NoContent();
    }

    [HttpDelete("{id:guid}/$value")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMediaStream(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);
        if (!entityDef.HasStream)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotMediaEntity, $"Entity '{entity}' is not a media entity", entity));

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Delete);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required", entity));

        var ifMatch = Request.Headers.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal.ToString() : null;
        var result = await _mediaService.DeleteMediaStreamAsync(entityDef, id, tenantId, ifMatch, ct);

        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));

        return NoContent();
    }

    #endregion

    #region OData v4: Stream Properties

    [HttpGet("{id:guid}/{property}/$value")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPropertyValue(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string property,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required for scoped entity", entity));

        var result = await _propertyService.GetPropertyValueAsync(entityDef, id, property, tenantId, ct);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));

        if (result.Value == null)
            return NoContent();

        return result.Value switch
        {
            byte[] bytes => File(bytes, "application/octet-stream"),
            string str => Content(str, "text/plain"),
            _ => Content(result.Value.ToString() ?? "", "text/plain")
        };
    }

    [HttpPut("{id:guid}/{property}/$value")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePropertyValue(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string property,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required for scoped entity", entity));

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var content = ms.ToArray();

        var result = await _propertyService.UpdatePropertyValueAsync(entityDef, id, property, content, tenantId, ct);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));

        return NoContent();
    }

    [HttpDelete("{id:guid}/{property}/$value")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePropertyValue(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string property,
        CancellationToken ct = default)
    {
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Delete);
        if (permissionResult != null) return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        if (entityDef.TenantScoped && tenantId == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantRequired, "Tenant context required for scoped entity", entity));

        var result = await _propertyService.DeletePropertyValueAsync(entityDef, id, property, tenantId, ct);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, ODataErrorResponse.FromException(result.ErrorCode!, result.ErrorMessage!));

        return NoContent();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// M12: Query soft-deleted records since a delta token timestamp.
    /// Returns items annotated with @removed for OData delta responses.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> QueryDeletedSinceDeltaAsync(
        BmEntity entityDef,
        DateTime sinceUtc,
        Guid? tenantId,
        CancellationToken ct)
    {
        var hasDeletedAt = entityDef.Fields.Any(f => f.Name.Equals("DeletedAt", StringComparison.OrdinalIgnoreCase));

        // Query for records that were soft-deleted since the delta token timestamp
        var deletedFilter = hasDeletedAt
            ? $"deletedAt ge {sinceUtc:O}"
            : $"isDeleted eq true";

        var deletedOptions = new QueryOptions
        {
            Filter = deletedFilter,
            Select = "Id",
            TenantId = entityDef.TenantScoped ? tenantId : null,
            IncludeDeleted = true
        };

        var (deletedItems, _) = await _queryService.ExecuteListQueryAsync(entityDef, deletedOptions, null, null, ct);

        var removedItems = new List<Dictionary<string, object?>>();
        foreach (var item in deletedItems)
        {
            var id = item.GetValueOrDefault("Id") ?? item.GetValueOrDefault("id");
            if (id == null) continue;

            removedItems.Add(new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["@removed"] = new Dictionary<string, object?> { ["reason"] = "deleted" }
            });
        }

        Logger.LogDebug("Delta @removed: found {Count} deleted records since {Since}",
            removedItems.Count, sinceUtc);

        return removedItems;
    }

    #endregion
}
