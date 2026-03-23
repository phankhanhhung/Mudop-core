namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime.Expressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hybrid action executor that tries database execution first,
/// falling back to interpreted execution if the function is not deployed.
/// </summary>
public class HybridActionExecutor : IActionExecutor
{
    private readonly DatabaseActionExecutor _databaseExecutor;
    private readonly InterpretedActionExecutor _interpretedExecutor;
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly ILogger<HybridActionExecutor> _logger;
    private readonly bool _preferDatabase;

    public HybridActionExecutor(
        DatabaseActionExecutor databaseExecutor,
        InterpretedActionExecutor interpretedExecutor,
        IRuntimeExpressionEvaluator evaluator,
        ILogger<HybridActionExecutor> logger,
        bool preferDatabase = true)
    {
        _databaseExecutor = databaseExecutor ?? throw new ArgumentNullException(nameof(databaseExecutor));
        _interpretedExecutor = interpretedExecutor ?? throw new ArgumentNullException(nameof(interpretedExecutor));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferDatabase = preferDatabase;
    }

    public async Task<ActionResult> ExecuteActionAsync(
        BmEntity entity,
        BmAction action,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        if (_preferDatabase && action.Body.Count > 0)
        {
            // Check if database function is deployed
            var isDeployed = await _databaseExecutor.IsFunctionDeployedAsync(entity, action.Name, ct);
            
            if (isDeployed)
            {
                _logger.LogDebug("Using database execution for action {Entity}.{Action}",
                    entity.Name, action.Name);

                // Evaluate preconditions before database execution
                var preError = EvaluatePreconditions(action, context);
                if (preError != null)
                    return new ActionResult { Success = false, ErrorMessage = preError };

                var result = await _databaseExecutor.ExecuteActionAsync(entity, action, entityId, parameters, context, ct);

                // Evaluate postconditions after database execution
                if (result.Success)
                {
                    var postError = EvaluatePostconditions(action, context);
                    if (postError != null)
                        return new ActionResult { Success = false, ErrorMessage = postError };
                }

                return result;
            }
            
            _logger.LogDebug("Database function not deployed, falling back to interpreted execution for {Entity}.{Action}",
                entity.Name, action.Name);
        }

        // InterpretedActionExecutor already enforces contracts
        return await _interpretedExecutor.ExecuteActionAsync(entity, action, entityId, parameters, context, ct);
    }

    public async Task<FunctionResult> ExecuteFunctionAsync(
        BmEntity entity,
        BmFunction function,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        if (_preferDatabase && function.Body.Count > 0)
        {
            var isDeployed = await _databaseExecutor.IsFunctionDeployedAsync(entity, function.Name, ct);
            
            if (isDeployed)
            {
                _logger.LogDebug("Using database execution for function {Entity}.{Function}",
                    entity.Name, function.Name);
                return await _databaseExecutor.ExecuteFunctionAsync(entity, function, entityId, parameters, context, ct);
            }
            
            _logger.LogDebug("Database function not deployed, falling back to interpreted execution for {Entity}.{Function}",
                entity.Name, function.Name);
        }

        return await _interpretedExecutor.ExecuteFunctionAsync(entity, function, entityId, parameters, context, ct);
    }

    public Task<bool> IsFunctionDeployedAsync(BmEntity entity, string operationName, CancellationToken ct = default)
    {
        return _databaseExecutor.IsFunctionDeployedAsync(entity, operationName, ct);
    }

    private string? EvaluatePreconditions(BmAction action, EvaluationContext context)
    {
        foreach (var precondition in action.Preconditions)
        {
            var result = _evaluator.Evaluate(precondition, context);
            if (!TypeConversionHelpers.ConvertToBool(result))
                return $"Precondition failed for action '{action.Name}': condition not satisfied";
        }
        return null;
    }

    private string? EvaluatePostconditions(BmAction action, EvaluationContext context)
    {
        foreach (var postcondition in action.Postconditions)
        {
            var result = _evaluator.Evaluate(postcondition, context);
            if (!TypeConversionHelpers.ConvertToBool(result))
                return $"Postcondition failed for action '{action.Name}': ensures condition not satisfied";
        }
        return null;
    }
}
