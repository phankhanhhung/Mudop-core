using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Expressions;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates business rules: target entity, triggers, statements,
/// and checks for unsupported subquery/exists expressions in rule contexts.
/// </summary>
public class RuleValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var rule in model.Rules)
        {
            count++;

            // Must have target entity
            if (string.IsNullOrEmpty(rule.TargetEntity))
            {
                context.AddError(ErrorCodes.SEM_RULE_NO_TARGET, $"Rule '{rule.Name}' has no target entity", rule.SourceFile, rule.StartLine, PassName);
            }

            // Must have at least one trigger
            if (rule.Triggers.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_RULE_NO_TRIGGERS, $"Rule '{rule.Name}' has no triggers", rule.SourceFile, rule.StartLine, PassName);
            }

            // Must have at least one statement
            if (rule.Statements.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_RULE_NO_STATEMENTS, $"Rule '{rule.Name}' has no statements", rule.SourceFile, rule.StartLine, PassName);
            }

            // Validate statements
            count += ValidateRuleStatements(context, rule);
        }

        return count;
    }

    private int ValidateRuleStatements(CompilationContext context, BmRule rule)
    {
        int count = 0;

        foreach (var stmt in rule.Statements)
        {
            count++;
            count += ValidateSingleStatement(context, rule, stmt);
        }

        return count;
    }

    private int ValidateSingleStatement(CompilationContext context, BmRule rule, BmRuleStatement stmt)
    {
        int count = 0;

        // Check all expression ASTs in this statement for unsupported subquery/exists
        CheckStatementForSubqueryExpressions(context, rule, stmt);

        switch (stmt)
        {
            case BmValidateStatement v:
                if (string.IsNullOrEmpty(v.Expression) && v.ExpressionAst == null)
                {
                    context.AddError(ErrorCodes.SEM_VALIDATE_NO_EXPR, $"Validate statement in rule '{rule.Name}' has no expression", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmComputeStatement c:
                if (string.IsNullOrEmpty(c.Target))
                {
                    context.AddError(ErrorCodes.SEM_COMPUTE_NO_TARGET, $"Compute statement in rule '{rule.Name}' has no target", rule.SourceFile, rule.StartLine, PassName);
                }
                if (string.IsNullOrEmpty(c.Expression) && c.ExpressionAst == null)
                {
                    context.AddError(ErrorCodes.SEM_COMPUTE_NO_EXPR, $"Compute statement in rule '{rule.Name}' has no expression", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmWhenStatement w:
                if (string.IsNullOrEmpty(w.Condition) && w.ConditionAst == null)
                {
                    context.AddError(ErrorCodes.SEM_WHEN_NO_CONDITION, $"When statement in rule '{rule.Name}' has no condition", rule.SourceFile, rule.StartLine, PassName);
                }
                // Recursively validate then/else bodies
                if (w.ThenStatements != null)
                {
                    foreach (var inner in w.ThenStatements)
                    {
                        count++;
                        count += ValidateSingleStatement(context, rule, inner);
                    }
                }
                if (w.ElseStatements != null)
                {
                    foreach (var inner in w.ElseStatements)
                    {
                        count++;
                        count += ValidateSingleStatement(context, rule, inner);
                    }
                }
                break;

            case BmCallStatement call:
                if (string.IsNullOrEmpty(call.Target))
                {
                    context.AddError(ErrorCodes.SEM_CALL_NO_TARGET, $"Call statement in rule '{rule.Name}' has no target", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmEmitStatement emit:
                if (string.IsNullOrEmpty(emit.EventName))
                {
                    context.AddError(ErrorCodes.SEM_EMIT_NO_EVENT, $"Emit statement in rule '{rule.Name}' has no event name", rule.SourceFile, rule.StartLine, PassName);
                }
                else if (context.Model != null)
                {
                    // Verify the referenced event exists in the model
                    var eventExists = context.Model.Events.Any(e =>
                        e.Name == emit.EventName ||
                        e.QualifiedName == emit.EventName);
                    if (!eventExists)
                    {
                        context.AddWarning(ErrorCodes.SEM_EMIT_NO_EVENT, $"Emit statement in rule '{rule.Name}' references unknown event '{emit.EventName}'", rule.SourceFile, rule.StartLine, PassName);
                    }
                }
                break;

            case BmReturnStatement ret:
                if (string.IsNullOrEmpty(ret.Expression) && ret.ExpressionAst == null)
                {
                    context.AddWarning(ErrorCodes.SEM_RETURN_NO_EXPR, $"Return statement in rule '{rule.Name}' has no expression", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmLetStatement let:
                if (string.IsNullOrEmpty(let.VariableName))
                {
                    context.AddError(ErrorCodes.SEM_LET_NO_VARIABLE, $"Let statement in rule '{rule.Name}' has no variable name", rule.SourceFile, rule.StartLine, PassName);
                }
                if (string.IsNullOrEmpty(let.Expression) && let.ExpressionAst == null)
                {
                    context.AddError(ErrorCodes.SEM_LET_NO_EXPR, $"Let statement in rule '{rule.Name}' has no expression", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmRejectStatement reject:
                // Reject without a message is valid but worth a warning
                if (reject.Message == null)
                {
                    context.AddWarning(ErrorCodes.SEM_REJECT_NO_MESSAGE, $"Reject statement in rule '{rule.Name}' has no message expression", rule.SourceFile, rule.StartLine, PassName);
                }
                break;

            case BmForeachStatement forEach:
                if (string.IsNullOrEmpty(forEach.VariableName))
                {
                    context.AddError(ErrorCodes.SEM_FOREACH_NO_VARIABLE, $"Foreach statement in rule '{rule.Name}' has no variable name", rule.SourceFile, rule.StartLine, PassName);
                }
                if (string.IsNullOrEmpty(forEach.Collection) && forEach.CollectionAst == null)
                {
                    context.AddError(ErrorCodes.SEM_FOREACH_NO_COLLECTION, $"Foreach statement in rule '{rule.Name}' has no collection", rule.SourceFile, rule.StartLine, PassName);
                }
                if (forEach.Body.Count == 0)
                {
                    context.AddWarning(ErrorCodes.SEM_FOREACH_NO_BODY, $"Foreach statement in rule '{rule.Name}' has an empty body", rule.SourceFile, rule.StartLine, PassName);
                }
                // Recursively validate body statements
                foreach (var inner in forEach.Body)
                {
                    count++;
                    count += ValidateSingleStatement(context, rule, inner);
                }
                break;
        }

        return count;
    }

    private void CheckStatementForSubqueryExpressions(CompilationContext context, BmRule rule, BmRuleStatement stmt)
    {
        // Collect all expression ASTs from the statement
        var expressions = new List<BmExpression>();
        switch (stmt)
        {
            case BmValidateStatement v when v.ExpressionAst != null:
                expressions.Add(v.ExpressionAst);
                break;
            case BmComputeStatement c when c.ExpressionAst != null:
                expressions.Add(c.ExpressionAst);
                break;
            case BmLetStatement l when l.ExpressionAst != null:
                expressions.Add(l.ExpressionAst);
                break;
            case BmWhenStatement w when w.ConditionAst != null:
                expressions.Add(w.ConditionAst);
                break;
            case BmReturnStatement r when r.ExpressionAst != null:
                expressions.Add(r.ExpressionAst);
                break;
        }

        foreach (var expr in expressions)
        {
            if (ContainsSubqueryOrExists(expr))
            {
                context.AddError(ErrorCodes.SEM_SUBQUERY_IN_RULE,
                    $"Subquery/EXISTS expressions are not supported in rule contexts (rule '{rule.Name}')",
                    rule.SourceFile, rule.StartLine, PassName);
            }
        }
    }

    private static bool ContainsSubqueryOrExists(BmExpression expr)
    {
        return BmExpressionWalker.Any(expr, e => e is BmSubqueryExpression or BmExistsExpression);
    }
}
