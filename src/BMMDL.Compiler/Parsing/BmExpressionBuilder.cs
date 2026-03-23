using System.Globalization;
using Antlr4.Runtime.Misc;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Types;
using Microsoft.Extensions.Logging;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds BmExpression AST from ANTLR parse tree.
/// </summary>
public class BmExpressionBuilder : BmmdlParserBaseVisitor<BmExpression>
{
    private readonly string? _sourceFile;
    private readonly BmTypeReferenceBuilder _typeBuilder = new();
    private static readonly Lazy<ILogger> _logger = new(() =>
        CompilerLoggerFactory.CreateLogger(nameof(BmExpressionBuilder)));

    public BmExpressionBuilder(string? sourceFile = null)
    {
        _sourceFile = sourceFile;
    }

    public override BmExpression VisitLiteralExpr([NotNull] BmmdlParser.LiteralExprContext context)
    {
        return VisitLiteral(context.literal());
    }

    private new BmExpression VisitLiteral(BmmdlParser.LiteralContext context)
    {
        var expr = CreateLiteralExpression(context);
        SetLocation(expr, context);
        return expr;
    }

    private BmLiteralExpression CreateLiteralExpression(BmmdlParser.LiteralContext context)
    {
        if (context.STRING_LITERAL() != null)
        {
            var text = context.STRING_LITERAL().GetText();
            if (text.Length < 2)
                return BmLiteralExpression.String("");
            var value = text.Substring(1, text.Length - 2); // Remove quotes
            return BmLiteralExpression.String(value);
        }
        if (context.INTEGER_LITERAL() != null)
        {
            var text = context.INTEGER_LITERAL().GetText();
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                _logger.Value.LogWarning(
                    "Invalid integer literal '{Text}' at {File}:{Line}, defaulting to 0",
                    text, _sourceFile ?? "unknown", context.Start.Line);
                value = 0;
            }
            return BmLiteralExpression.Integer(value);
        }
        if (context.DECIMAL_LITERAL() != null)
        {
            var text = context.DECIMAL_LITERAL().GetText();
            if (!decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
            {
                _logger.Value.LogWarning(
                    "Invalid decimal literal '{Text}' at {File}:{Line}, defaulting to 0",
                    text, _sourceFile ?? "unknown", context.Start.Line);
                value = 0m;
            }
            return BmLiteralExpression.Decimal(value);
        }
        if (context.TRUE() != null)
            return BmLiteralExpression.Boolean(true);
        if (context.FALSE() != null)
            return BmLiteralExpression.Boolean(false);
        if (context.NULL() != null)
            return BmLiteralExpression.Null();
        if (context.HASH() != null && context.IDENTIFIER() != null)
            return BmLiteralExpression.EnumValue(context.IDENTIFIER().GetText());

        return BmLiteralExpression.Null();
    }

    public override BmExpression VisitRefExpr([NotNull] BmmdlParser.RefExprContext context)
    {
        var path = context.identifierReference().IDENTIFIER()
            .Select(id => id.GetText())
            .ToArray();
        var expr = new BmIdentifierExpression(path);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitContextVarExpr([NotNull] BmmdlParser.ContextVarExprContext context)
    {
        var path = context.contextVar().contextVarSegment()
            .Select(seg => seg.GetText())
            .ToArray();
        var expr = new BmContextVariableExpression(path);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitParamRefExpr([NotNull] BmmdlParser.ParamRefExprContext context)
    {
        var name = context.paramRef().IDENTIFIER().GetText();
        var expr = new BmParameterExpression(name);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitParenExpr([NotNull] BmmdlParser.ParenExprContext context)
    {
        var inner = Visit(context.expression());
        var expr = new BmParenExpression(inner);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitSubqueryExpr([NotNull] BmmdlParser.SubqueryExprContext context)
    {
        var selectText = context.selectStatement().GetText();
        var expr = new BmSubqueryExpression(selectText);

        // Parse into AST (best-effort, raw string preserved as fallback)
        try
        {
            var selectBuilder = new BmSelectStatementBuilder(this, _sourceFile);
            expr.ParsedSelect = selectBuilder.Build(context.selectStatement());
        }
        catch (Exception ex)
        {
            _logger.Value.LogWarning(
                "Failed to parse SubqueryExpr AST at {File}:{Line}, falling back to raw string: {Error}",
                _sourceFile ?? "unknown", context.Start.Line, ex.Message);
        }

        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitExistsExpr([NotNull] BmmdlParser.ExistsExprContext context)
    {
        var selectText = context.selectStatement().GetText();
        var expr = new BmExistsExpression(selectText);

        // Parse into AST (best-effort, raw string preserved as fallback)
        try
        {
            var selectBuilder = new BmSelectStatementBuilder(this, _sourceFile);
            expr.ParsedSelect = selectBuilder.Build(context.selectStatement());
        }
        catch (Exception ex)
        {
            _logger.Value.LogWarning(
                "Failed to parse ExistsExpr AST at {File}:{Line}, falling back to raw string: {Error}",
                _sourceFile ?? "unknown", context.Start.Line, ex.Message);
        }

        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitUnaryExpr([NotNull] BmmdlParser.UnaryExprContext context)
    {
        var operand = Visit(context.expression());
        BmUnaryOperator op;

        if (context.NOT() != null)
            op = BmUnaryOperator.Not;
        else if (context.MINUS() != null)
            op = BmUnaryOperator.Negate;
        else
            op = BmUnaryOperator.Plus;

        var expr = new BmUnaryExpression(op, operand);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitMultDivExpr([NotNull] BmmdlParser.MultDivExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));

        BmBinaryOperator op;
        if (context.STAR() != null)
            op = BmBinaryOperator.Multiply;
        else if (context.SLASH() != null)
            op = BmBinaryOperator.Divide;
        else
            op = BmBinaryOperator.Modulo;

        var expr = new BmBinaryExpression(left, op, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitAddSubExpr([NotNull] BmmdlParser.AddSubExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));

        var op = context.PLUS() != null ? BmBinaryOperator.Add : BmBinaryOperator.Subtract;
        var expr = new BmBinaryExpression(left, op, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitConcatExpr([NotNull] BmmdlParser.ConcatExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmBinaryExpression(left, BmBinaryOperator.Concat, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitRelationalExpr([NotNull] BmmdlParser.RelationalExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));

        BmBinaryOperator op;
        if (context.GT() != null)
            op = BmBinaryOperator.GreaterThan;
        else if (context.LT() != null)
            op = BmBinaryOperator.LessThan;
        else if (context.GTE() != null)
            op = BmBinaryOperator.GreaterOrEqual;
        else
            op = BmBinaryOperator.LessOrEqual;

        var expr = new BmBinaryExpression(left, op, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitEqualityExpr([NotNull] BmmdlParser.EqualityExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));

        var op = context.EQ() != null ? BmBinaryOperator.Equal : BmBinaryOperator.NotEqual;
        var expr = new BmBinaryExpression(left, op, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitAndExpr([NotNull] BmmdlParser.AndExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmBinaryExpression(left, BmBinaryOperator.And, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitOrExpr([NotNull] BmmdlParser.OrExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmBinaryExpression(left, BmBinaryOperator.Or, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitIsNullExpr([NotNull] BmmdlParser.IsNullExprContext context)
    {
        var expression = Visit(context.expression());
        var isNot = context.NOT() != null;
        var expr = new BmIsNullExpression(expression, isNot);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitInExpression([NotNull] BmmdlParser.InExpressionContext context)
    {
        var expression = Visit(context.expression());
        var isNot = context.NOT() != null;
        var inExpr = context.inExpr();

        // Check for subquery form: expr IN (SELECT ...)
        if (inExpr.selectStatement() != null)
        {
            var selectText = inExpr.selectStatement().GetText();
            var subquery = new BmSubqueryExpression(selectText);

            // Parse into AST (best-effort, raw string preserved as fallback)
            try
            {
                var selectBuilder = new BmSelectStatementBuilder(this, _sourceFile);
                subquery.ParsedSelect = selectBuilder.Build(inExpr.selectStatement());
            }
            catch (Exception ex)
            {
                _logger.Value.LogWarning(
                    "Failed to parse IN SubqueryExpr AST at {File}:{Line}, falling back to raw string: {Error}",
                    _sourceFile ?? "unknown", context.Start.Line, ex.Message);
            }

            var expr = new BmInExpression(expression, subquery, isNot);
            SetLocation(expr, context);
            return expr;
        }

        // List form: expr IN (value1, value2, ...)
        var values = inExpr.expression()
            .Select(e => Visit(e))
            .ToList();

        var result = new BmInExpression(expression, values, isNot);
        SetLocation(result, context);
        return result;
    }

    public override BmExpression VisitBetweenExpr([NotNull] BmmdlParser.BetweenExprContext context)
    {
        var expression = Visit(context.expression(0));
        var low = Visit(context.expression(1));
        var high = Visit(context.expression(2));
        var isNot = context.NOT() != null;

        var expr = new BmBetweenExpression(expression, low, high, isNot);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitLikeExpr([NotNull] BmmdlParser.LikeExprContext context)
    {
        var expression = Visit(context.expression(0));
        var pattern = Visit(context.expression(1));
        var isNot = context.NOT() != null;

        var expr = new BmLikeExpression(expression, pattern, isNot);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitOverlapsExpr([NotNull] BmmdlParser.OverlapsExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmTemporalBinaryExpression(left, TemporalBinaryOperator.Overlaps, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitContainsExpr([NotNull] BmmdlParser.ContainsExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmTemporalBinaryExpression(left, TemporalBinaryOperator.Contains, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitPrecedesExpr([NotNull] BmmdlParser.PrecedesExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmTemporalBinaryExpression(left, TemporalBinaryOperator.Precedes, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitMeetsExpr([NotNull] BmmdlParser.MeetsExprContext context)
    {
        var left = Visit(context.expression(0));
        var right = Visit(context.expression(1));
        var expr = new BmTemporalBinaryExpression(left, TemporalBinaryOperator.Meets, right);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitTernaryExpr([NotNull] BmmdlParser.TernaryExprContext context)
    {
        var condition = Visit(context.expression(0));
        var thenExpr = Visit(context.expression(1));
        var elseExpr = Visit(context.expression(2));

        var expr = new BmTernaryExpression(condition, thenExpr, elseExpr);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitFunctionCallExpr([NotNull] BmmdlParser.FunctionCallExprContext context)
    {
        var funcCall = context.functionCall();
        string funcName;

        if (funcCall.IDENTIFIER() != null)
            funcName = funcCall.IDENTIFIER().GetText();
        else
            funcName = funcCall.builtInFunc().GetText();

        var arguments = new List<BmExpression>();
        var labels = new List<string?>();
        
        if (funcCall.argumentList()?.argument() != null)
        {
            foreach (var arg in funcCall.argumentList().argument())
            {
                arguments.Add(Visit(arg.expression()));
                labels.Add(arg.IDENTIFIER()?.GetText());
            }
        }

        var expr = new BmFunctionCallExpression(funcName, arguments.ToArray());
        expr.ArgumentLabels = labels;
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitAggregateExpr([NotNull] BmmdlParser.AggregateExprContext context)
    {
        var aggCall = context.aggregateCall();
        var isDistinct = aggCall.DISTINCT() != null;

        BmAggregateFunction func;
        if (aggCall.COUNT() != null) func = BmAggregateFunction.Count;
        else if (aggCall.SUM() != null) func = BmAggregateFunction.Sum;
        else if (aggCall.AVG() != null) func = BmAggregateFunction.Avg;
        else if (aggCall.MIN() != null) func = BmAggregateFunction.Min;
        else if (aggCall.MAX() != null) func = BmAggregateFunction.Max;
        else if (aggCall.STDDEV() != null) func = BmAggregateFunction.StdDev;
        else func = BmAggregateFunction.Variance;

        BmExpression? argument = null;
        BmExpression? whereCondition = null;
        
        // First expression is the aggregate argument
        var expressions = aggCall.expression();
        if (expressions != null && expressions.Length > 0)
            argument = Visit(expressions[0]);
        
        // Second expression (if present) is the WHERE condition
        if (expressions != null && expressions.Length > 1)
            whereCondition = Visit(expressions[1]);

        var expr = new BmAggregateExpression(func, argument, isDistinct, whereCondition);
        SetLocation(expr, context);
        return expr;
    }

    public override BmExpression VisitWindowExpr([NotNull] BmmdlParser.WindowExprContext context)
    {
        var winFuncExpr = context.windowFunctionExpr();
        var winFunc = winFuncExpr.windowFunction();
        var winSpec = winFuncExpr.windowSpec();
        
        var expr = new BmWindowExpression();
        
        // Determine function name and arguments
        if (winFunc.ROW_NUMBER() != null) expr.FunctionName = "ROW_NUMBER";
        else if (winFunc.RANK() != null) expr.FunctionName = "RANK";
        else if (winFunc.DENSE_RANK() != null) expr.FunctionName = "DENSE_RANK";
        else if (winFunc.NTILE() != null) expr.FunctionName = "NTILE";
        else if (winFunc.LAG() != null) expr.FunctionName = "LAG";
        else if (winFunc.LEAD() != null) expr.FunctionName = "LEAD";
        else if (winFunc.FIRST_VALUE() != null) expr.FunctionName = "FIRST_VALUE";
        else if (winFunc.LAST_VALUE() != null) expr.FunctionName = "LAST_VALUE";
        else if (winFunc.SUM() != null) expr.FunctionName = "SUM";
        else if (winFunc.AVG() != null) expr.FunctionName = "AVG";
        else if (winFunc.COUNT() != null) expr.FunctionName = "COUNT";
        else if (winFunc.MIN() != null) expr.FunctionName = "MIN";
        else if (winFunc.MAX() != null) expr.FunctionName = "MAX";
        
        // Parse function arguments
        foreach (var argExpr in winFunc.expression())
        {
            expr.FunctionArguments.Add(Visit(argExpr));
        }
        
        // Parse PARTITION BY
        if (winSpec.PARTITION() != null)
        {
            foreach (var partExpr in winSpec.expression())
            {
                expr.PartitionBy.Add(Visit(partExpr));
            }
        }
        
        // Parse ORDER BY
        if (winSpec.ORDER() != null)
        {
            foreach (var orderCtx in winSpec.orderItem())
            {
                var item = new BmOrderByItem
                {
                    Expression = Visit(orderCtx.expression()),
                    Descending = orderCtx.DESC() != null
                };
                if (orderCtx.NULLS() != null)
                    item.Nulls = orderCtx.FIRST() != null ? NullsPosition.First : NullsPosition.Last;
                expr.OrderBy.Add(item);
            }
        }
        
        // Parse window frame
        if (winSpec.windowFrame() != null)
        {
            var frameCtx = winSpec.windowFrame();
            var frame = new BmWindowFrame
            {
                Type = frameCtx.ROWS() != null ? "ROWS" : "RANGE"
            };
            
            var bounds = frameCtx.windowFrameBound();
            if (bounds != null && bounds.Length > 0)
            {
                frame.Start = ParseFrameBound(bounds[0]);
                if (bounds.Length > 1)
                    frame.End = ParseFrameBound(bounds[1]);
            }

            expr.Frame = frame;
        }
        
        SetLocation(expr, context);
        return expr;
    }
    
    private BmFrameBound ParseFrameBound(BmmdlParser.WindowFrameBoundContext ctx)
    {
        if (ctx.UNBOUNDED() != null && ctx.PRECEDING() != null)
            return new BmFrameBound { BoundType = BmFrameBoundType.UnboundedPreceding };
        if (ctx.UNBOUNDED() != null && ctx.FOLLOWING() != null)
            return new BmFrameBound { BoundType = BmFrameBoundType.UnboundedFollowing };
        if (ctx.CURRENT() != null)
            return new BmFrameBound { BoundType = BmFrameBoundType.CurrentRow };
        if (ctx.PRECEDING() != null)
            return new BmFrameBound { BoundType = BmFrameBoundType.Preceding, Offset = Visit(ctx.expression()) };
        return new BmFrameBound { BoundType = BmFrameBoundType.Following, Offset = Visit(ctx.expression()) };
    }

    public override BmExpression VisitCaseExpression([NotNull] BmmdlParser.CaseExpressionContext context)
    {
        var caseExpr = context.caseExpr();
        var result = new BmCaseExpression();

        // Simple CASE with input expression (CASE expr WHEN...)
        var inputExpr = caseExpr.expression();
        if (inputExpr != null)
        {
            result.InputExpression = Visit(inputExpr);
        }

        // WHEN clauses
        foreach (var whenClause in caseExpr.whenClause())
        {
            var whenExpr = Visit(whenClause.expression(0));
            var thenExpr = Visit(whenClause.expression(1));
            result.WhenClauses.Add((whenExpr, thenExpr));
        }

        // ELSE clause
        if (caseExpr.elseClause() != null)
        {
            result.ElseResult = Visit(caseExpr.elseClause().expression());
        }

        SetLocation(result, context);
        return result;
    }

    public override BmExpression VisitCastExpression([NotNull] BmmdlParser.CastExpressionContext context)
    {
        var castExpr = context.castExpr();
        var expression = Visit(castExpr.expression());
        var targetType = _typeBuilder.Parse(castExpr.typeReference().GetText());

        var expr = new BmCastExpression(expression, targetType);
        SetLocation(expr, context);
        return expr;
    }

    private void SetLocation(BmExpression expr, Antlr4.Runtime.ParserRuleContext context)
    {
        expr.SourceFile = _sourceFile;
        expr.StartLine = context.Start.Line;
        expr.EndLine = context.Stop?.Line ?? context.Start.Line;
    }
}
