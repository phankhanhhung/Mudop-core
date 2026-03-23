namespace BMMDL.MetaModel.Expressions;

/// <summary>
/// Generic expression tree walker for BmExpression hierarchies.
/// Provides Walk, Any, and Collect operations that handle ALL expression subtypes.
/// Lives in MetaModel so all projects can use it without compiler dependencies.
/// </summary>
public static class BmExpressionWalker
{
    /// <summary>
    /// Visits every node in the expression tree (pre-order depth-first).
    /// </summary>
    public static void Walk(BmExpression? expr, Action<BmExpression> visitor)
    {
        WalkCore(expr, node => { visitor(node); return true; });
    }

    /// <summary>
    /// Returns true if any node in the expression tree matches the predicate.
    /// Short-circuits on first match.
    /// </summary>
    public static bool Any(BmExpression? expr, Func<BmExpression, bool> predicate)
    {
        bool found = false;
        WalkCore(expr, node =>
        {
            if (predicate(node)) { found = true; return false; }
            return true;
        });
        return found;
    }

    /// <summary>
    /// Collects non-null results from applying the selector to each node.
    /// </summary>
    public static List<T> Collect<T>(BmExpression? expr, Func<BmExpression, T?> selector) where T : class
    {
        var results = new List<T>();
        Walk(expr, node =>
        {
            var result = selector(node);
            if (result != null)
                results.Add(result);
        });
        return results;
    }

    /// <summary>
    /// Core traversal engine. Visitor returns true to continue, false to stop.
    /// Returns false if traversal was stopped early.
    /// </summary>
    private static bool WalkCore(BmExpression? expr, Func<BmExpression, bool> visitor)
    {
        if (expr == null) return true;
        if (!visitor(expr)) return false;

        switch (expr)
        {
            case BmBinaryExpression bin:
                if (!WalkCore(bin.Left, visitor)) return false;
                if (!WalkCore(bin.Right, visitor)) return false;
                break;

            case BmUnaryExpression un:
                if (!WalkCore(un.Operand, visitor)) return false;
                break;

            case BmFunctionCallExpression func:
                foreach (var arg in func.Arguments)
                    if (!WalkCore(arg, visitor)) return false;
                break;

            case BmAggregateExpression aggregate:
                if (!WalkCore(aggregate.Argument, visitor)) return false;
                if (!WalkCore(aggregate.WhereCondition, visitor)) return false;
                break;

            case BmWindowExpression window:
                foreach (var arg in window.FunctionArguments)
                    if (!WalkCore(arg, visitor)) return false;
                foreach (var p in window.PartitionBy)
                    if (!WalkCore(p, visitor)) return false;
                foreach (var o in window.OrderBy)
                    if (!WalkCore(o.Expression, visitor)) return false;
                if (window.Frame?.Start.Offset != null)
                    if (!WalkCore(window.Frame.Start.Offset, visitor)) return false;
                if (window.Frame?.End?.Offset != null)
                    if (!WalkCore(window.Frame.End.Offset, visitor)) return false;
                break;

            case BmCaseExpression caseExpr:
                if (!WalkCore(caseExpr.InputExpression, visitor)) return false;
                foreach (var (when, then) in caseExpr.WhenClauses)
                {
                    if (!WalkCore(when, visitor)) return false;
                    if (!WalkCore(then, visitor)) return false;
                }
                if (!WalkCore(caseExpr.ElseResult, visitor)) return false;
                break;

            case BmCastExpression cast:
                if (!WalkCore(cast.Expression, visitor)) return false;
                break;

            case BmTernaryExpression ternary:
                if (!WalkCore(ternary.Condition, visitor)) return false;
                if (!WalkCore(ternary.ThenExpression, visitor)) return false;
                if (!WalkCore(ternary.ElseExpression, visitor)) return false;
                break;

            case BmInExpression inExpr:
                if (!WalkCore(inExpr.Expression, visitor)) return false;
                foreach (var val in inExpr.Values)
                    if (!WalkCore(val, visitor)) return false;
                if (inExpr.Subquery != null)
                    if (!WalkCore(inExpr.Subquery, visitor)) return false;
                break;

            case BmBetweenExpression between:
                if (!WalkCore(between.Expression, visitor)) return false;
                if (!WalkCore(between.Low, visitor)) return false;
                if (!WalkCore(between.High, visitor)) return false;
                break;

            case BmLikeExpression like:
                if (!WalkCore(like.Expression, visitor)) return false;
                if (!WalkCore(like.Pattern, visitor)) return false;
                break;

            case BmIsNullExpression isNull:
                if (!WalkCore(isNull.Expression, visitor)) return false;
                break;

            case BmParenExpression paren:
                if (!WalkCore(paren.Inner, visitor)) return false;
                break;

            case BmTemporalBinaryExpression temporal:
                if (!WalkCore(temporal.Left, visitor)) return false;
                if (!WalkCore(temporal.Right, visitor)) return false;
                break;

            // Leaf nodes: BmLiteralExpression, BmIdentifierExpression,
            // BmContextVariableExpression, BmParameterExpression,
            // BmSubqueryExpression, BmExistsExpression
            // (subquery/exists internal SELECT ASTs are not BmExpression children)
        }

        return true;
    }
}
