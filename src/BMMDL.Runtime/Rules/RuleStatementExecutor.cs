namespace BMMDL.Runtime.Rules;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Statement executor for business rule statements.
/// Thin wrapper around <see cref="StatementExecutor"/> with fail-fast error policy.
/// Internal class — created by RuleEngine, not registered in DI.
/// </summary>
internal class RuleStatementExecutor
{
    private readonly StatementExecutor _inner;

    internal RuleStatementExecutor(
        IRuntimeExpressionEvaluator evaluator,
        IMetaModelCache metaModelCache,
        ILogger logger,
        IUnitOfWork? unitOfWork = null,
        IEventPublisher? eventPublisher = null)
    {
        var callTargetResolver = new CallTargetResolver(metaModelCache);
        _inner = new StatementExecutor(evaluator, metaModelCache, logger, callTargetResolver, FailFastPolicy.Instance, unitOfWork, eventPublisher);
    }

    internal Task<RuleExecutionResult> ExecuteStatementAsync(BmRuleStatement statement, EvaluationContext context)
        => _inner.ExecuteStatementAsync(statement, context);

    internal static BmSeverity MapSeverity(MetaModel.BmSeverity severity)
        => StatementExecutor.MapSeverity(severity);
}
