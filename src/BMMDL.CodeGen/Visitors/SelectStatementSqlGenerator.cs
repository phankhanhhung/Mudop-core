using System.Text;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Visitors;

/// <summary>
/// Generates PostgreSQL SQL from a BmSelectStatement AST.
/// Used by the DDL generator to produce CREATE VIEW AS SELECT statements.
/// </summary>
public class SelectStatementSqlGenerator
{
    private readonly IReadOnlyDictionary<string, BmEntity> _entityCache;
    private readonly PostgresSqlExpressionVisitor? _exprVisitor;

    public SelectStatementSqlGenerator(IReadOnlyDictionary<string, BmEntity> entityCache)
    {
        _entityCache = entityCache;
        // Expression visitor needs an entity context — we use a dummy one for views
        _exprVisitor = null;
    }

    /// <summary>
    /// Generate SQL from a BmSelectStatement AST.
    /// Falls back to raw string if AST is incomplete.
    /// </summary>
    public string Generate(BmSelectStatement stmt)
    {
        var sb = new StringBuilder();

        // SELECT [DISTINCT]
        sb.Append("SELECT ");
        if (stmt.IsDistinct) sb.Append("DISTINCT ");

        // Columns
        sb.Append(GenerateSelectList(stmt.Columns));

        // FROM
        sb.Append(" FROM ");
        sb.Append(GenerateFromSource(stmt.From));

        // JOINs
        foreach (var join in stmt.Joins)
        {
            sb.Append(GenerateJoin(join));
        }

        // WHERE
        if (stmt.WhereCondition != null || !string.IsNullOrEmpty(stmt.WhereConditionString))
        {
            sb.Append(" WHERE ");
            sb.Append(GenerateExpression(stmt.WhereCondition, stmt.WhereConditionString));
        }

        // GROUP BY
        if (stmt.GroupByColumns.Count > 0 || stmt.GroupByStrings.Count > 0)
        {
            sb.Append(" GROUP BY ");
            if (stmt.GroupByColumns.Count > 0)
            {
                sb.Append(string.Join(", ", stmt.GroupByColumns.Select(e => GenerateExpression(e, null))));
            }
            else
            {
                sb.Append(string.Join(", ", stmt.GroupByStrings.Select(TransformIdentifier)));
            }
        }

        // HAVING
        if (stmt.HavingCondition != null || !string.IsNullOrEmpty(stmt.HavingConditionString))
        {
            sb.Append(" HAVING ");
            sb.Append(GenerateExpression(stmt.HavingCondition, stmt.HavingConditionString));
        }

        // ORDER BY
        if (stmt.OrderByColumns.Count > 0)
        {
            sb.Append(" ORDER BY ");
            sb.Append(string.Join(", ", stmt.OrderByColumns.Select(GenerateOrderByColumn)));
        }

        // UNION / INTERSECT / EXCEPT
        foreach (var union in stmt.UnionClauses)
        {
            sb.Append(GenerateUnionClause(union));
        }

        return sb.ToString();
    }

    private string GenerateSelectList(List<BmSelectColumn> columns)
    {
        if (columns.Count == 0) return "*";

        return string.Join(", ", columns.Select(col =>
        {
            if (col.IsWildcard)
            {
                return col.WildcardQualifier != null
                    ? $"{TransformIdentifier(col.WildcardQualifier)}.*"
                    : "*";
            }

            var expr = GenerateExpression(col.Expression, col.ExpressionString);
            return col.Alias != null ? $"{expr} AS {TransformIdentifier(col.Alias)}" : expr;
        }));
    }

    private string GenerateFromSource(BmFromSource source)
    {
        var sb = new StringBuilder();

        if (source.Subquery != null)
        {
            sb.Append('(');
            sb.Append(Generate(source.Subquery));
            sb.Append(')');
        }
        else if (source.EntityReference != null)
        {
            sb.Append(ResolveEntityToTable(source.EntityReference));
        }
        else
        {
            throw new InvalidOperationException(
                "BmFromSource has neither Subquery nor EntityReference set. " +
                "This indicates a malformed SELECT statement AST.");
        }

        if (source.TemporalQualifier != null)
        {
            sb.Append(GenerateTemporalQualifier(source.TemporalQualifier));
        }

        if (source.Alias != null)
        {
            sb.Append($" AS {TransformIdentifier(source.Alias)}");
        }

        return sb.ToString();
    }

    private string GenerateTemporalQualifier(BmTemporalQualifier qualifier)
    {
        // Temporal qualifiers are BMMDL-specific; translate to SQL comments or system column filters
        // For now, pass through as comments (the runtime handles temporal logic)
        return qualifier.RawText != null ? $" /* {qualifier.RawText.Replace("*/", "* /")} */" : "";
    }

    private string GenerateJoin(BmJoinClause join)
    {
        var sb = new StringBuilder();

        var joinTypeStr = join.JoinType switch
        {
            BmJoinType.Left => " LEFT JOIN ",
            BmJoinType.Right => " RIGHT JOIN ",
            BmJoinType.Full => " FULL JOIN ",
            BmJoinType.Cross => " CROSS JOIN ",
            _ => " INNER JOIN "
        };
        sb.Append(joinTypeStr);
        sb.Append(GenerateFromSource(join.Source));

        if (join.OnCondition != null || !string.IsNullOrEmpty(join.OnConditionString))
        {
            sb.Append(" ON ");
            sb.Append(GenerateExpression(join.OnCondition, join.OnConditionString));
        }

        return sb.ToString();
    }

    private string GenerateOrderByColumn(BmOrderByColumn col)
    {
        var expr = GenerateExpression(col.Expression, col.ExpressionString);
        var dir = col.Direction == BmSortDirection.Desc ? " DESC" : " ASC";
        var nulls = col.NullsOrdering switch
        {
            BmNullsOrdering.First => " NULLS FIRST",
            BmNullsOrdering.Last => " NULLS LAST",
            _ => ""
        };
        return $"{expr}{dir}{nulls}";
    }

    private string GenerateUnionClause(BmUnionClause union)
    {
        var typeStr = union.Type switch
        {
            BmUnionType.Union => union.IsAll ? " UNION ALL " : " UNION ",
            BmUnionType.Intersect => " INTERSECT ",
            BmUnionType.Except => " EXCEPT ",
            _ => " UNION "
        };
        return $"{typeStr}{Generate(union.Select)}";
    }

    private string GenerateExpression(BmExpression? expr, string? rawString)
    {
        // If we have an AST expression, try to generate SQL from it
        if (expr != null)
        {
            return TransformExpressionString(expr.ToExpressionString());
        }

        // Fall back to raw string with identifier transforms
        return TransformExpressionString(rawString ?? "");
    }

    /// <summary>
    /// Resolve an entity name to its qualified PostgreSQL table name.
    /// </summary>
    private string ResolveEntityToTable(string entityRef)
    {
        // Try to find entity in cache
        if (_entityCache.TryGetValue(entityRef, out var entity))
        {
            var schemaName = NamingConvention.GetSchemaName(entity.Namespace);
            var tableName = NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(entity.Name));
            return string.IsNullOrEmpty(schemaName) ? tableName : $"{NamingConvention.QuoteIdentifier(schemaName)}.{tableName}";
        }

        // Try qualified name lookup
        foreach (var e in _entityCache.Values)
        {
            if (e.QualifiedName == entityRef || e.Name == entityRef)
            {
                var schemaName = NamingConvention.GetSchemaName(e.Namespace);
                var tableName = NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(e.Name));
                return string.IsNullOrEmpty(schemaName) ? tableName : $"{NamingConvention.QuoteIdentifier(schemaName)}.{tableName}";
            }
        }

        // Fallback: convert to snake_case
        return NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(entityRef));
    }

    /// <summary>
    /// Transform PascalCase identifiers to snake_case in an expression string.
    /// </summary>
    private static string TransformExpressionString(string text)
    {
        // Convert PascalCase/camelCase identifiers to snake_case
        // Skip SQL keywords
        var sqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "AS", "ON", "JOIN",
            "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "GROUP",
            "ORDER", "BY", "HAVING", "UNION", "ALL", "INTERSECT", "EXCEPT",
            "DISTINCT", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN",
            "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "ASC", "DESC",
            "NULLS", "FIRST", "LAST", "TRUE", "FALSE", "LIMIT", "OFFSET",
            "COUNT", "SUM", "AVG", "MIN", "MAX", "CAST", "COALESCE"
        };

        // Match both PascalCase (TestProduct) and camelCase (sellingPrice, isActive)
        return System.Text.RegularExpressions.Regex.Replace(text, @"\b([a-zA-Z][a-z]*[A-Z][a-zA-Z]*)\b", match =>
        {
            if (sqlKeywords.Contains(match.Value)) return match.Value;
            return NamingConvention.GetColumnName(match.Value);
        });
    }

    private static string TransformIdentifier(string name)
    {
        return NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(name));
    }
}
