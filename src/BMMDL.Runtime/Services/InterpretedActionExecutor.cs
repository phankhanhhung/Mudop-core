namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using Microsoft.Extensions.Logging;

/// <summary>
/// Executes actions/functions via C# interpretation.
/// Uses RuntimeExpressionEvaluator for expression evaluation.
/// This is the fallback when database functions are not deployed.
/// Delegates statement execution to <see cref="StatementExecutor"/>.
/// </summary>
public class InterpretedActionExecutor : IActionExecutor
{
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly StatementExecutor _statementExecutor;
    private readonly ILogger<InterpretedActionExecutor> _logger;

    public InterpretedActionExecutor(
        IRuntimeExpressionEvaluator evaluator,
        ILogger<InterpretedActionExecutor> logger,
        IMetaModelCache? cache = null,
        IUnitOfWork? unitOfWork = null,
        IEventPublisher? eventPublisher = null)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        IMetaModelCache effectiveCache = cache ?? new MetaModelCache();
        var callTargetResolver = new CallTargetResolver(effectiveCache);
        _statementExecutor = new StatementExecutor(evaluator, effectiveCache, logger, callTargetResolver, FailFastPolicy.Instance, unitOfWork, eventPublisher);
    }

    public async Task<ActionResult> ExecuteActionAsync(
        BmEntity entity,
        BmAction action,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        var result = new ActionResult();

        _logger.LogInformation("Executing interpreted action {Entity}.{Action} for entity {EntityId}",
            entity.Name, action.Name, entityId);

        try
        {
            // Merge parameters into context
            foreach (var param in parameters)
            {
                context.Parameters[param.Key] = param.Value;
            }

            // Evaluate preconditions (REQUIRES clauses)
            if (action.Preconditions.Count > 0)
            {
                foreach (var precondition in action.Preconditions)
                {
                    ct.ThrowIfCancellationRequested();
                    var conditionResult = _evaluator.Evaluate(precondition, context);
                    if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                    {
                        throw new PreconditionFailedException(
                            $"Precondition failed for action '{action.Name}': condition not satisfied");
                    }
                }
                _logger.LogDebug("All {Count} preconditions passed for action {Action}",
                    action.Preconditions.Count, action.Name);
            }

            // Execute each statement in the body via shared StatementExecutor
            foreach (var statement in action.Body)
            {
                ct.ThrowIfCancellationRequested();
                var stmtResult = await _statementExecutor.ExecuteStatementAsync(statement, context);

                // Translate RuleExecutionResult to ActionResult semantics
                if (stmtResult.Rejected)
                {
                    var msg = stmtResult.Errors.Count > 0 ? stmtResult.Errors[0].Message : "Operation rejected";
                    throw new RejectionException(msg);
                }
                if (!stmtResult.Success)
                {
                    var msg = stmtResult.Errors.Count > 0 ? stmtResult.Errors[0].Message : "Validation failed";
                    throw new ValidationException(msg);
                }

                foreach (var cv in stmtResult.ComputedValues)
                    result.ComputedValues[cv.Key] = cv.Value;

                result.EmittedEvents.AddRange(stmtResult.EmittedEvents);

                if (stmtResult.ShouldReturn)
                {
                    result.Value = stmtResult.ReturnValue;
                    result.HasReturned = true;
                    break;
                }
            }

            // M12: Enforce modifies clause — evaluate each modification expression
            // and apply the result to the entity data in the evaluation context
            if (action.Modifies.Count > 0 && context.EntityData != null)
            {
                foreach (var (fieldName, expression) in action.Modifies)
                {
                    var modifiedValue = _evaluator.Evaluate(expression, context);
                    context.EntityData[fieldName] = modifiedValue;
                    result.ComputedValues[fieldName] = modifiedValue;
                    _logger.LogDebug("Applied modifies clause: {Field} = {Value}", fieldName, modifiedValue);
                }
            }

            // Evaluate postconditions (ENSURES clauses)
            if (action.Postconditions.Count > 0)
            {
                foreach (var postcondition in action.Postconditions)
                {
                    ct.ThrowIfCancellationRequested();
                    var conditionResult = _evaluator.Evaluate(postcondition, context);
                    if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                    {
                        throw new PostconditionFailedException(
                            $"Postcondition failed for action '{action.Name}': ensures condition not satisfied");
                    }
                }
                _logger.LogDebug("All {Count} postconditions passed for action {Action}",
                    action.Postconditions.Count, action.Name);
            }

            result.Success = true;
            _logger.LogInformation("Interpreted action {Action} completed successfully", action.Name);
        }
        catch (RejectionException ex)
        {
            _logger.LogInformation("Action {Action} rejected: {Message}", action.Name, ex.Message);
            result.Success = false;
            result.Rejected = true;
            result.RejectionMessage = ex.Message;
            result.ErrorMessage = ex.Message;
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Action {Action} validation failed: {Message}", action.Name, ex.Message);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        catch (PreconditionFailedException ex)
        {
            _logger.LogWarning("Action {Action} precondition failed: {Message}", action.Name, ex.Message);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        catch (PostconditionFailedException ex)
        {
            _logger.LogWarning("Action {Action} postcondition failed: {Message}", action.Name, ex.Message);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing interpreted action {Action}", action.Name);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<FunctionResult> ExecuteFunctionAsync(
        BmEntity entity,
        BmFunction function,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        var result = new FunctionResult();

        _logger.LogInformation("Executing interpreted function {Entity}.{Function} for entity {EntityId}",
            entity.Name, function.Name, entityId);

        try
        {
            // Merge parameters into context
            foreach (var param in parameters)
            {
                context.Parameters[param.Key] = param.Value;
            }

            object? returnValue = null;

            _logger.LogDebug("Function {Function} has {BodyCount} statements in body",
                function.Name, function.Body.Count);

            // Execute each statement in the body
            foreach (var statement in function.Body)
            {
                ct.ThrowIfCancellationRequested();

                var stmtResult = await _statementExecutor.ExecuteStatementAsync(statement, context);

                foreach (var cv in stmtResult.ComputedValues)
                {
                    context.Parameters[cv.Key] = cv.Value;
                }

                // Handle validate/raise errors
                if (stmtResult.Rejected)
                {
                    var msg = stmtResult.Errors.Count > 0 ? stmtResult.Errors[0].Message : "Operation rejected";
                    throw new RejectionException(msg);
                }
                if (!stmtResult.Success)
                {
                    var msg = stmtResult.Errors.Count > 0 ? stmtResult.Errors[0].Message : "Validation failed";
                    throw new ValidationException(msg);
                }

                if (stmtResult.ShouldReturn)
                {
                    returnValue = stmtResult.ReturnValue;
                    break;
                }
            }

            result.Value = returnValue;
            result.Success = true;
            _logger.LogInformation("Interpreted function {Function} completed with value: {Value}", function.Name, returnValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing interpreted function {Function}", function.Name);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public Task<bool> IsFunctionDeployedAsync(BmEntity entity, string operationName, CancellationToken ct = default)
    {
        // Interpreted executor doesn't use deployed functions
        return Task.FromResult(false);
    }
}

/// <summary>
/// Exception thrown when a validation rule fails.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

public class PreconditionFailedException : Exception
{
    public PreconditionFailedException(string message) : base(message) { }
}

public class PostconditionFailedException : Exception
{
    public PostconditionFailedException(string message) : base(message) { }
}

public class RejectionException : Exception
{
    public RejectionException(string message) : base(message) { }
}
