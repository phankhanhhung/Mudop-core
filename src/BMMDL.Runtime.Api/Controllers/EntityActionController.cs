using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Observability;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Services;
using BMMDL.Runtime.Validation;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Controller for OData bound actions, functions, and navigation property POST operations.
/// Routes: POST/GET /api/odata/{module}/{entity}/{id}/{actionOrNavProperty}
/// This controller handles the route ambiguity by checking actions first, then navigation properties.
/// </summary>
[Tags("Entity Actions")]
public class EntityActionController : EntityControllerBase
{
    private readonly IActionExecutor _actionExecutor;
    private readonly IUnitOfWork _unitOfWork;

    public EntityActionController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IFieldRestrictionApplier fieldRestrictionApplier,
        IActionExecutor actionExecutor,
        IUnitOfWork unitOfWork,
        BmmdlMetrics metrics,
        ILogger<EntityActionController> logger,
        IPermissionChecker permissionChecker,
        IEntityResolver entityResolver)
        : base(cacheManager, sqlBuilder, queryExecutor, ruleEngine, eventPublisher, fieldRestrictionApplier, metrics, logger, permissionChecker, entityResolver)
    {
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Handle POST to entity/{id}/{name} - dispatches to bound action or contained entity creation.
    /// Route: POST /api/odata/{module}/{entity}/{id}/{actionOrNavProperty}
    /// First checks if name matches a bound action, if not, treats it as navigation property creation.
    /// </summary>
    [HttpPost("{id:guid}/{actionOrNavProperty}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandlePostToEntityMember(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string actionOrNavProperty,
        [FromBody] Dictionary<string, object?>? data = null,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // First, check if it's a bound action
        var action = entityDef.BoundActions.FirstOrDefault(a =>
            a.Name.Equals(actionOrNavProperty, StringComparison.OrdinalIgnoreCase));

        if (action != null)
        {
            // Access control check: bound actions require execute permission
            var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Execute);
            if (permissionResult != null) return permissionResult;

            // It's a bound action - invoke it
            return await InvokeBoundActionInternal(module, entity, entityDef, id, action, actionOrNavProperty, data, tenantId, ct);
        }

        // Not an action - check if it's a navigation property for contained entity creation
        var composition = entityDef.Compositions.FirstOrDefault(c =>
            c.Name.Equals(actionOrNavProperty, StringComparison.OrdinalIgnoreCase));

        var assoc = composition ?? entityDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(actionOrNavProperty, StringComparison.OrdinalIgnoreCase));

        if (assoc != null)
        {
            // Access control check: creating contained entity requires create permission on the CHILD entity
            var childEntityDef = await GetEntityDefinitionAsync(module, assoc.TargetEntity);
            var permissionResult = await CheckPermissionAsync(childEntityDef, CrudOperation.Create, data);
            if (permissionResult != null) return permissionResult;

            // It's a navigation property - create contained entity
            return await CreateContainedEntityInternal(module, entity, entityDef, id, assoc, actionOrNavProperty, data ?? new(), tenantId, ct);
        }

        // Neither action nor navigation property found
        Logger.LogWarning("No action or navigation property '{Name}' found on {Entity}", actionOrNavProperty, entity);
        return NotFound(ODataErrorResponse.FromException(
            "ACTION_OR_NAV_NOT_FOUND",
            $"'{actionOrNavProperty}' is neither a bound action nor a navigation property on entity '{entity}'",
            $"{module}.{entity}.{actionOrNavProperty}"));
    }

    /// <summary>
    /// Internal method to invoke a bound action.
    /// </summary>
    private async Task<IActionResult> InvokeBoundActionInternal(
        string module,
        string entity,
        BMMDL.MetaModel.Structure.BmEntity entityDef,
        Guid id,
        BMMDL.MetaModel.Service.BmAction action,
        string actionName,
        Dictionary<string, object?>? parameters,
        Guid? tenantId,
        CancellationToken ct)
    {
        Logger.LogInformation("Invoking bound action {Entity}.{Action} on ID {Id}",
            entity, actionName, id);

        // M13: Validate parameter types
        var validationErrors = ValidateActionParameters(action.Parameters, parameters);
        if (validationErrors.Count > 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                string.Join("; ", validationErrors)));
        }

        // Get the entity instance
        var effectiveTenantId = GetEffectiveTenantId(entityDef);
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, sqlParams) = SqlBuilder.BuildSelectQuery(entityDef, options, id);
        var entityInstance = await QueryExecutor.ExecuteSingleAsync(sql, sqlParams, ct);

        if (entityInstance == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", id);
        }

        // Create evaluation context with entity instance
        var evalContext = CreateEvaluationContext(tenantId, entityInstance, parameters);

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            // Execute action using action executor (hybrid: DB or interpreted)
            var actionResult = await _actionExecutor.ExecuteActionAsync(
                entityDef, action, id, parameters ?? new(), evalContext, ct);

            if (!actionResult.Success)
            {
                await _unitOfWork.RollbackAsync(ct);
                Logger.LogWarning("Action {Action} failed: {Message}", actionName, actionResult.ErrorMessage);
                return BadRequest(ODataErrorResponse.FromException(
                    ODataConstants.ErrorCodes.ActionExecutionFailed,
                    actionResult.ErrorMessage ?? "Action execution failed",
                    $"{module}.{entity}.{actionName}"));
            }

            // Persist computed/modified field changes back to the entity row
            if (actionResult.ComputedValues.Count > 0)
            {
                var effectiveTenantId = GetEffectiveTenantId(entityDef);
                var (updateSql, updateParams) = SqlBuilder.BuildUpdateQuery(
                    entityDef, id, actionResult.ComputedValues, effectiveTenantId);
                await QueryExecutor.ExecuteNonQueryAsync(updateSql, updateParams, ct);
            }

            // Build response dictionary with success flag and computed values
            var responseDict = new Dictionary<string, object?> { ["success"] = true };
            foreach (var kv in actionResult.ComputedValues)
                responseDict[kv.Key] = kv.Value;
            if (actionResult.Value != null)
                responseDict["value"] = actionResult.Value;

            // Enqueue events if action defines them (dispatched post-commit)
            foreach (var eventName in action.Emits)
            {
                var eventPayload = new Dictionary<string, object?>
                {
                    ["ActionName"] = actionName,
                    ["EntityName"] = entity,
                    ["EntityId"] = id,
                    ["TenantId"] = tenantId,
                    ["Result"] = responseDict,
                    ["Timestamp"] = DateTime.UtcNow
                };

                _unitOfWork.EnqueueEvent(new BMMDL.Runtime.Events.DomainEvent
                {
                    EventName = eventName,
                    EntityName = entity,
                    EntityId = id,
                    TenantId = tenantId,
                    Payload = eventPayload
                });
            }

            return Ok(responseDict);
        }, ct);
    }

    /// <summary>
    /// Handle GET to entity/{id}/{segment} - dispatches to bound function or navigation property.
    /// Route: GET /api/odata/{module}/{entity}/{id}/{segment}
    /// First checks if segment is a function call (with or without params), then navigation property.
    /// </summary>
    [HttpGet("{id:guid}/{segment}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HandleGetEntityMember(
        [FromRoute] string module,
        [FromRoute] string entity,
        [FromRoute] Guid id,
        [FromRoute] string segment,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$orderby")] string? orderBy = null,
        [FromQuery(Name = "$top")] int? top = null,
        [FromQuery(Name = "$skip")] int? skip = null,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$count")] bool count = false,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top.HasValue && (top.Value < 0 || top.Value > QueryConstants.MaxPageSize))
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                $"$top must be between 0 and {QueryConstants.MaxPageSize}"));
        if (skip.HasValue && skip.Value < 0)
            return BadRequest(ODataErrorResponse.FromException("INVALID_SKIP",
                "$skip must be non-negative"));

        var tenantId = GetTenantId();
        var entityDef = await GetEntityDefinitionAsync(module, entity);

        // Access control check: read permission for both functions and navigation reads
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null) return permissionResult;

        // Parse segment - might be function call like "GetPrice(discount=0.10)" or navigation property
        var (name, inlineParams) = ParseFunctionCall(segment);

        // First, check if it's a bound function
        var function = entityDef.BoundFunctions.FirstOrDefault(f =>
            f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (function != null)
        {
            // It's a bound function - invoke it
            return await InvokeBoundFunctionInternal(module, entity, entityDef, id, function, name, inlineParams, tenantId, ct);
        }

        // Not a function - check if it's a navigation property
        var composition = entityDef.Compositions.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var assoc = composition ?? entityDef.Associations.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (assoc != null)
        {
            // It's a navigation property - get contained entities
            return await GetContainedEntitiesInternal(module, entity, entityDef, id, assoc, name, tenantId, filter, orderBy, top, skip, select, count, ct);
        }

        // Neither function nor navigation property found
        Logger.LogWarning("No function or navigation property '{Name}' found on {Entity}", name, entity);
        return NotFound(ODataErrorResponse.FromException(
            "FUNCTION_OR_NAV_NOT_FOUND",
            $"'{name}' is neither a bound function nor a navigation property on entity '{entity}'",
            $"{module}.{entity}.{name}"));
    }

    /// <summary>
    /// Internal method to invoke a bound function.
    /// </summary>
    private async Task<IActionResult> InvokeBoundFunctionInternal(
        string module,
        string entity,
        BMMDL.MetaModel.Structure.BmEntity entityDef,
        Guid id,
        BMMDL.MetaModel.Service.BmFunction function,
        string functionName,
        Dictionary<string, string> inlineParams,
        Guid? tenantId,
        CancellationToken ct)
    {
        Logger.LogInformation("Invoking bound function {Entity}.{Function} on ID {Id}",
            entity, functionName, id);

        // M13: Validate inline parameter types
        var typedInlineForValidation = new Dictionary<string, object?>();
        foreach (var kvp in inlineParams)
            typedInlineForValidation[kvp.Key] = (object?)kvp.Value;
        var validationErrors = ValidateActionParameters(function.Parameters, typedInlineForValidation);
        if (validationErrors.Count > 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                string.Join("; ", validationErrors)));
        }

        // Get the entity instance
        var effectiveTenantId = GetEffectiveTenantId(entityDef);
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, sqlParams) = SqlBuilder.BuildSelectQuery(entityDef, options, id);
        var entityInstance = await QueryExecutor.ExecuteSingleAsync(sql, sqlParams, ct);

        if (entityInstance == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", id);
        }

        // Convert inline parameters to typed parameters
        var typedParams = new Dictionary<string, object?>();
        foreach (var kvp in inlineParams)
        {
            var paramDef = function.Parameters.FirstOrDefault(p =>
                p.Name.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));

            typedParams[kvp.Key] = paramDef != null
                ? ConvertParameterValue(kvp.Value, paramDef.Type)
                : kvp.Value;
        }

        // Create evaluation context
        var evalContext = CreateEvaluationContext(tenantId, entityInstance, typedParams);

        // Execute function using action executor
        var funcResult = await _actionExecutor.ExecuteFunctionAsync(
            entityDef, function, id, typedParams, evalContext, ct);

        if (!funcResult.Success)
        {
            Logger.LogWarning("Function {Function} failed: {Message}", functionName, funcResult.ErrorMessage);
            return BadRequest(ODataErrorResponse.FromException(
                "FUNCTION_EXECUTION_FAILED",
                funcResult.ErrorMessage ?? "Function execution failed",
                $"{module}.{entity}.{functionName}"));
        }

        // Build response with success flag and return value
        var funcResponse = new Dictionary<string, object?> { ["success"] = true };
        if (funcResult.Value != null)
            funcResponse["value"] = funcResult.Value;
        return Ok(funcResponse);
    }

    /// <summary>
    /// Internal method to get contained entities via navigation property.
    /// </summary>
    private async Task<IActionResult> GetContainedEntitiesInternal(
        string module,
        string entity,
        BMMDL.MetaModel.Structure.BmEntity parentDef,
        Guid id,
        BMMDL.MetaModel.Structure.BmAssociation assoc,
        string navProperty,
        Guid? tenantId,
        string? filter,
        string? orderBy,
        int? top,
        int? skip,
        string? select,
        bool count,
        CancellationToken ct)
    {
        Logger.LogInformation("Getting contained entities: {Module}.{Entity}/{Id}/{NavProperty}",
            module, entity, id, navProperty);

        // Verify parent entity exists
        var effectiveTenantId = GetEffectiveTenantId(parentDef);
        var parentOptions = new QueryOptions { TenantId = effectiveTenantId };
        var (selectSql, selectParams) = SqlBuilder.BuildSelectQuery(parentDef, parentOptions, id);
        var parentEntity = await QueryExecutor.ExecuteSingleAsync(selectSql, selectParams, ct);

        if (parentEntity == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", id);
        }

        // C3: Verify parent entity's tenant matches request tenant to prevent cross-tenant access
        if (parentDef.TenantScoped && parentEntity.TryGetValue("TenantId", out var parentTenant))
        {
            var parentTenantId = parentTenant is Guid g ? g : (Guid.TryParse(parentTenant?.ToString(), out var parsed) ? parsed : Guid.Empty);
            if (parentTenantId != tenantId) return NotFound();
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

        List<Dictionary<string, object?>> results;

        // ManyToMany: query through junction table
        if (assoc.Cardinality == BMMDL.MetaModel.Structure.BmCardinality.ManyToMany)
        {
            var names = new[] { parentDef.Name.ToLowerInvariant(), targetEntity.Name.ToLowerInvariant() };
            Array.Sort(names);
            var junctionName = $"{names[0]}_{names[1]}";

            var schema = !string.IsNullOrEmpty(parentDef.Namespace)
                ? NamingConvention.GetSchemaName(parentDef.Namespace)
                : null;
            var qualifiedJunction = schema != null ? $"\"{schema}\".\"{junctionName}\"" : $"\"{junctionName}\"";

            var targetTable = SqlBuilder.GetTableName(targetEntity);
            var sourceFk = NamingConvention.GetFkColumnName(parentDef.Name);
            var targetFk = NamingConvention.GetFkColumnName(targetEntity.Name);

            var quotedTargetFk = NamingConvention.QuoteIdentifier(targetFk);
            var quotedSourceFk = NamingConvention.QuoteIdentifier(sourceFk);

            var mmSql = $"SELECT t.* FROM {targetTable} t INNER JOIN {qualifiedJunction} j ON j.{quotedTargetFk} = t.id WHERE j.{quotedSourceFk} = @p0";
            var mmParams = new List<Npgsql.NpgsqlParameter> { new("@p0", id) };

            if (targetEntity.TenantScoped && tenantId.HasValue)
            {
                mmSql += " AND t.tenant_id = @pTenant";
                mmParams.Add(new("@pTenant", tenantId.Value));
            }

            // Apply additional OData filters (basic support for orderby, top, skip)
            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                var orderToken = orderBy.Replace(" asc", "").Replace(" desc", "").Trim();
                var orderColumn = NamingConvention.ToSnakeCase(orderToken);
                var direction = orderBy.Contains("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

                // Validate orderColumn against target entity fields to prevent SQL injection
                var matchedField = targetEntity.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, orderToken, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NamingConvention.ToSnakeCase(f.Name), orderColumn, StringComparison.OrdinalIgnoreCase));

                if (matchedField != null)
                {
                    var safeColumn = NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(matchedField.Name));
                    mmSql += $" ORDER BY t.{safeColumn} {direction}";
                }
                else
                {
                    Logger.LogWarning("Ignoring invalid $orderby field '{OrderBy}' for entity '{Entity}'. Valid fields: {Fields}",
                        orderBy, targetEntity.Name, string.Join(", ", targetEntity.Fields.Select(f => f.Name)));
                }
            }

            if (top.HasValue)
            {
                mmSql += " LIMIT @pLimit";
                mmParams.Add(new("@pLimit", top.Value));
            }

            if (skip.HasValue)
            {
                mmSql += " OFFSET @pOffset";
                mmParams.Add(new("@pOffset", skip.Value));
            }

            results = await QueryExecutor.ExecuteListAsync(mmSql, mmParams.ToArray(), ct);
        }
        else
        {
            // OneToMany / Composition: children have FK pointing to parent
            // C2: Use parameterized query instead of string interpolation for FK filter
            var parentFkColumn = NamingConvention.GetFkColumnName(entity);
            var quotedParentFkColumn = NamingConvention.QuoteIdentifier(parentFkColumn);

            // Build base query with OData options (excluding FK filter)
            var childOptions = new QueryOptions
            {
                TenantId = targetEntity.TenantScoped ? tenantId : null,
                Filter = filter,
                OrderBy = orderBy,
                Top = top,
                Skip = skip,
                Select = select
            };

            var (sql, parameters) = SqlBuilder.BuildSelectQuery(targetEntity, childOptions);
            // Inject parameterized FK WHERE clause
            var fkParamName = $"@p_parent_fk_{parameters.Count}";
            var fkClause = $"{quotedParentFkColumn} = {fkParamName}";
            var paramList = new List<Npgsql.NpgsqlParameter>(parameters) { new(fkParamName, id) };

            // Insert FK clause into the WHERE position
            var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
            if (whereIndex >= 0)
            {
                // Existing WHERE — prepend FK clause with AND
                sql = sql.Insert(whereIndex + 6, $"{fkClause} AND ");
            }
            else
            {
                // No WHERE — find insertion point (before ORDER BY, LIMIT, OFFSET, or end)
                var insertBefore = FindWhereInsertionPoint(sql);
                sql = sql.Insert(insertBefore, $" WHERE {fkClause}");
            }

            results = await QueryExecutor.ExecuteListAsync(sql, paramList.ToArray(), ct);
        }

        // Build OData response
        var response = new Dictionary<string, object>
        {
            [ODataConstants.JsonProperties.Context] = $"{Request.Scheme}://{Request.Host}/api/odata/$metadata#{assoc.TargetEntity}",
            ["value"] = results
        };

        if (count)
        {
            if (assoc.Cardinality == BMMDL.MetaModel.Structure.BmCardinality.ManyToMany)
            {
                var countNames = new[] { parentDef.Name.ToLowerInvariant(), targetEntity.Name.ToLowerInvariant() };
                Array.Sort(countNames);
                var countJunction = $"{countNames[0]}_{countNames[1]}";
                var countSchema = !string.IsNullOrEmpty(parentDef.Namespace)
                    ? NamingConvention.GetSchemaName(parentDef.Namespace)
                    : null;
                var qualifiedCountJunction = countSchema != null ? $"\"{countSchema}\".\"{countJunction}\"" : $"\"{countJunction}\"";
                var countSourceFk = NamingConvention.GetFkColumnName(parentDef.Name);
                var quotedCountSourceFk = NamingConvention.QuoteIdentifier(countSourceFk);

                var countSql = $"SELECT COUNT(*) FROM {qualifiedCountJunction} WHERE {quotedCountSourceFk} = @p0";
                var countParamList = new List<Npgsql.NpgsqlParameter> { new("@p0", id) };

                // Add tenant filter when either entity is tenant-scoped to prevent cross-tenant count leaks
                if ((parentDef.TenantScoped || targetEntity.TenantScoped) && tenantId.HasValue)
                {
                    countSql += $" AND {NamingConvention.QuoteIdentifier("tenant_id")} = @pCountTenant";
                    countParamList.Add(new("@pCountTenant", tenantId.Value));
                }

                var totalCount = await QueryExecutor.ExecuteScalarAsync<long>(countSql, countParamList.ToArray(), ct);
                response[ODataConstants.JsonProperties.Count] = totalCount;
            }
            else
            {
                // C2: Use parameterized query instead of string interpolation for FK filter
                var countParentFkColumn = NamingConvention.GetFkColumnName(entity);
                var quotedCountParentFkColumn = NamingConvention.QuoteIdentifier(countParentFkColumn);
                var countOptions = new QueryOptions
                {
                    TenantId = targetEntity.TenantScoped ? tenantId : null
                };
                var (countSql, countParams) = SqlBuilder.BuildCountQuery(targetEntity, countOptions);
                // Inject parameterized FK WHERE clause into count query
                var countFkParamName = $"@p_parent_fk_{countParams.Count}";
                var countFkClause = $"{quotedCountParentFkColumn} = {countFkParamName}";
                var countParamList = new List<Npgsql.NpgsqlParameter>(countParams) { new(countFkParamName, id) };

                var countWhereIndex = countSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
                if (countWhereIndex >= 0)
                {
                    countSql = countSql.Insert(countWhereIndex + 6, $"{countFkClause} AND ");
                }
                else
                {
                    countSql += $" WHERE {countFkClause}";
                }
                var totalCount = await QueryExecutor.ExecuteScalarAsync<long>(countSql, countParamList.ToArray(), ct);
                response[ODataConstants.JsonProperties.Count] = totalCount;
            }
        }

        return Ok(response);
    }

    /// <summary>
    /// Internal method to create a contained entity via navigation property.
    /// </summary>
    private async Task<IActionResult> CreateContainedEntityInternal(
        string module,
        string entity,
        BMMDL.MetaModel.Structure.BmEntity parentDef,
        Guid id,
        BMMDL.MetaModel.Structure.BmAssociation assoc,
        string navProperty,
        Dictionary<string, object?> data,
        Guid? tenantId,
        CancellationToken ct)
    {
        Logger.LogInformation("Creating contained entity: {Module}.{Entity}/{Id}/{NavProperty}",
            module, entity, id, navProperty);

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

        // Add parent FK to the child data — use parentDef.Name (not route entity param)
        // to match the DDL convention where FK column derives from the entity definition name
        var parentName = parentDef.Name;
        var parentFkField = NamingConvention.GetFkFieldName(parentName);
        data[parentFkField] = id;

        // Generate ID if not provided
        if (!data.ContainsKey("id"))
        {
            data["id"] = Guid.NewGuid();
        }

        // Add tenant ID if target is tenant-scoped
        if (targetEntity.TenantScoped && tenantId.HasValue)
        {
            data["tenantId"] = tenantId.Value;
        }

        return await ExecuteInUnitOfWorkAsync(_unitOfWork, async ct =>
        {
            // Execute insert
            var childEffectiveTenantId = targetEntity.TenantScoped ? tenantId : null;
            var (insertSql, insertParams) = SqlBuilder.BuildInsertQuery(targetEntity, data, childEffectiveTenantId);
            await QueryExecutor.ExecuteNonQueryAsync(insertSql, insertParams, ct);

            // Fetch and return the created entity
            var childOptions = new QueryOptions { TenantId = childEffectiveTenantId };
            if (!Guid.TryParse(data["id"]?.ToString(), out var childId))
            {
                return BadRequest(ODataErrorResponse.FromException(
                    ODataConstants.ErrorCodes.ValidationError,
                    "Invalid or missing 'id' for contained entity"));
            }
            var (selectSql, selectParams) = SqlBuilder.BuildSelectQuery(targetEntity, childOptions, childId);
            var created = await QueryExecutor.ExecuteSingleAsync(selectSql, selectParams, ct);

            Logger.LogInformation("Created contained entity {TargetEntity}/{ChildId} under {Entity}/{Id}",
                assoc.TargetEntity, data["id"], entity, id);

            return Created(
                $"/api/odata/{module}/{entity}/{id}/{navProperty}/{data["id"]}",
                created);
        }, ct);
    }

    /// <summary>
    /// Parse OData function call syntax: FunctionName(param1=value1,param2=value2)
    /// </summary>
    private static (string functionName, Dictionary<string, string> parameters) ParseFunctionCall(string functionCall)
    {
        var openParen = functionCall.IndexOf('(');
        if (openParen < 0)
        {
            return (functionCall, new Dictionary<string, string>());
        }

        var functionName = functionCall[..openParen];
        var paramsSection = functionCall[(openParen + 1)..].TrimEnd(')');

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(paramsSection))
        {
            foreach (var param in paramsSection.Split(','))
            {
                var parts = param.Split('=', 2);
                if (parts.Length == 2)
                {
                    parameters[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        return (functionName, parameters);
    }

    /// <summary>
    /// Validate parameter types for bound actions/functions.
    /// Checks required parameters and type compatibility.
    /// </summary>
    private static List<string> ValidateActionParameters(
        List<BMMDL.MetaModel.Service.BmParameter> paramDefs,
        Dictionary<string, object?>? providedParams)
    {
        var errors = new List<string>();
        providedParams ??= new Dictionary<string, object?>();

        foreach (var param in paramDefs)
        {
            if (!providedParams.ContainsKey(param.Name))
            {
                if (!param.IsOptional)
                {
                    errors.Add($"Missing required parameter: {param.Name}");
                }
                else if (param.DefaultValueAst != null)
                {
                    // Fill in the default value for optional parameters
                    var evaluator = new BMMDL.Runtime.Expressions.RuntimeExpressionEvaluator();
                    var defaultVal = evaluator.Evaluate(param.DefaultValueAst, new BMMDL.Runtime.Expressions.EvaluationContext());
                    providedParams[param.Name] = defaultVal;
                }
            }
            else
            {
                var value = providedParams[param.Name];
                if (value != null)
                {
                    var typeError = ParameterTypeValidator.ValidateParameterType(param.Name, param.Type, value);
                    if (typeError != null)
                    {
                        errors.Add(typeError);
                    }
                }
            }
        }

        return errors;
    }

    private static object? ConvertParameterValue(string value, string typeString)
    {
        return typeString.ToLowerInvariant() switch
        {
            "integer" or "int" => int.TryParse(value, out var i) ? i : value,
            "decimal" => decimal.TryParse(value, out var d) ? d : value,
            "boolean" or "bool" => bool.TryParse(value, out var b) ? b : value,
            "uuid" or "guid" => Guid.TryParse(value, out var g) ? g : value,
            "date" => DateOnly.TryParse(value, out var dt) ? dt : value,
            "datetime" or "timestamp" => DateTime.TryParse(value, out var ts) ? ts : value,
            _ => value
        };
    }

    /// <summary>
    /// Find the insertion point for a WHERE clause in a SQL string that has no existing WHERE.
    /// Returns index before ORDER BY, LIMIT, OFFSET, or end of string.
    /// </summary>
    private static int FindWhereInsertionPoint(string sql)
    {
        var keywords = new[] { " ORDER BY ", " LIMIT ", " OFFSET " };
        foreach (var keyword in keywords)
        {
            var idx = sql.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return idx;
        }
        return sql.Length;
    }
}
