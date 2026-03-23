using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds access control AST nodes from ANTLR parse tree contexts.
/// Extracted from BmmdlModelBuilder to reduce class size.
/// </summary>
public class BmAccessControlBuilder
{
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly string? _sourceFile;
    private readonly List<ParseDiagnostic> _diagnostics;

    public BmAccessControlBuilder(
        BmExpressionBuilder exprBuilder,
        string? sourceFile = null,
        List<ParseDiagnostic>? diagnostics = null)
    {
        _exprBuilder = exprBuilder;
        _sourceFile = sourceFile;
        _diagnostics = diagnostics ?? new List<ParseDiagnostic>();
    }

    /// <summary>
    /// Build a BmAccessControl from an accessControlDef context.
    /// </summary>
    public BmAccessControl Build(BmmdlParser.AccessControlDefContext context)
    {
        var refs = context.identifierReference();
        // FOR identifierReference is optional (omitted inside aspects)
        var targetName = refs.Length > 0 ? refs[0].GetText() : "";

        // EXTENDS identifierReference is the second ref (if FOR is present) or the first ref (if FOR is omitted but EXTENDS is present)
        string? extendsFrom = null;
        if (context.FOR() != null)
        {
            targetName = refs.Length > 0 ? refs[0].GetText() : "";
            extendsFrom = refs.Length > 1 ? refs[1].GetText() : null;
        }
        else
        {
            // No FOR keyword — no target entity
            targetName = "";
            extendsFrom = refs.Length > 0 ? refs[0].GetText() : null;
        }

        var acl = new BmAccessControl
        {
            Name = targetName,
            TargetEntity = targetName,
            ExtendsFrom = extendsFrom,
            SourceFile = _sourceFile,
            StartLine = context.Start.Line,
            EndLine = context.Stop.Line
        };

        foreach (var rule in context.accessRule())
        {
            acl.Rules.Add(BuildAccessRule(rule));
        }

        return acl;
    }

    private BmAccessRule BuildAccessRule(BmmdlParser.AccessRuleContext context)
    {
        var rule = new BmAccessRule();

        if (context.GRANT() != null)
        {
            rule.RuleType = BmAccessRuleType.Grant;
        }
        else if (context.DENY() != null)
        {
            rule.RuleType = BmAccessRuleType.Deny;
        }
        else if (context.RESTRICT() != null)
        {
            rule.RuleType = BmAccessRuleType.RestrictFields;
        }

        foreach (var op in context.operation())
        {
            rule.Operations.Add(op.GetText().ToUpper());
        }

        if (context.principal() != null)
        {
            rule.Principal = BuildPrincipal(context.principal());
        }

        // Parse scope level (AT TENANT scope, AT COMPANY scope, AT GLOBAL scope)
        if (context.scopeLevel() != null)
        {
            rule.Scope = ParseScopeLevel(context.scopeLevel());
        }

        if (context.whereClause() != null)
        {
            rule.WhereCondition = context.whereClause().expression().GetText();
            try { rule.WhereConditionExpr = _exprBuilder.Visit(context.whereClause().expression()); }
            catch (Exception ex) { AddWarning(context.Start.Line, "WhereClause", "Failed to parse where condition", ex); }
        }

        foreach (var fr in context.fieldRestriction())
        {
            rule.FieldRestrictions.Add(BuildFieldRestriction(fr));
        }

        return rule;
    }

    private static BmAccessScope ParseScopeLevel(BmmdlParser.ScopeLevelContext context)
    {
        if (context.GLOBAL() != null)
        {
            return BmAccessScope.Global;
        }
        else if (context.TENANT() != null)
        {
            return BmAccessScope.Tenant;
        }
        else if (context.COMPANY() != null)
        {
            return BmAccessScope.Company;
        }

        // Default to Tenant scope
        return BmAccessScope.Tenant;
    }

    private static BmPrincipal BuildPrincipal(BmmdlParser.PrincipalContext context)
    {
        var principal = new BmPrincipal();

        if (context.ROLE() != null)
        {
            principal.Type = BmPrincipalType.Role;
            principal.Values.AddRange(context.STRING_LITERAL().Select(s =>
                s.GetText().Trim('\'')));
        }
        else if (context.USER() != null)
        {
            principal.Type = BmPrincipalType.User;
            principal.Values.AddRange(context.STRING_LITERAL().Select(s =>
                s.GetText().Trim('\'')));
        }
        else if (context.AUTHENTICATED() != null)
        {
            principal.Type = BmPrincipalType.Authenticated;
        }
        else if (context.ANONYMOUS() != null)
        {
            principal.Type = BmPrincipalType.Anonymous;
        }

        return principal;
    }

    private BmFieldRestriction BuildFieldRestriction(BmmdlParser.FieldRestrictionContext context)
    {
        var fr = new BmFieldRestriction
        {
            FieldName = context.IDENTIFIER().GetText()
        };

        var rule = context.fieldAccessRule();
        if (rule.VISIBLE() != null)
        {
            fr.AccessType = BmFieldAccessType.Visible;
            fr.Condition = rule.expression()?.GetText();
            if (rule.expression() != null)
            {
                try { fr.ConditionExpr = _exprBuilder.Visit(rule.expression()); }
                catch (Exception ex) { AddWarning(context.Start.Line, "VisibleCondition", "Failed to parse visible condition", ex); }
            }
        }
        else if (rule.MASKED() != null)
        {
            fr.AccessType = BmFieldAccessType.Masked;
            fr.MaskType = rule.IDENTIFIER()?.GetText();
        }
        else if (rule.READONLY() != null)
        {
            fr.AccessType = BmFieldAccessType.Readonly;
            fr.Condition = rule.expression()?.GetText();
            if (rule.expression() != null)
            {
                try { fr.ConditionExpr = _exprBuilder.Visit(rule.expression()); }
                catch (Exception ex) { AddWarning(context.Start.Line, "ReadonlyCondition", "Failed to parse readonly condition", ex); }
            }
        }
        else if (rule.HIDDEN_KW() != null)
        {
            fr.AccessType = BmFieldAccessType.Hidden;
            fr.Condition = rule.expression()?.GetText();
            if (rule.expression() != null)
            {
                try { fr.ConditionExpr = _exprBuilder.Visit(rule.expression()); }
                catch (Exception ex) { AddWarning(context.Start.Line, "HiddenCondition", "Failed to parse hidden condition", ex); }
            }
        }

        return fr;
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
