namespace BMMDL.Runtime.Rules;

using BMMDL.MetaModel;
using BMMDL.Runtime.Expressions;

/// <summary>
/// Interface for executing BMMDL rule statements.
/// Provides a unified entry point for statement dispatch across rules, actions, and event handlers.
/// </summary>
public interface IStatementExecutor
{
    /// <summary>
    /// Execute a single rule statement and return the result.
    /// </summary>
    Task<RuleExecutionResult> ExecuteStatementAsync(BmRuleStatement statement, EvaluationContext context);

    /// <summary>
    /// Execute a list of statements sequentially, merging results.
    /// Stops early on reject or return.
    /// </summary>
    Task<RuleExecutionResult> ExecuteStatementsAsync(IList<BmRuleStatement> statements, EvaluationContext context);
}
