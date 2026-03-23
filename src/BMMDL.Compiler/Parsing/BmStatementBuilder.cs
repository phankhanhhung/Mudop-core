using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds rule/action/function statement AST nodes from ANTLR parse tree contexts.
/// Extracted from BmmdlModelBuilder to reduce class size.
/// </summary>
public class BmStatementBuilder
{
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly string? _sourceFile;
    private readonly List<ParseDiagnostic> _diagnostics;

    public BmStatementBuilder(
        BmExpressionBuilder exprBuilder,
        string? sourceFile = null,
        List<ParseDiagnostic>? diagnostics = null)
    {
        _exprBuilder = exprBuilder;
        _sourceFile = sourceFile;
        _diagnostics = diagnostics ?? new List<ParseDiagnostic>();
    }

    /// <summary>
    /// Build a rule body statement from a ruleStmt context.
    /// </summary>
    public BmRuleStatement BuildRuleStmt(BmmdlParser.RuleStmtContext context)
    {
        if (context.validateStmt() != null)
        {
            return BuildValidateStmt(context.validateStmt());
        }
        else if (context.computeStmt() != null)
        {
            return BuildComputeStmt(context.computeStmt());
        }
        else if (context.whenStmt() != null)
        {
            return BuildWhenStmtForRule(context.whenStmt());
        }
        else if (context.callStmt() != null)
        {
            return BuildCallStmt(context.callStmt());
        }
        else if (context.raiseStmt() != null)
        {
            return BuildRaiseStmt(context.raiseStmt());
        }
        else if (context.rejectStmt() != null)
        {
            return BuildRejectStmt(context.rejectStmt());
        }
        else if (context.foreachStmt() != null)
        {
            return BuildForeachStmt(context.foreachStmt());
        }
        else if (context.letStmt() != null)
        {
            return BuildLetStmt(context.letStmt());
        }
        else if (context.emitStmt() != null)
        {
            return BuildEmitStmt(context.emitStmt());
        }

        throw new InvalidOperationException("Unknown rule statement type");
    }

    /// <summary>
    /// Build an action body statement from an actionStmt context.
    /// </summary>
    public BmRuleStatement BuildActionStmt(BmmdlParser.ActionStmtContext context)
    {
        if (context.validateStmt() != null)
        {
            return BuildValidateStmt(context.validateStmt());
        }
        else if (context.computeStmt() != null)
        {
            return BuildComputeStmt(context.computeStmt());
        }
        else if (context.callStmt() != null)
        {
            return BuildCallStmt(context.callStmt());
        }
        else if (context.emitStmt() != null)
        {
            return BuildEmitStmt(context.emitStmt());
        }
        else if (context.foreachStmt() != null)
        {
            return BuildForeachStmt(context.foreachStmt());
        }
        else if (context.returnStmt() != null)
        {
            return BuildReturnStmt(context.returnStmt());
        }
        else if (context.letStmt() != null)
        {
            return BuildLetStmt(context.letStmt());
        }
        else if (context.raiseStmt() != null)
        {
            return BuildRaiseStmt(context.raiseStmt());
        }
        else if (context.rejectStmt() != null)
        {
            return BuildRejectStmt(context.rejectStmt());
        }
        else if (context.whenActionStmt() != null)
        {
            return BuildWhenActionStmt(context.whenActionStmt());
        }

        throw new InvalidOperationException("Unknown action statement type");
    }

    /// <summary>
    /// Build a function body statement from a functionStmt context.
    /// </summary>
    public BmRuleStatement BuildFunctionStmt(BmmdlParser.FunctionStmtContext context)
    {
        if (context.letStmt() != null)
        {
            return BuildLetStmt(context.letStmt());
        }
        else if (context.returnStmt() != null)
        {
            return BuildReturnStmt(context.returnStmt());
        }
        else if (context.whenFuncStmt() != null)
        {
            return BuildWhenFuncStmt(context.whenFuncStmt());
        }
        else if (context.callStmt() != null)
        {
            return BuildCallStmt(context.callStmt());
        }

        throw new InvalidOperationException("Unknown function statement type");
    }

    // ============================================================
    // Individual Statement Builders
    // ============================================================

    private BmValidateStatement BuildValidateStmt(BmmdlParser.ValidateStmtContext s)
    {
        var exprText = s.expression().GetText();
        BmExpression? exprAst = null;
        try { exprAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "ValidateExpr", "Failed to parse validate expression", ex); }

        return new BmValidateStatement
        {
            Expression = exprText,
            ExpressionAst = exprAst,
            Message = s.STRING_LITERAL()?.GetText()?.Trim('\''),
            Severity = ParseSeverity(s.severityLevel())
        };
    }

    private BmComputeStatement BuildComputeStmt(BmmdlParser.ComputeStmtContext s)
    {
        var exprText = s.expression().GetText();
        BmExpression? exprAst = null;
        try { exprAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "ComputeExpr", "Failed to parse compute expression", ex); }

        return new BmComputeStatement
        {
            Target = s.identifierReference().GetText(),
            Expression = exprText,
            ExpressionAst = exprAst
        };
    }

    private BmCallStatement BuildCallStmt(BmmdlParser.CallStmtContext s)
    {
        var call = new BmCallStatement
        {
            Target = s.identifierReference().GetText()
        };
        if (s.argumentList() != null)
        {
            foreach (var arg in s.argumentList().argument())
            {
                try
                {
                    var expr = _exprBuilder.Visit(arg.expression());
                    if (expr != null)
                    {
                        call.Arguments.Add(expr);
                        call.ArgumentLabels.Add(arg.IDENTIFIER()?.GetText());
                    }
                }
                catch (Exception ex)
                {
                    AddWarning(arg.Start.Line, "CallArg", "Failed to parse call argument", ex);
                }
            }
        }
        return call;
    }

    private BmRaiseStatement BuildRaiseStmt(BmmdlParser.RaiseStmtContext s)
    {
        return new BmRaiseStatement
        {
            Message = s.STRING_LITERAL().GetText().Trim('\''),
            Severity = ParseSeverity(s.severityLevel())
        };
    }

    private BmRejectStatement BuildRejectStmt(BmmdlParser.RejectStmtContext s)
    {
        var reject = new BmRejectStatement();
        if (s.expression() != null)
        {
            try { reject.Message = _exprBuilder.Visit(s.expression()); }
            catch (Exception ex) { AddWarning(s.Start.Line, "RejectMsg", "Failed to parse reject message", ex); }
        }
        return reject;
    }

    private BmLetStatement BuildLetStmt(BmmdlParser.LetStmtContext s)
    {
        var exprText = s.expression().GetText();
        BmExpression? exprAst = null;
        try { exprAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "LetExpr", "Failed to parse let expression", ex); }

        return new BmLetStatement
        {
            VariableName = s.IDENTIFIER().GetText(),
            Expression = exprText,
            ExpressionAst = exprAst
        };
    }

    private BmEmitStatement BuildEmitStmt(BmmdlParser.EmitStmtContext s)
    {
        var emit = new BmEmitStatement { EventName = s.identifierReference().GetText() };
        foreach (var field in s.emitField())
        {
            var fieldName = field.IDENTIFIER().GetText();
            BmExpression? fieldExpr = null;
            try { fieldExpr = _exprBuilder.Visit(field.expression()); }
            catch (Exception ex) { AddWarning(field.Start.Line, "EmitField", "Failed to parse emit field", ex); }
            if (fieldExpr != null)
            {
                emit.FieldAssignments[fieldName] = fieldExpr;
            }
        }
        return emit;
    }

    private BmReturnStatement BuildReturnStmt(BmmdlParser.ReturnStmtContext s)
    {
        var exprText = s.expression().GetText();
        BmExpression? exprAst = null;
        try { exprAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "ReturnExpr", "Failed to parse return expression", ex); }

        return new BmReturnStatement { Expression = exprText, ExpressionAst = exprAst };
    }

    // ============================================================
    // When Statement Builders (different grammar rules per context)
    // ============================================================

    private BmWhenStatement BuildWhenStmtForRule(BmmdlParser.WhenStmtContext s)
    {
        var condText = s.expression().GetText();
        BmExpression? condAst = null;
        try { condAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "WhenCondition", "Failed to parse when condition", ex); }

        var when = new BmWhenStatement
        {
            Condition = condText,
            ConditionAst = condAst
        };

        // Split ruleStmt children into then/else blocks using ELSE token position
        var allStmts = s.ruleStmt();
        if (s.ELSE() == null)
        {
            foreach (var stmt in allStmts)
                when.ThenStatements.Add(BuildRuleStmt(stmt));
        }
        else
        {
            var elseTokenIndex = s.ELSE().Symbol.TokenIndex;
            foreach (var stmt in allStmts)
            {
                if (stmt.Start.TokenIndex < elseTokenIndex)
                    when.ThenStatements.Add(BuildRuleStmt(stmt));
                else
                    when.ElseStatements.Add(BuildRuleStmt(stmt));
            }
        }
        return when;
    }

    private BmWhenStatement BuildWhenActionStmt(BmmdlParser.WhenActionStmtContext s)
    {
        var condText = s.expression().GetText();
        BmExpression? condAst = null;
        try { condAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "WhenCondition", "Failed to parse when condition", ex); }

        var when = new BmWhenStatement { Condition = condText, ConditionAst = condAst };
        var allStmts = s.actionStmt();
        if (s.ELSE() == null)
        {
            foreach (var stmt in allStmts) when.ThenStatements.Add(BuildActionStmt(stmt));
        }
        else
        {
            var elseTokenIndex = s.ELSE().Symbol.TokenIndex;
            foreach (var stmt in allStmts)
            {
                if (stmt.Start.TokenIndex < elseTokenIndex)
                    when.ThenStatements.Add(BuildActionStmt(stmt));
                else
                    when.ElseStatements.Add(BuildActionStmt(stmt));
            }
        }
        return when;
    }

    private BmWhenStatement BuildWhenFuncStmt(BmmdlParser.WhenFuncStmtContext s)
    {
        var condText = s.expression().GetText();
        BmExpression? condAst = null;
        try { condAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "WhenCondition", "Failed to parse when condition", ex); }

        var when = new BmWhenStatement { Condition = condText, ConditionAst = condAst };
        var allStmts = s.functionStmt();
        if (s.ELSE() == null)
        {
            foreach (var stmt in allStmts) when.ThenStatements.Add(BuildFunctionStmt(stmt));
        }
        else
        {
            var elseTokenIndex = s.ELSE().Symbol.TokenIndex;
            foreach (var stmt in allStmts)
            {
                if (stmt.Start.TokenIndex < elseTokenIndex)
                    when.ThenStatements.Add(BuildFunctionStmt(stmt));
                else
                    when.ElseStatements.Add(BuildFunctionStmt(stmt));
            }
        }
        return when;
    }

    // ============================================================
    // Foreach Statement Builders
    // ============================================================

    private BmForeachStatement BuildForeachStmt(BmmdlParser.ForeachStmtContext s)
    {
        var collectionText = s.expression().GetText();
        BmExpression? collectionAst = null;
        try { collectionAst = _exprBuilder.Visit(s.expression()); }
        catch (Exception ex) { AddWarning(s.Start.Line, "ForeachCollection", "Failed to parse foreach collection", ex); }

        var foreach_ = new BmForeachStatement
        {
            VariableName = s.IDENTIFIER().GetText(),
            Collection = collectionText,
            CollectionAst = collectionAst
        };
        foreach (var stmt in s.actionStmt())
        {
            foreach_.Body.Add(BuildActionStmt(stmt));
        }
        return foreach_;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static BmSeverity ParseSeverity(BmmdlParser.SeverityLevelContext? context)
    {
        if (context?.ERROR() != null) return BmSeverity.Error;
        if (context?.WARNING() != null) return BmSeverity.Warning;
        if (context?.INFO() != null) return BmSeverity.Info;
        return BmSeverity.Error;
    }

    private void AddWarning(int line, string context, string message)
    {
        _diagnostics.Add(new ParseDiagnostic(
            ParseDiagnosticLevel.Warning,
            _sourceFile ?? "unknown",
            line,
            context,
            message
        ));
    }

    private void AddWarning(int line, string context, string message, Exception ex)
    {
        var fullMessage = $"{message}\nException: {ex}";
        _diagnostics.Add(new ParseDiagnostic(
            ParseDiagnosticLevel.Warning,
            _sourceFile ?? "unknown",
            line,
            context,
            fullMessage
        ));
    }
}
