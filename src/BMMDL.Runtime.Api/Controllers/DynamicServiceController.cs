namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Dynamic REST controller for invoking service operations.
/// Routes: /api/v1/services/{module}/{serviceName}/{operationName}
/// Requires JWT authentication.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/services/{module}/{serviceName}")]
[Tags("Services")]
public class DynamicServiceController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IRuleEngine _ruleEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DynamicServiceController> _logger;

    // Access cache through manager asynchronously
    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public DynamicServiceController(
        MetaModelCacheManager cacheManager,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<DynamicServiceController> logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// List all services available in a module.
    /// </summary>
    /// <param name="module">Module name.</param>
    /// <returns>List of service definitions.</returns>
    [HttpGet("/api/v1/services/{module}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListServices([FromRoute] string module)
    {
        var cache = await GetCacheAsync();
        var services = cache.Services
            .Where(s => s.Namespace.Equals(module, StringComparison.OrdinalIgnoreCase))
            .Select(s => new
            {
                s.Name,
                s.QualifiedName,
                Functions = s.Functions.Select(f => new { f.Name, f.ReturnType, Parameters = f.Parameters.Select(p => new { p.Name, p.Type }) }),
                Actions = s.Actions.Select(a => new { a.Name, a.ReturnType, a.Emits, Parameters = a.Parameters.Select(p => new { p.Name, p.Type }) })
            })
            .ToList();

        return Ok(new { Value = services });
    }

    /// <summary>
    /// Get service definition.
    /// </summary>
    /// <param name="module">Module name.</param>
    /// <param name="serviceName">Service name.</param>
    /// <returns>Service definition with operations.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetService([FromRoute] string module, [FromRoute] string serviceName)
    {
        var service = await GetServiceDefinitionAsync(module, serviceName);

        return Ok(new
        {
            service.Name,
            service.QualifiedName,
            Functions = service.Functions.Select(f => new
            {
                f.Name,
                f.ReturnType,
                Parameters = f.Parameters.Select(p => new { p.Name, p.Type })
            }),
            Actions = service.Actions.Select(a => new
            {
                a.Name,
                a.ReturnType,
                a.Emits,
                Parameters = a.Parameters.Select(p => new { p.Name, p.Type })
            }),
            EventHandlers = service.EventHandlers.Select(h => h.EventName)
        });
    }

    /// <summary>
    /// Invoke a service function (read-only, no side effects).
    /// </summary>
    /// <param name="module">Module name.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="operationName">Function name.</param>
    /// <param name="parameters">Function parameters as query string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Function result.</returns>
    [HttpGet("{operationName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InvokeFunction(
        [FromRoute] string module,
        [FromRoute] string serviceName,
        [FromRoute] string operationName,
        [FromQuery] Dictionary<string, string>? parameters = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var service = await GetServiceDefinitionAsync(module, serviceName);
        
        // Find the function
        var function = service.Functions.FirstOrDefault(f => 
            f.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase));
        
        if (function == null)
        {
            _logger.LogWarning("Function not found: {Service}.{Operation}", serviceName, operationName);
            return NotFound(ODataErrorResponse.FromException(
                "FUNCTION_NOT_FOUND",
                $"Function '{operationName}' not found in service '{serviceName}'",
                $"{module}.{serviceName}.{operationName}"));
        }

        _logger.LogInformation("Invoking function {Service}.{Operation} for tenant {TenantId}",
            serviceName, operationName, tenantId);

        // Convert query parameters to typed values
        var typedParams = ConvertParameters(function.Parameters, parameters);

        // Execute function body using rule engine
        var evalContext = CreateEvaluationContext();
        evalContext.Parameters = typedParams;
        
        var result = await ExecuteStatementsAsync(function.Body, evalContext, ct);

        return Ok(new { Value = result });
    }

    /// <summary>
    /// Invoke a service action (may have side effects, may emit events).
    /// </summary>
    /// <param name="module">Module name.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="operationName">Action name.</param>
    /// <param name="parameters">Action parameters in request body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Action result.</returns>
    [HttpPost("{operationName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InvokeAction(
        [FromRoute] string module,
        [FromRoute] string serviceName,
        [FromRoute] string operationName,
        [FromBody] Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.GetTenantId();
        var service = await GetServiceDefinitionAsync(module, serviceName);
        
        // Find the action
        var action = service.Actions.FirstOrDefault(a => 
            a.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase));
        
        if (action == null)
        {
            _logger.LogWarning("Action not found: {Service}.{Operation}", serviceName, operationName);
            return NotFound(ODataErrorResponse.FromException(
                "ACTION_NOT_FOUND",
                $"Action '{operationName}' not found in service '{serviceName}'",
                $"{module}.{serviceName}.{operationName}"));
        }

        _logger.LogInformation("Invoking action {Service}.{Operation} for tenant {TenantId}",
            serviceName, operationName, tenantId);

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

        await _unitOfWork.BeginAsync(ct);
        try
        {
            var result = await ExecuteStatementsAsync(action.Body, evalContext, ct);

            // Enqueue events if action defines them (dispatched post-commit)
            foreach (var eventName in action.Emits)
            {
                var eventPayload = new Dictionary<string, object?>
                {
                    ["ActionName"] = operationName,
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

                _logger.LogInformation("Enqueuing event {EventName} from action {Action}",
                    eventName, operationName);

                _unitOfWork.EnqueueEvent(new DomainEvent
                {
                    EventName = eventName,
                    EntityName = serviceName,
                    TenantId = tenantId,
                    Payload = eventPayload
                });
            }

            await _unitOfWork.CommitAsync(ct);

            return Ok(new
            {
                Success = true,
                Value = result,
                EmittedEvents = action.Emits
            });
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Get service definition from cache, throw if not found.
    /// </summary>
    private async Task<BmService> GetServiceDefinitionAsync(string module, string serviceName)
    {
        // Try qualified name first, then simple name
        var cache = await GetCacheAsync();
        var qualifiedName = $"{module}.{serviceName}";
        var service = cache.GetService(qualifiedName) ?? cache.GetService(serviceName);

        if (service == null)
        {
            _logger.LogWarning("Service not found: {QualifiedName}", qualifiedName);
            throw new EntityNotFoundException(qualifiedName);
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
        rawParams ??= new Dictionary<string, string>();

        foreach (var param in paramDefs)
        {
            if (rawParams.TryGetValue(param.Name, out var rawValue))
            {
                result[param.Name] = ConvertValue(rawValue, param.Type);
            }
            else if (param.IsOptional && param.DefaultValueAst != null)
            {
                var evaluator = new RuntimeExpressionEvaluator();
                result[param.Name] = evaluator.Evaluate(param.DefaultValueAst, new EvaluationContext());
            }
        }

        return result;
    }

    /// <summary>
    /// Validate that required parameters are provided.
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
        }

        return errors;
    }

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
    /// Execute a list of rule statements.
    /// </summary>
    private async Task<object?> ExecuteStatementsAsync(
        IEnumerable<BmRuleStatement> statements,
        EvaluationContext context,
        CancellationToken ct)
    {
        var stmtList = statements.ToList();
        
        // For empty bodies, return the parameters as-is (pass-through)
        if (stmtList.Count == 0)
        {
            return context.Parameters;
        }

        // Delegate to RuleEngine for full statement execution
        // (validates, computes, when, call, foreach, let, emit, raise, reject, return)
        return await _ruleEngine.ExecuteStatementsAsync(stmtList, context, ct);
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
}
