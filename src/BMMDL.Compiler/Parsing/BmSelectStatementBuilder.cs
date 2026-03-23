using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Builds a BmSelectStatement AST from an ANTLR selectStatement context.
/// Uses the existing BmExpressionBuilder for expression parsing.
/// Falls back to raw strings when expression parsing fails.
/// </summary>
public class BmSelectStatementBuilder
{
    private readonly BmExpressionBuilder _exprBuilder;
    private readonly string? _sourceFile;
    private readonly List<ParseDiagnostic> _diagnostics;

    public BmSelectStatementBuilder(
        BmExpressionBuilder exprBuilder,
        string? sourceFile = null,
        List<ParseDiagnostic>? diagnostics = null)
    {
        _exprBuilder = exprBuilder;
        _sourceFile = sourceFile;
        _diagnostics = diagnostics ?? new List<ParseDiagnostic>();
    }

    /// <summary>
    /// Parse a selectStatement context into a BmSelectStatement AST.
    /// Returns null if parsing completely fails.
    /// </summary>
    public BmSelectStatement? Build(BmmdlParser.SelectStatementContext context)
    {
        try
        {
            var stmt = new BmSelectStatement();

            // DISTINCT
            stmt.IsDistinct = context.DISTINCT() != null;

            // SELECT list
            BuildSelectList(context.selectList(), stmt);

            // FROM clause
            BuildFromClause(context.fromClause(), stmt);

            // WHERE clause
            if (context.whereClause() != null)
            {
                BuildWhereClause(context.whereClause(), stmt);
            }

            // GROUP BY clause
            if (context.groupByClause() != null)
            {
                BuildGroupByClause(context.groupByClause(), stmt);
            }

            // HAVING clause
            if (context.havingClause() != null)
            {
                BuildHavingClause(context.havingClause(), stmt);
            }

            // ORDER BY clause
            if (context.orderByClause() != null)
            {
                BuildOrderByClause(context.orderByClause(), stmt);
            }

            // UNION/INTERSECT/EXCEPT clauses
            foreach (var unionCtx in context.unionClause())
            {
                BuildUnionClause(unionCtx, stmt);
            }

            return stmt;
        }
        catch (Exception ex)
        {
            AddWarning(context.Start.Line, "SelectStatement", $"Failed to parse SELECT AST: {ex.Message}");
            return null;
        }
    }

    private void BuildSelectList(BmmdlParser.SelectListContext context, BmSelectStatement stmt)
    {
        // Check for wildcard: SELECT *
        if (context.STAR() != null)
        {
            stmt.Columns.Add(new BmSelectColumn { IsWildcard = true, ExpressionString = "*" });
            return;
        }

        // Parse each select item
        foreach (var item in context.selectItem())
        {
            stmt.Columns.Add(BuildSelectItem(item));
        }
    }

    private BmSelectColumn BuildSelectItem(BmmdlParser.SelectItemContext context)
    {
        // Check for qualified wildcard: entity.*
        if (context.STAR() != null && context.identifierReference() != null)
        {
            return new BmSelectColumn
            {
                IsWildcard = true,
                WildcardQualifier = context.identifierReference().GetText(),
                ExpressionString = $"{context.identifierReference().GetText()}.*"
            };
        }

        // Normal expression with optional alias
        var col = new BmSelectColumn
        {
            ExpressionString = context.expression().GetText(),
            Alias = context.IDENTIFIER()?.GetText()
        };

        try
        {
            col.Expression = _exprBuilder.Visit(context.expression());
        }
        catch (Exception ex)
        {
            AddWarning(context.Start.Line, "SelectColumn",
                $"Failed to parse select column expression: {ex.Message}");
        }

        return col;
    }

    private void BuildFromClause(BmmdlParser.FromClauseContext context, BmSelectStatement stmt)
    {
        // Primary FROM source
        stmt.From = BuildFromSource(context.fromSource());

        // JOIN clauses
        foreach (var joinCtx in context.joinClause())
        {
            stmt.Joins.Add(BuildJoinClause(joinCtx));
        }
    }

    private BmFromSource BuildFromSource(BmmdlParser.FromSourceContext context)
    {
        var source = new BmFromSource();

        if (context.identifierReference() != null)
        {
            // Direct entity reference
            source.EntityReference = context.identifierReference().GetText();
            source.Alias = context.IDENTIFIER()?.GetText();

            // Temporal qualifier
            if (context.temporalQualifier() != null)
            {
                source.TemporalQualifier = BuildTemporalQualifier(context.temporalQualifier());
            }
        }
        else if (context.selectStatement() != null)
        {
            // Subquery source
            source.Subquery = Build(context.selectStatement());
            source.Alias = context.IDENTIFIER()?.GetText();
        }

        return source;
    }

    private BmTemporalQualifier BuildTemporalQualifier(BmmdlParser.TemporalQualifierContext context)
    {
        var qualifier = new BmTemporalQualifier
        {
            RawText = context.GetText()
        };

        if (context.ASOF() != null)
        {
            qualifier.Type = BmTemporalQualifierType.AsOf;
            try { qualifier.AsOfExpression = _exprBuilder.Visit(context.expression(0)); }
            catch (Exception ex) { AddWarning(context.Start.Line, "TemporalQualifier", $"Failed to parse AS OF expression, falling back to raw text: {ex.Message}"); }
        }
        else if (context.VERSIONS() != null && context.ALL() != null)
        {
            qualifier.Type = BmTemporalQualifierType.VersionsAll;
        }
        else if (context.VERSIONS() != null && context.BETWEEN() != null)
        {
            qualifier.Type = BmTemporalQualifierType.VersionsBetween;
            var exprs = context.expression();
            if (exprs.Length >= 2)
            {
                try { qualifier.VersionsFromExpression = _exprBuilder.Visit(exprs[0]); }
                catch (Exception ex) { AddWarning(context.Start.Line, "TemporalQualifier", $"Failed to parse VERSIONS FROM expression, falling back to raw text: {ex.Message}"); }
                try { qualifier.VersionsToExpression = _exprBuilder.Visit(exprs[1]); }
                catch (Exception ex) { AddWarning(context.Start.Line, "TemporalQualifier", $"Failed to parse VERSIONS TO expression, falling back to raw text: {ex.Message}"); }
            }
        }
        else if (context.CURRENT() != null)
        {
            qualifier.Type = BmTemporalQualifierType.Current;
        }

        return qualifier;
    }

    private BmJoinClause BuildJoinClause(BmmdlParser.JoinClauseContext context)
    {
        var join = new BmJoinClause();

        // Parse join type
        var joinTypeCtx = context.joinType();
        if (joinTypeCtx != null)
        {
            if (joinTypeCtx.LEFT() != null) join.JoinType = BmJoinType.Left;
            else if (joinTypeCtx.RIGHT() != null) join.JoinType = BmJoinType.Right;
            else if (joinTypeCtx.FULL() != null) join.JoinType = BmJoinType.Full;
            else if (joinTypeCtx.CROSS() != null) join.JoinType = BmJoinType.Cross;
            else join.JoinType = BmJoinType.Inner;
        }

        // FROM source
        join.Source = BuildFromSource(context.fromSource());

        // ON condition
        if (context.expression() != null)
        {
            join.OnConditionString = context.expression().GetText();
            try
            {
                join.OnCondition = _exprBuilder.Visit(context.expression());
            }
            catch (Exception ex)
            {
                AddWarning(context.Start.Line, "JoinCondition",
                    $"Failed to parse JOIN ON condition: {ex.Message}");
            }
        }

        return join;
    }

    private void BuildWhereClause(BmmdlParser.WhereClauseContext context, BmSelectStatement stmt)
    {
        stmt.WhereConditionString = context.expression().GetText();
        try
        {
            stmt.WhereCondition = _exprBuilder.Visit(context.expression());
        }
        catch (Exception ex)
        {
            AddWarning(context.Start.Line, "WhereClause",
                $"Failed to parse WHERE condition: {ex.Message}");
        }
    }

    private void BuildGroupByClause(BmmdlParser.GroupByClauseContext context, BmSelectStatement stmt)
    {
        foreach (var expr in context.expression())
        {
            stmt.GroupByStrings.Add(expr.GetText());
            try
            {
                stmt.GroupByColumns.Add(_exprBuilder.Visit(expr));
            }
            catch (Exception ex)
            {
                AddWarning(expr.Start.Line, "GroupBy",
                    $"Failed to parse GROUP BY expression: {ex.Message}");
            }
        }
    }

    private void BuildHavingClause(BmmdlParser.HavingClauseContext context, BmSelectStatement stmt)
    {
        stmt.HavingConditionString = context.expression().GetText();
        try
        {
            stmt.HavingCondition = _exprBuilder.Visit(context.expression());
        }
        catch (Exception ex)
        {
            AddWarning(context.Start.Line, "Having",
                $"Failed to parse HAVING condition: {ex.Message}");
        }
    }

    private void BuildOrderByClause(BmmdlParser.OrderByClauseContext context, BmSelectStatement stmt)
    {
        foreach (var item in context.orderItem())
        {
            var orderCol = new BmOrderByColumn
            {
                ExpressionString = item.expression().GetText()
            };

            // Parse expression
            try
            {
                orderCol.Expression = _exprBuilder.Visit(item.expression());
            }
            catch (Exception ex)
            {
                AddWarning(item.Start.Line, "OrderBy",
                    $"Failed to parse ORDER BY expression: {ex.Message}");
            }

            // Direction
            if (item.DESC() != null)
                orderCol.Direction = BmSortDirection.Desc;
            else
                orderCol.Direction = BmSortDirection.Asc;

            // NULLS ordering
            if (item.NULLS() != null)
            {
                orderCol.NullsOrdering = item.FIRST() != null
                    ? BmNullsOrdering.First
                    : BmNullsOrdering.Last;
            }

            stmt.OrderByColumns.Add(orderCol);
        }
    }

    private void BuildUnionClause(BmmdlParser.UnionClauseContext context, BmSelectStatement stmt)
    {
        var union = new BmUnionClause();

        if (context.UNION() != null)
        {
            union.Type = BmUnionType.Union;
            union.IsAll = context.ALL() != null;
        }
        else if (context.INTERSECT() != null)
        {
            union.Type = BmUnionType.Intersect;
        }
        else if (context.EXCEPT() != null)
        {
            union.Type = BmUnionType.Except;
        }

        var nestedSelect = Build(context.selectStatement());
        if (nestedSelect != null)
        {
            union.Select = nestedSelect;
        }

        stmt.UnionClauses.Add(union);
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
}
