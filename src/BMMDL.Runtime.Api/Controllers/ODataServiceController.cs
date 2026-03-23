namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// OData v4 controller for unbound service operations.
/// Routes: /api/odata/{serviceName}/{operationName}
/// Implements ActionImport and FunctionImport routing per OData v4 spec.
/// </summary>
[ApiController]
[Authorize]
[Route("api/odata/services/{serviceName}")]
[Tags("OData Services")]
public class ODataServiceController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IRuleEngine _ruleEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ODataServiceController> _logger;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public ODataServiceController(
        MetaModelCacheManager cacheManager,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<ODataServiceController> logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ======================================================================
    // OData v4 Unbound Actions (ActionImport)
    // POST /api/odata/{serviceName}/{actionName}
    // ======================================================================

    /// <summary>
    /// Invoke an unbound action (ActionImport) on a service.
    /// This is for service-level operations not bound to a specific entity.
    /// Supports overloading: when multiple actions share the same name,
    /// the best match is selected by comparing provided parameter names.
    /// </summary>
    [HttpPost("{actionName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InvokeUnboundAction(
        [FromRoute] string serviceName,
        [FromRoute] string actionName,
        [FromBody] Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var service = await GetServiceDefinitionAsync(serviceName);

        // H1: Null-check parameters before passing to overload resolution
        parameters ??= new Dictionary<string, object?>();

        // Find the action — support overloading by parameter names
        var action = ResolveActionOverload(service, actionName, parameters);

        if (action == null)
        {
            _logger.LogWarning("Unbound action not found: {Service}.{Action}", serviceName, actionName);
            return NotFound(ODataErrorResponse.FromException(
                "ACTION_NOT_FOUND",
                $"Action '{actionName}' not found in service '{serviceName}'",
                $"{serviceName}.{actionName}"));
        }

        _logger.LogInformation("Invoking unbound action {Service}.{Action} for tenant {TenantId}",
            serviceName, actionName, tenantId);

        // Validate required parameters
        var validationErrors = ValidateParameters(action.Parameters, parameters);
        if (validationErrors.Count > 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "VALIDATION_ERROR",
                string.Join("; ", validationErrors)));
        }

        // Execute action body using rule engine
        var evalContext = CreateEvaluationContext();
        evalContext.Parameters = parameters ?? new Dictionary<string, object?>();
        evalContext.ServiceName = serviceName;

        if (action.Body.Count == 0)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, ODataErrorResponse.FromException(
                "NOT_IMPLEMENTED",
                $"Action '{actionName}' in service '{serviceName}' has no implementation body",
                $"{serviceName}.{actionName}"));
        }

        await _unitOfWork.BeginAsync(ct);
        try
        {
            // Evaluate preconditions (REQUIRES clauses) before body execution
            if (action.Preconditions.Count > 0)
            {
                var evaluator = new RuntimeExpressionEvaluator();
                foreach (var precondition in action.Preconditions)
                {
                    var conditionResult = evaluator.Evaluate(precondition, evalContext);
                    if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                    {
                        await _unitOfWork.RollbackAsync(ct);
                        return BadRequest(ODataErrorResponse.FromException(
                            "PRECONDITION_FAILED",
                            $"Precondition failed for action '{actionName}': requires condition not satisfied",
                            $"{serviceName}.{actionName}"));
                    }
                }
                _logger.LogDebug("All {Count} preconditions passed for unbound action {Action}",
                    action.Preconditions.Count, actionName);
            }

            object? result = await _ruleEngine.ExecuteStatementsAsync(action.Body, evalContext, ct);

            // Evaluate modifies clause — apply modification expressions to parameters
            if (action.Modifies.Count > 0)
            {
                var evaluator = new RuntimeExpressionEvaluator();
                foreach (var (fieldName, expression) in action.Modifies)
                {
                    var modifiedValue = evaluator.Evaluate(expression, evalContext);
                    evalContext.Parameters[fieldName] = modifiedValue;
                    _logger.LogDebug("Applied modifies clause: {Field} = {Value}", fieldName, modifiedValue);
                }
            }

            // Evaluate postconditions (ENSURES clauses) after body + modifies
            if (action.Postconditions.Count > 0)
            {
                var evaluator = new RuntimeExpressionEvaluator();
                foreach (var postcondition in action.Postconditions)
                {
                    var conditionResult = evaluator.Evaluate(postcondition, evalContext);
                    if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                    {
                        await _unitOfWork.RollbackAsync(ct);
                        return BadRequest(ODataErrorResponse.FromException(
                            "POSTCONDITION_FAILED",
                            $"Postcondition failed for action '{actionName}': ensures condition not satisfied",
                            $"{serviceName}.{actionName}"));
                    }
                }
                _logger.LogDebug("All {Count} postconditions passed for unbound action {Action}",
                    action.Postconditions.Count, actionName);
            }

            // Enqueue events if action defines them (dispatched post-commit)
            foreach (var eventName in action.Emits)
            {
                var eventPayload = new Dictionary<string, object?>
                {
                    ["ActionName"] = actionName,
                    ["ServiceName"] = serviceName,
                    ["TenantId"] = tenantId,
                    ["Result"] = result,
                    ["Timestamp"] = DateTime.UtcNow
                };

                // Merge result into payload if it's a dictionary
                if (result is Dictionary<string, object?> resultDict)
                {
                    foreach (var kvp in resultDict)
                    {
                        eventPayload[kvp.Key] = kvp.Value;
                    }
                }

                _logger.LogInformation("Enqueuing event {EventName} from unbound action {Action}",
                    eventName, actionName);

                _unitOfWork.EnqueueEvent(new DomainEvent
                {
                    EventName = eventName,
                    EntityName = serviceName,
                    TenantId = tenantId,
                    Payload = eventPayload
                });
            }

            await _unitOfWork.CommitAsync(ct);

            // H3: Return OData v4 compliant response format
            if (result == null)
            {
                return NoContent();
            }
            if (result is Dictionary<string, object?> entityResult)
            {
                return Ok(entityResult);
            }
            // Primitive or other result — wrap in { "value": ... }
            return Ok(new Dictionary<string, object?> { ["value"] = result });
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    // ======================================================================
    // OData v4 Unbound Functions (FunctionImport)
    // GET /api/odata/{serviceName}/{functionCall}
    // Supports: FunctionName() and FunctionName(param1=value1,param2=value2)
    // ======================================================================

    /// <summary>
    /// Invoke an unbound function (FunctionImport) on a service.
    /// Functions are read-only operations with no side effects.
    /// OData v4 allows inline parameters: FunctionName(param1=value1,param2=value2)
    /// Inline parameters take precedence over query string parameters.
    /// Supports overloading: when multiple functions share the same name,
    /// the best match is selected by comparing provided parameter names.
    /// </summary>
    [HttpGet("{**functionCall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InvokeUnboundFunction(
        [FromRoute] string serviceName,
        [FromRoute] string functionCall,
        [FromQuery] Dictionary<string, string>? parameters = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var service = await GetServiceDefinitionAsync(serviceName);

        // Parse function name and inline parameters from the URL segment
        var (functionName, inlineParams) = ParseFunctionCall(functionCall);

        // Merge query string parameters with inline parameters (inline takes precedence)
        var mergedParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parameters != null)
        {
            foreach (var kvp in parameters)
                mergedParams[kvp.Key] = kvp.Value;
        }
        foreach (var kvp in inlineParams)
            mergedParams[kvp.Key] = kvp.Value;

        // Find the function — support overloading by parameter names
        var function = ResolveFunctionOverload(service, functionName, mergedParams);

        if (function == null)
        {
            _logger.LogWarning("Unbound function not found: {Service}.{Function}", serviceName, functionName);
            return NotFound(ODataErrorResponse.FromException(
                "FUNCTION_NOT_FOUND",
                $"Function '{functionName}' not found in service '{serviceName}'",
                $"{serviceName}.{functionName}"));
        }

        _logger.LogInformation("Invoking unbound function {Service}.{Function} for tenant {TenantId}",
            serviceName, functionName, tenantId);

        // Convert merged parameters to typed values
        var typedParams = ConvertParameters(function.Parameters, mergedParams);

        // Execute function body using rule engine
        var evalContext = CreateEvaluationContext();
        evalContext.Parameters = typedParams;
        evalContext.ServiceName = serviceName;

        if (function.Body.Count == 0)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, ODataErrorResponse.FromException(
                "NOT_IMPLEMENTED",
                $"Function '{functionName}' in service '{serviceName}' has no implementation body",
                $"{serviceName}.{functionName}"));
        }

        object? result = await _ruleEngine.ExecuteStatementsAsync(function.Body, evalContext, ct);

        // M3: If composable, apply OData query options ($filter, $select, $orderby, $top, $skip) to the result
        if (function.IsComposable && result is List<Dictionary<string, object?>> composableCollection)
        {
            result = ApplyComposableQueryOptions(composableCollection, Request.Query);
        }

        // H3: Return OData v4 compliant response format
        if (result == null)
        {
            return NoContent();
        }
        if (result is Dictionary<string, object?> entityResult)
        {
            return Ok(entityResult);
        }
        if (result is List<Dictionary<string, object?>> collectionResult)
        {
            return Ok(new ODataCollectionResponse<Dictionary<string, object?>>
            {
                Value = collectionResult
            });
        }
        return Ok(new Dictionary<string, object?> { ["value"] = result });
    }

    // ======================================================================
    // Helper Methods
    // ======================================================================

    /// <summary>
    /// Get service definition from cache, throw if not found.
    /// </summary>
    private async Task<BmService> GetServiceDefinitionAsync(string serviceName)
    {
        // Try exact match first, then case-insensitive
        var cache = await GetCacheAsync();
        var service = cache.GetService(serviceName)
            ?? cache.Services.FirstOrDefault(s =>
                s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        if (service == null)
        {
            _logger.LogWarning("Service not found: {ServiceName}", serviceName);
            throw new EntityNotFoundException(serviceName);
        }

        return service;
    }

    /// <summary>
    /// Convert string parameters to typed values based on parameter definitions.
    /// </summary>
    private Dictionary<string, object?> ConvertParameters(
        IEnumerable<BmParameter> paramDefs,
        Dictionary<string, string>? rawParams)
    {
        var result = new Dictionary<string, object?>();
        if (rawParams == null) return result;

        foreach (var param in paramDefs)
        {
            if (rawParams.TryGetValue(param.Name, out var rawValue))
            {
                result[param.Name] = ConvertValue(rawValue, param.Type);
            }
        }

        return result;
    }

    /// <summary>
    /// Validate that required parameters are provided and values match declared types.
    /// </summary>
    private List<string> ValidateParameters(
        IEnumerable<BmParameter> paramDefs,
        Dictionary<string, object?>? providedParams,
        EvaluationContext? evalContext = null)
    {
        var errors = new List<string>();
        providedParams ??= new Dictionary<string, object?>();

        foreach (var param in paramDefs)
        {
            if (!providedParams.ContainsKey(param.Name))
            {
                if (param.IsOptional && param.DefaultValueAst != null)
                {
                    // Fill in the default value
                    var evaluator = new RuntimeExpressionEvaluator();
                    var defaultVal = evaluator.Evaluate(param.DefaultValueAst, evalContext ?? new EvaluationContext());
                    providedParams[param.Name] = defaultVal;
                }
                else
                {
                    errors.Add($"Missing required parameter: {param.Name}");
                }
            }
            else
            {
                // M13: Type validation for provided parameters
                var value = providedParams[param.Name];
                if (value != null)
                {
                    var typeError = ValidateParameterType(param.Name, param.Type, value);
                    if (typeError != null)
                    {
                        errors.Add(typeError);
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Validate that a parameter value matches the declared type.
    /// Delegates to shared ParameterTypeValidator.
    /// </summary>
    internal static string? ValidateParameterType(string paramName, string declaredType, object value)
        => ParameterTypeValidator.ValidateParameterType(paramName, declaredType, value);

    /// <summary>
    /// Convert a string value to the specified type.
    /// </summary>
    private object? ConvertValue(string value, string typeName)
    {
        return typeName.ToUpperInvariant() switch
        {
            "UUID" or "GUID" => Guid.TryParse(value, out var g) ? g : null,
            "INT" or "INTEGER" => int.TryParse(value, out var i) ? i : null,
            "LONG" or "BIGINT" => long.TryParse(value, out var l) ? l : null,
            "DECIMAL" or "MONEY" => decimal.TryParse(value, out var d) ? d : null,
            "BOOL" or "BOOLEAN" => bool.TryParse(value, out var b) ? b : null,
            "DATE" or "DATETIME" or "TIMESTAMP" => DateTime.TryParse(value, out var dt) ? dt : null,
            _ => value // String and other types
        };
    }

    /// <summary>
    /// Create an EvaluationContext from the current HTTP context.
    /// </summary>
    private EvaluationContext CreateEvaluationContext()
    {
        var tenantId = HttpContext.GetTenantId();
        var runtimeUserContext = HttpContext.GetUserContext();

        return new EvaluationContext
        {
            TenantId = tenantId,
            User = runtimeUserContext != null ? new UserContext
            {
                Id = runtimeUserContext.UserId,
                Username = runtimeUserContext.Username,
                Email = runtimeUserContext.Email,
                TenantId = runtimeUserContext.TenantId,
                Roles = runtimeUserContext.Roles.ToList()
            } : null
        };
    }

    /// <summary>
    /// Parse OData function call syntax: FunctionName(param1=value1,param2=value2)
    /// Reuses the same pattern as EntityActionController.ParseFunctionCall.
    /// </summary>
    public static (string functionName, Dictionary<string, string> parameters) ParseFunctionCall(string functionCall)
    {
        var openParen = functionCall.IndexOf('(');
        if (openParen < 0)
        {
            return (functionCall, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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
    /// Resolve action overload: when multiple actions share the same name,
    /// pick the one whose parameter names best match the provided parameters.
    /// Falls back to first match when only one candidate exists.
    /// </summary>
    public static BmAction? ResolveActionOverload(
        BmService service,
        string actionName,
        Dictionary<string, object?>? providedParams)
    {
        var candidates = service.Actions
            .Where(a => a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        return PickBestOverload(candidates, providedParams?.Keys);
    }

    /// <summary>
    /// Resolve function overload: when multiple functions share the same name,
    /// pick the one whose parameter names best match the provided parameters.
    /// Falls back to first match when only one candidate exists.
    /// </summary>
    public static BmFunction? ResolveFunctionOverload(
        BmService service,
        string functionName,
        Dictionary<string, string>? providedParams)
    {
        var candidates = service.Functions
            .Where(f => f.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        return PickBestOverload(candidates, providedParams?.Keys);
    }

    /// <summary>
    /// Picks the best overload from candidates by matching provided parameter names.
    /// Score = number of declared params matched by provided params.
    /// Tie-break: fewest unmatched declared params (closest arity).
    /// </summary>
    private static T? PickBestOverload<T>(List<T> candidates, IEnumerable<string>? providedKeys) where T : BmFunction
    {
        var keys = new HashSet<string>(providedKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        T? best = default;
        int bestScore = -1;
        int bestUnmatched = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var declaredNames = candidate.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int matched = keys.Count(k => declaredNames.Contains(k));
            int unmatched = declaredNames.Count - matched;

            if (matched > bestScore || (matched == bestScore && unmatched < bestUnmatched))
            {
                best = candidate;
                bestScore = matched;
                bestUnmatched = unmatched;
            }
        }

        return best;
    }

    /// <summary>
    /// Apply OData query options ($filter, $select, $orderby, $top, $skip)
    /// to an in-memory collection returned by a composable function.
    /// </summary>
    internal static List<Dictionary<string, object?>> ApplyComposableQueryOptions(
        List<Dictionary<string, object?>> items,
        IQueryCollection query)
    {
        IEnumerable<Dictionary<string, object?>> result = items;

        // $filter: simple in-memory property comparison
        var filterValue = query.ContainsKey("$filter") ? query["$filter"].ToString() : null;
        if (!string.IsNullOrWhiteSpace(filterValue))
        {
            result = ApplyInMemoryFilter(result, filterValue);
        }

        // $orderby: sort
        var orderByValue = query.ContainsKey("$orderby") ? query["$orderby"].ToString() : null;
        if (!string.IsNullOrWhiteSpace(orderByValue))
        {
            result = ApplyInMemoryOrderBy(result, orderByValue);
        }

        // $skip
        var skipValue = query.ContainsKey("$skip") ? query["$skip"].ToString() : null;
        if (int.TryParse(skipValue, out var skip) && skip > 0)
        {
            result = result.Skip(skip);
        }

        // $top (H3: cap at 10000 to prevent unbounded results)
        var topValue = query.ContainsKey("$top") ? query["$top"].ToString() : null;
        if (int.TryParse(topValue, out var top) && top > 0)
        {
            result = result.Take(Math.Min(top, 10000));
        }

        var materialized = result.ToList();

        // $select: project only requested fields
        var selectValue = query.ContainsKey("$select") ? query["$select"].ToString() : null;
        if (!string.IsNullOrWhiteSpace(selectValue))
        {
            var selectedFields = selectValue.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            materialized = materialized.Select(item =>
            {
                var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in item)
                {
                    if (selectedFields.Contains(kvp.Key))
                        projected[kvp.Key] = kvp.Value;
                }
                return projected;
            }).ToList();
        }

        return materialized;
    }

    /// <summary>
    /// Apply simple OData filter expressions to an in-memory collection.
    /// Supports: eq, ne, gt, ge, lt, le operators with 'and'/'or' connectives.
    /// </summary>
    internal static IEnumerable<Dictionary<string, object?>> ApplyInMemoryFilter(
        IEnumerable<Dictionary<string, object?>> items,
        string filter)
    {
        // Split by ' and ' (simple approach for common cases)
        // Supports: field eq 'value', field gt 123, etc.
        var conditions = SplitFilterConditions(filter);
        return items.Where(item => EvaluateFilterConditions(item, conditions));
    }

    private static List<(string Field, string Op, string Value)> SplitFilterConditions(string filter)
    {
        var conditions = new List<(string, string, string)>();
        // Split on ' and ' (case-insensitive)
        var parts = System.Text.RegularExpressions.Regex.Split(filter, @"\s+and\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            // Match: fieldName op value
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var field = match.Groups[1].Value;
                var op = match.Groups[2].Value.ToLowerInvariant();
                var value = match.Groups[3].Value.Trim().Trim('\'');
                conditions.Add((field, op, value));
            }
        }

        return conditions;
    }

    private static bool EvaluateFilterConditions(Dictionary<string, object?> item, List<(string Field, string Op, string Value)> conditions)
    {
        foreach (var (field, op, filterValue) in conditions)
        {
            // Find the field case-insensitively
            var kvp = item.FirstOrDefault(k => k.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
            var actualValue = kvp.Value;

            if (!CompareValues(actualValue, op, filterValue))
                return false;
        }
        return true;
    }

    private static bool CompareValues(object? actual, string op, string filterValue)
    {
        if (actual == null)
        {
            return op == "eq" && filterValue.Equals("null", StringComparison.OrdinalIgnoreCase);
        }

        var actualStr = actual.ToString() ?? "";

        // Try numeric comparison first
        if (decimal.TryParse(actualStr, out var actualNum) && decimal.TryParse(filterValue, out var filterNum))
        {
            return op switch
            {
                "eq" => actualNum == filterNum,
                "ne" => actualNum != filterNum,
                "gt" => actualNum > filterNum,
                "ge" => actualNum >= filterNum,
                "lt" => actualNum < filterNum,
                "le" => actualNum <= filterNum,
                _ => false
            };
        }

        // String comparison (case-insensitive)
        var cmp = string.Compare(actualStr, filterValue, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "eq" => cmp == 0,
            "ne" => cmp != 0,
            "gt" => cmp > 0,
            "ge" => cmp >= 0,
            "lt" => cmp < 0,
            "le" => cmp <= 0,
            _ => false
        };
    }

    /// <summary>
    /// Apply $orderby to an in-memory collection.
    /// Supports: field asc, field desc, and multiple comma-separated clauses.
    /// </summary>
    internal static IEnumerable<Dictionary<string, object?>> ApplyInMemoryOrderBy(
        IEnumerable<Dictionary<string, object?>> items,
        string orderBy)
    {
        var clauses = orderBy.Split(',').Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
        if (clauses.Count == 0) return items;

        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

        foreach (var clause in clauses)
        {
            var parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue; // H2: skip empty orderby clauses
            var field = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            Func<Dictionary<string, object?>, object?> selector = item =>
            {
                var kvp = item.FirstOrDefault(k => k.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
                return kvp.Value;
            };

            if (ordered == null)
            {
                ordered = descending
                    ? items.OrderByDescending(selector, NullSafeComparer.Instance)
                    : items.OrderBy(selector, NullSafeComparer.Instance);
            }
            else
            {
                ordered = descending
                    ? ordered.ThenByDescending(selector, NullSafeComparer.Instance)
                    : ordered.ThenBy(selector, NullSafeComparer.Instance);
            }
        }

        return ordered ?? items;
    }

    /// <summary>
    /// Null-safe comparer for in-memory sorting.
    /// </summary>
    private class NullSafeComparer : IComparer<object?>
    {
        public static readonly NullSafeComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            if (x is IComparable cx) return cx.CompareTo(y);
            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

}
