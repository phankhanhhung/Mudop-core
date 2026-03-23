using BMMDL.MetaModel.Expressions;

namespace BMMDL.MetaModel.Structure;

/// <summary>
/// AST representation of a parsed SELECT statement from a view definition.
/// Replaces raw string storage for validation, dependency analysis, and SQL generation.
/// </summary>
public class BmSelectStatement
{
    /// <summary>
    /// Whether DISTINCT keyword is present.
    /// </summary>
    public bool IsDistinct { get; set; }

    /// <summary>
    /// The columns in the SELECT list.
    /// </summary>
    public List<BmSelectColumn> Columns { get; } = new();

    /// <summary>
    /// The primary FROM source.
    /// </summary>
    public BmFromSource From { get; set; } = new();

    /// <summary>
    /// JOIN clauses.
    /// </summary>
    public List<BmJoinClause> Joins { get; } = new();

    /// <summary>
    /// WHERE condition (parsed as BmExpression).
    /// </summary>
    public BmExpression? WhereCondition { get; set; }

    /// <summary>
    /// WHERE condition as raw string (fallback).
    /// </summary>
    public string? WhereConditionString { get; set; }

    /// <summary>
    /// GROUP BY expressions.
    /// </summary>
    public List<BmExpression> GroupByColumns { get; } = new();

    /// <summary>
    /// GROUP BY as raw strings (fallback).
    /// </summary>
    public List<string> GroupByStrings { get; } = new();

    /// <summary>
    /// HAVING condition (parsed as BmExpression).
    /// </summary>
    public BmExpression? HavingCondition { get; set; }

    /// <summary>
    /// HAVING condition as raw string (fallback).
    /// </summary>
    public string? HavingConditionString { get; set; }

    /// <summary>
    /// ORDER BY columns.
    /// </summary>
    public List<BmOrderByColumn> OrderByColumns { get; } = new();

    /// <summary>
    /// UNION/INTERSECT/EXCEPT clauses.
    /// </summary>
    public List<BmUnionClause> UnionClauses { get; } = new();
}

/// <summary>
/// A column in a SELECT list.
/// </summary>
public class BmSelectColumn
{
    /// <summary>
    /// The expression for this column (parsed AST).
    /// Null if IsWildcard is true or parsing failed.
    /// </summary>
    public BmExpression? Expression { get; set; }

    /// <summary>
    /// Raw expression string (always available).
    /// </summary>
    public string ExpressionString { get; set; } = "";

    /// <summary>
    /// Column alias (from AS clause), if any.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// True if this is a wildcard (*) column.
    /// </summary>
    public bool IsWildcard { get; set; }

    /// <summary>
    /// For qualified wildcards like "e.*", the qualifier prefix.
    /// </summary>
    public string? WildcardQualifier { get; set; }
}

/// <summary>
/// A FROM source (entity reference or subquery).
/// </summary>
public class BmFromSource
{
    /// <summary>
    /// The entity/table reference (e.g., "Customer", "HR.Employee").
    /// Null if this is a subquery source.
    /// </summary>
    public string? EntityReference { get; set; }

    /// <summary>
    /// Alias for this source (from AS clause).
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Subquery source, if this FROM is a derived table.
    /// </summary>
    public BmSelectStatement? Subquery { get; set; }

    /// <summary>
    /// Temporal qualifier (e.g., TEMPORAL AS OF, TEMPORAL VERSIONS ALL).
    /// </summary>
    public BmTemporalQualifier? TemporalQualifier { get; set; }
}

/// <summary>
/// Temporal qualifier for FROM sources.
/// </summary>
public class BmTemporalQualifier
{
    public BmTemporalQualifierType Type { get; set; }

    /// <summary>
    /// For AS OF: the point-in-time expression.
    /// </summary>
    public BmExpression? AsOfExpression { get; set; }

    /// <summary>
    /// For VERSIONS BETWEEN: the start expression.
    /// </summary>
    public BmExpression? VersionsFromExpression { get; set; }

    /// <summary>
    /// For VERSIONS BETWEEN: the end expression.
    /// </summary>
    public BmExpression? VersionsToExpression { get; set; }

    public string? RawText { get; set; }
}

public enum BmTemporalQualifierType
{
    AsOf,
    VersionsAll,
    VersionsBetween,
    Current
}

/// <summary>
/// A JOIN clause in a SELECT statement.
/// </summary>
public class BmJoinClause
{
    /// <summary>
    /// The join type (INNER, LEFT, RIGHT, FULL, CROSS).
    /// </summary>
    public BmJoinType JoinType { get; set; } = BmJoinType.Inner;

    /// <summary>
    /// The joined source.
    /// </summary>
    public BmFromSource Source { get; set; } = new();

    /// <summary>
    /// The ON condition (parsed AST).
    /// </summary>
    public BmExpression? OnCondition { get; set; }

    /// <summary>
    /// The ON condition as raw string (fallback).
    /// </summary>
    public string? OnConditionString { get; set; }
}

public enum BmJoinType
{
    Inner,
    Left,
    Right,
    Full,
    Cross
}

/// <summary>
/// An ORDER BY column specification.
/// </summary>
public class BmOrderByColumn
{
    /// <summary>
    /// The order expression (parsed AST).
    /// </summary>
    public BmExpression? Expression { get; set; }

    /// <summary>
    /// Raw expression string.
    /// </summary>
    public string ExpressionString { get; set; } = "";

    /// <summary>
    /// Sort direction.
    /// </summary>
    public BmSortDirection Direction { get; set; } = BmSortDirection.Asc;

    /// <summary>
    /// NULLS FIRST or NULLS LAST, if specified.
    /// </summary>
    public BmNullsOrdering? NullsOrdering { get; set; }
}

public enum BmSortDirection
{
    Asc,
    Desc
}

public enum BmNullsOrdering
{
    First,
    Last
}

/// <summary>
/// A UNION/INTERSECT/EXCEPT clause.
/// </summary>
public class BmUnionClause
{
    public BmUnionType Type { get; set; }
    public bool IsAll { get; set; }
    public BmSelectStatement Select { get; set; } = new();
}

public enum BmUnionType
{
    Union,
    Intersect,
    Except
}
