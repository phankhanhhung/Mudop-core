using BMMDL.MetaModel.Service;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Shared parsing logic for action and function definitions.
/// Used by both BmEntityBuilder (bound actions/functions) and BmServiceBuilder (service actions/functions).
/// </summary>
public static class ActionFunctionParsingHelper
{
    public static BmAction ParseAction(
        BmmdlParser.ActionDefContext context,
        BmEntityElementBuilder elemBuilder,
        BmExpressionBuilder exprBuilder,
        BmStatementBuilder stmtBuilder,
        string? sourceFile,
        Action<int, string, string> addWarning,
        Action<int, string, string, Exception> addWarningEx)
    {
        var action = new BmAction
        {
            Name = context.IDENTIFIER().GetText(),
            ReturnType = context.typeReference()?.GetText() ?? "void",
            SourceFile = sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        if (context.parameterList() != null)
        {
            foreach (var param in context.parameterList().parameter())
            {
                var p = elemBuilder.ParseParameter(param);
                action.Parameters.Add(p);
            }
        }

        // Parse action clauses (emits, requires, ensures, modifies)
        foreach (var clause in context.actionClause())
        {
            if (clause.EMITS() != null)
            {
                action.Emits.Add(clause.identifierReference().GetText());
            }
            else if (clause.REQUIRES() != null)
            {
                try
                {
                    var expr = exprBuilder.Visit(clause.expression());
                    action.Preconditions.Add(expr);
                }
                catch (Exception ex) { addWarningEx(clause.Start.Line, "RequiresExpr", "Failed to parse requires expression", ex); }
            }
            else if (clause.ENSURES() != null)
            {
                try
                {
                    var expr = exprBuilder.Visit(clause.expression());
                    action.Postconditions.Add(expr);
                }
                catch (Exception ex) { addWarningEx(clause.Start.Line, "EnsuresExpr", "Failed to parse ensures expression", ex); }
            }
            else if (clause.MODIFIES() != null)
            {
                var fieldName = clause.IDENTIFIER().GetText();
                try
                {
                    var expr = exprBuilder.Visit(clause.expression());
                    action.Modifies.Add((fieldName, expr));
                }
                catch (Exception ex) { addWarningEx(clause.Start.Line, "ModifiesExpr", $"Failed to parse modifies expression for '{fieldName}'", ex); }
            }
        }

        // Parse action body statements
        foreach (var stmt in context.actionStmt())
        {
            action.Body.Add(stmtBuilder.BuildActionStmt(stmt));
        }

        return action;
    }

    public static BmFunction ParseFunction(
        BmmdlParser.FunctionDefContext context,
        BmEntityElementBuilder elemBuilder,
        BmStatementBuilder stmtBuilder,
        string? sourceFile)
    {
        var func = new BmFunction
        {
            Name = context.IDENTIFIER().GetText(),
            ReturnType = context.typeReference().GetText(),
            IsComposable = context.COMPOSABLE() != null,
            SourceFile = sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        if (context.parameterList() != null)
        {
            foreach (var param in context.parameterList().parameter())
            {
                var p = elemBuilder.ParseParameter(param);
                func.Parameters.Add(p);
            }
        }

        // Parse function body statements
        foreach (var stmt in context.functionStmt())
        {
            func.Body.Add(stmtBuilder.BuildFunctionStmt(stmt));
        }

        return func;
    }
}
