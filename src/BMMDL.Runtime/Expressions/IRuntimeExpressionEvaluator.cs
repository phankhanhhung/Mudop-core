namespace BMMDL.Runtime.Expressions;

using BMMDL.MetaModel.Expressions;

/// <summary>
/// Interface for runtime expression evaluation of BmExpression AST nodes.
/// </summary>
public interface IRuntimeExpressionEvaluator
{
    /// <summary>
    /// Evaluate an expression with the given context (synchronous).
    /// For expressions that may contain aggregates, use <see cref="EvaluateAsync"/> instead.
    /// </summary>
    object? Evaluate(BmExpression expression, EvaluationContext context);

    /// <summary>
    /// Evaluate an expression with entity data only (synchronous).
    /// </summary>
    object? Evaluate(BmExpression expression, Dictionary<string, object?> entityData);

    /// <summary>
    /// Evaluate an expression with the given context (async).
    /// Required for expressions that may contain aggregate sub-expressions (COUNT, SUM, etc.)
    /// which need async DB access.
    /// </summary>
    Task<object?> EvaluateAsync(BmExpression expression, EvaluationContext context);
}
