using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Utilities;

/// <summary>
/// Compiler-specific expression traversal that extends BmExpressionWalker
/// with BmSelectStatement-aware traversal (subquery/exists internal ASTs).
/// </summary>
public static class ExpressionTraversalUtility
{
    /// <summary>
    /// Traverses an expression tree, calling the visitor for each node.
    /// Unlike BmExpressionWalker.Walk, this also traverses into BmSelectStatement ASTs
    /// inside BmSubqueryExpression and BmExistsExpression nodes.
    /// </summary>
    public static void Traverse(BmExpression? expr, Action<BmExpression> visitor)
    {
        if (expr == null) return;

        BmExpressionWalker.Walk(expr, node =>
        {
            visitor(node);

            // Additionally traverse into parsed SELECT ASTs (compiler-specific)
            switch (node)
            {
                case BmSubqueryExpression subquery:
                    TraverseSelectStatement(subquery.ParsedSelect, visitor);
                    break;
                case BmExistsExpression exists:
                    TraverseSelectStatement(exists.ParsedSelect, visitor);
                    break;
            }
        });
    }

    /// <summary>
    /// Traverses all expressions embedded within a BmSelectStatement AST.
    /// </summary>
    private static void TraverseSelectStatement(BmSelectStatement? select, Action<BmExpression> visitor)
    {
        if (select == null) return;

        // SELECT columns
        foreach (var col in select.Columns)
            Traverse(col.Expression, visitor);

        // FROM source temporal qualifier expressions
        TraverseFromSource(select.From, visitor);

        // JOINs
        foreach (var join in select.Joins)
        {
            TraverseFromSource(join.Source, visitor);
            Traverse(join.OnCondition, visitor);
        }

        // WHERE
        Traverse(select.WhereCondition, visitor);

        // GROUP BY
        foreach (var groupBy in select.GroupByColumns)
            Traverse(groupBy, visitor);

        // HAVING
        Traverse(select.HavingCondition, visitor);

        // ORDER BY
        foreach (var orderBy in select.OrderByColumns)
            Traverse(orderBy.Expression, visitor);

        // UNION clauses
        foreach (var union in select.UnionClauses)
            TraverseSelectStatement(union.Select, visitor);
    }

    /// <summary>
    /// Traverses expressions within a FROM source (subquery and temporal qualifiers).
    /// </summary>
    private static void TraverseFromSource(BmFromSource? source, Action<BmExpression> visitor)
    {
        if (source == null) return;

        // Subquery in FROM
        TraverseSelectStatement(source.Subquery, visitor);

        // Temporal qualifier expressions
        if (source.TemporalQualifier != null)
        {
            Traverse(source.TemporalQualifier.AsOfExpression, visitor);
            Traverse(source.TemporalQualifier.VersionsFromExpression, visitor);
            Traverse(source.TemporalQualifier.VersionsToExpression, visitor);
        }
    }

    /// <summary>
    /// Collects all identifier names from an expression tree.
    /// </summary>
    public static HashSet<string> CollectIdentifiers(BmExpression? expr)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Traverse(expr, node =>
        {
            if (node is BmIdentifierExpression ident && ident.Path.Count > 0)
            {
                identifiers.Add(ident.Path[0]);
            }
        });

        return identifiers;
    }

    /// <summary>
    /// Collects all expressions of a specific type from an expression tree.
    /// </summary>
    public static List<T> CollectAll<T>(BmExpression? expr) where T : BmExpression
    {
        var results = new List<T>();

        Traverse(expr, node =>
        {
            if (node is T typed)
                results.Add(typed);
        });

        return results;
    }

    /// <summary>
    /// Checks if an expression tree contains any expression matching a predicate.
    /// </summary>
    public static bool ContainsAny(BmExpression? expr, Func<BmExpression, bool> predicate)
    {
        return BmExpressionWalker.Any(expr, predicate);
    }
}
