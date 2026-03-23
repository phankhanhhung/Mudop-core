namespace BMMDL.Runtime.Rules;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared statement executor for all BMMDL statement types.
/// Handles dispatch of validate, compute, raise, reject, when, call, foreach, let, emit, and return statements.
/// Error handling behavior is controlled by <see cref="IStatementExecutionPolicy"/>.
/// </summary>
public class StatementExecutor : IStatementExecutor
{
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly IMetaModelCache _metaModelCache;
    private readonly ICallTargetResolver _callTargetResolver;
    private readonly IStatementExecutionPolicy _policy;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger _logger;

    // Track active call chains to detect circular calls (per async flow)
    private static readonly AsyncLocal<HashSet<string>?> _activeCallChain = new();

    public StatementExecutor(
        IRuntimeExpressionEvaluator evaluator,
        IMetaModelCache metaModelCache,
        ILogger logger,
        ICallTargetResolver callTargetResolver,
        IStatementExecutionPolicy policy,
        IUnitOfWork? unitOfWork = null,
        IEventPublisher? eventPublisher = null)
    {
        _evaluator = evaluator;
        _metaModelCache = metaModelCache;
        _logger = logger;
        _callTargetResolver = callTargetResolver;
        _policy = policy;
        _unitOfWork = unitOfWork;
        _eventPublisher = eventPublisher;
    }

    /// <inheritdoc />
    public async Task<RuleExecutionResult> ExecuteStatementsAsync(IList<BmRuleStatement> statements, EvaluationContext context)
    {
        var result = new RuleExecutionResult();
        foreach (var statement in statements)
        {
            var stmtResult = await ExecuteStatementAsync(statement, context);
            result.Merge(stmtResult);
            if (result.ShouldReturn || result.Rejected) break;
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<RuleExecutionResult> ExecuteStatementAsync(BmRuleStatement statement, EvaluationContext context)
    {
        var result = new RuleExecutionResult();

        switch (statement)
        {
            case BmValidateStatement validate:
                await ExecuteValidateStatementAsync(validate, context, result);
                break;

            case BmComputeStatement compute:
                await ExecuteComputeStatementAsync(compute, context, result);
                break;

            case BmRaiseStatement raise:
                ExecuteRaiseStatement(raise, context, result);
                break;

            case BmRejectStatement reject:
                await ExecuteRejectStatementAsync(reject, context, result);
                return result;

            case BmWhenStatement whenStmt:
                var whenResult = await ExecuteWhenStatementAsync(whenStmt, context);
                result.Merge(whenResult);
                break;

            case BmCallStatement call:
                var callResult = await ExecuteCallStatementAsync(call, context);
                result.Merge(callResult);
                break;

            case BmForeachStatement foreachStmt:
                var foreachResult = await ExecuteForeachStatementAsync(foreachStmt, context);
                result.Merge(foreachResult);
                break;

            case BmLetStatement letStmt:
                await ExecuteLetStatementAsync(letStmt, context);
                break;

            case BmEmitStatement emit:
                await ExecuteEmitStatementAsync(emit, context);
                result.EmittedEvents.Add(emit.EventName);
                break;

            case BmReturnStatement returnStmt:
                if (returnStmt.ExpressionAst != null)
                {
                    var returnValue = await _evaluator.EvaluateAsync(returnStmt.ExpressionAst, context);
                    result.ReturnValue = returnValue;
                    result.ShouldReturn = true;
                }
                else
                {
                    result.ShouldReturn = true;
                }
                return result;

            default:
                _logger.LogWarning("Unknown statement type: {Type}", statement.GetType().Name);
                break;
        }

        return result;
    }

    private async Task ExecuteValidateStatementAsync(BmValidateStatement validate, EvaluationContext context, RuleExecutionResult result)
    {
        if (validate.ExpressionAst == null)
        {
            _logger.LogWarning("Validate statement has no expression AST, Expression string: {Expression}", validate.Expression);
            return;
        }

        try
        {
            _logger.LogDebug("Evaluating validate expression: {Expression}", validate.Expression);

            var value = await _evaluator.EvaluateAsync(validate.ExpressionAst, context);
            var isValid = TypeConversionHelpers.ConvertToBool(value);

            _logger.LogDebug("Expression result: {Value}, IsValid: {IsValid}", value, isValid);

            if (!isValid)
            {
                var message = validate.Message ?? "Validation failed";
                var severity = MapSeverity(validate.Severity);
                result.AddError("", message, severity);

                _logger.LogDebug("Validation failed: {Message} (severity: {Severity})", message, severity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating validate expression: {Expression}", validate.Expression);
            result.AddError("", $"Validation error: {ex.Message}", BmSeverity.Error);
        }
    }

    private async Task ExecuteComputeStatementAsync(BmComputeStatement compute, EvaluationContext context, RuleExecutionResult result)
    {
        if (compute.ExpressionAst == null)
        {
            _logger.LogWarning("Compute statement for {Target} has no expression AST", compute.Target);
            return;
        }

        try
        {
            var value = await _evaluator.EvaluateAsync(compute.ExpressionAst, context);
            result.SetComputedValue(compute.Target, value);

            if (context.EntityData != null)
            {
                context.EntityData[compute.Target] = value;
            }

            _logger.LogDebug("Computed {Target} = {Value}", compute.Target, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing {Target}", compute.Target);
            result.AddError(compute.Target, $"Computation error: {ex.Message}", BmSeverity.Error);
        }
    }

    private void ExecuteRaiseStatement(BmRaiseStatement raise, EvaluationContext context, RuleExecutionResult result)
    {
        var severity = MapSeverity(raise.Severity);
        var message = raise.Message;

        _logger.LogDebug("Raise statement: {Message} [{Severity}]", message, severity);

        result.AddError("", message, severity);
    }

    private async Task ExecuteRejectStatementAsync(BmRejectStatement reject, EvaluationContext context, RuleExecutionResult result)
    {
        var message = reject.Message != null
            ? (await _evaluator.EvaluateAsync(reject.Message, context))?.ToString() ?? "Operation rejected by business rule"
            : "Operation rejected by business rule";

        _logger.LogDebug("Reject statement: {Message}", message);

        result.AddError("", message, BmSeverity.Error);
        result.Rejected = true;
    }

    private async Task<RuleExecutionResult> ExecuteWhenStatementAsync(BmWhenStatement when, EvaluationContext context)
    {
        var result = new RuleExecutionResult();

        if (when.ConditionAst == null)
        {
            _logger.LogWarning("When statement has no condition AST");
            return result;
        }

        try
        {
            var conditionValue = await _evaluator.EvaluateAsync(when.ConditionAst, context);
            var conditionMet = TypeConversionHelpers.ConvertToBool(conditionValue);

            if (conditionMet)
            {
                _logger.LogDebug("When condition met, executing THEN statements");
                foreach (var stmt in when.ThenStatements)
                {
                    var stmtResult = await ExecuteStatementAsync(stmt, context);
                    result.Merge(stmtResult);
                    if (result.ShouldReturn || result.Rejected) break;
                }
            }
            else if (when.ElseStatements.Count > 0)
            {
                _logger.LogDebug("When condition not met, executing ELSE statements");
                foreach (var stmt in when.ElseStatements)
                {
                    var stmtResult = await ExecuteStatementAsync(stmt, context);
                    result.Merge(stmtResult);
                    if (result.ShouldReturn || result.Rejected) break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating when condition");
            result.AddError("", $"Condition error: {ex.Message}", BmSeverity.Error);
        }

        return result;
    }

    private async Task<RuleExecutionResult> ExecuteCallStatementAsync(BmCallStatement call, EvaluationContext context)
    {
        var result = new RuleExecutionResult();
        _logger.LogInformation("Executing call statement target: {Target} with {ArgCount} arguments", call.Target, call.Arguments.Count);

        // Circular call detection — snapshot the previous chain and create a new one
        // so that exceptions don't leak entries into the parent async flow's chain
        var previousChain = _activeCallChain.Value;
        _activeCallChain.Value = previousChain != null
            ? new HashSet<string>(previousChain, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var callChain = _activeCallChain.Value;
            if (!callChain.Add(call.Target))
            {
                _logger.LogWarning("Circular call detected for target '{Target}', skipping to prevent infinite loop", call.Target);
                result.AddError("", $"Circular call detected for '{call.Target}'", BmSeverity.Error);
                return result;
            }

            BmFunction? targetAction = _callTargetResolver.Resolve(call.Target, context.ServiceName);
            if (targetAction != null)
            {
                var callParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < call.Arguments.Count && i < targetAction.Parameters.Count; i++)
                {
                    var argValue = await _evaluator.EvaluateAsync(call.Arguments[i], context);
                    callParams[targetAction.Parameters[i].Name] = argValue;
                }

                var callContext = context.CreateChild(callParams);

                foreach (var stmt in targetAction.Body)
                {
                    var stmtResult = await ExecuteStatementAsync(stmt, callContext);
                    result.Merge(stmtResult);
                    if (result.ShouldReturn || result.Rejected) break;
                    if (!stmtResult.Success)
                    {
                        _logger.LogWarning("Call to {Target} produced errors: {ErrorCount} error(s)", call.Target, stmtResult.Errors.Count);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Call statement target action not found: {Target}", call.Target);
                result.AddError("", $"Call statement target action not found: {call.Target}", BmSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Always propagate cancellation
        }
        catch (Exception ex) when (_policy.ContinueOnCallError)
        {
            _logger.LogWarning(ex, "Call to '{Target}' failed (resilient mode)", call.Target);
            result.AddError("", $"Call to '{call.Target}' failed: {ex.Message}", BmSeverity.Warning);
        }
        catch (Exception ex) when (!_policy.ContinueOnCallError)
        {
            _logger.LogError(ex, "Call to '{Target}' failed (fail-fast mode)", call.Target);
            throw;
        }
        finally
        {
            _activeCallChain.Value = previousChain; // Restore previous state
        }

        return result;
    }

    private async Task<RuleExecutionResult> ExecuteForeachStatementAsync(BmForeachStatement foreachStmt, EvaluationContext context)
    {
        var result = new RuleExecutionResult();

        if (foreachStmt.CollectionAst == null)
        {
            _logger.LogWarning("Foreach statement has no collection AST");
            return result;
        }

        var collectionValue = await _evaluator.EvaluateAsync(foreachStmt.CollectionAst, context);
        if (collectionValue is System.Collections.IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                var loopContext = context.CreateChild();
                loopContext.Parameters[foreachStmt.VariableName] = item;

                foreach (var stmt in foreachStmt.Body)
                {
                    var stmtResult = await ExecuteStatementAsync(stmt, loopContext);
                    result.Merge(stmtResult);
                    if (result.ShouldReturn || result.Rejected) break;
                }
                if (result.ShouldReturn || result.Rejected) break;
            }
        }
        else
        {
            _logger.LogWarning("Foreach collection did not evaluate to an enumerable: {Collection}", foreachStmt.Collection);
        }

        return result;
    }

    private async Task ExecuteLetStatementAsync(BmLetStatement letStmt, EvaluationContext context)
    {
        if (letStmt.ExpressionAst == null)
        {
            _logger.LogWarning("Let statement has no expression AST for variable: {Variable}", letStmt.VariableName);
            return;
        }

        var value = await _evaluator.EvaluateAsync(letStmt.ExpressionAst, context);
        context.Parameters[letStmt.VariableName] = value;
    }

    private async Task ExecuteEmitStatementAsync(BmEmitStatement emit, EvaluationContext context)
    {
        var payload = new Dictionary<string, object?>();
        foreach (var (fieldName, expression) in emit.FieldAssignments)
        {
            payload[fieldName] = await _evaluator.EvaluateAsync(expression, context);
        }

        var domainEvent = new DomainEvent
        {
            EventName = emit.EventName,
            EntityName = context.EntityName ?? "",
            Payload = payload,
            TenantId = context.TenantId,
            UserId = context.User?.Id,
            CorrelationId = _unitOfWork?.CorrelationId
        };

        var eventDef = _metaModelCache.GetEvent(emit.EventName);
        var isIntegration = eventDef?.IsIntegration == true;

        if (_unitOfWork?.IsStarted == true)
        {
            if (isIntegration)
            {
                _unitOfWork.EnqueueDurableEvent(domainEvent);
                _logger.LogDebug("Emit statement enqueued integration event {EventName} for durable outbox delivery", emit.EventName);
            }
            else
            {
                _unitOfWork.EnqueueEvent(domainEvent);
                _logger.LogDebug("Emit statement enqueued event {EventName} for post-commit dispatch", emit.EventName);
            }
        }
        else if (_eventPublisher != null)
        {
            await _eventPublisher.PublishAsync(domainEvent);
            _logger.LogDebug("Emit statement dispatched event {EventName} directly via EventPublisher", emit.EventName);
        }
        else
        {
            _logger.LogWarning("Emit statement for {EventName} dropped: no UoW or EventPublisher available", emit.EventName);
        }
    }

    internal static BmSeverity MapSeverity(MetaModel.BmSeverity severity)
    {
        return severity switch
        {
            MetaModel.BmSeverity.Error => BmSeverity.Error,
            MetaModel.BmSeverity.Warning => BmSeverity.Warning,
            MetaModel.BmSeverity.Info => BmSeverity.Info,
            _ => BmSeverity.Error
        };
    }
}
