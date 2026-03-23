namespace BMMDL.MetaModel.Expressions;

using BMMDL.MetaModel.Abstractions;

/// <summary>
/// Base class for all expressions in BMMDL.
/// </summary>
public abstract class BmExpression
{
    /// <summary>
    /// Source file where this expression was defined.
    /// </summary>
    public string? SourceFile { get; set; }
    
    /// <summary>
    /// Start line in source.
    /// </summary>
    public int StartLine { get; set; }
    
    /// <summary>
    /// End line in source.
    /// </summary>
    public int EndLine { get; set; }
    
    /// <summary>
    /// Inferred type of this expression (populated after type checking).
    /// </summary>
    public Types.BmTypeReference? InferredType { get; set; }
    
    /// <summary>
    /// Get the string representation of this expression.
    /// </summary>
    public abstract string ToExpressionString();
    
    public override string ToString() => ToExpressionString();
}

/// <summary>
/// Literal value kinds.
/// </summary>
public enum BmLiteralKind
{
    String,
    Integer,
    Decimal,
    Boolean,
    Null,
    EnumValue  // #EnumMember
}

/// <summary>
/// Literal expression: 'hello', 123, 45.67, true, false, null, #Active
/// </summary>
public class BmLiteralExpression : BmExpression
{
    public BmLiteralKind Kind { get; set; }
    public object? Value { get; set; }
    
    public BmLiteralExpression(BmLiteralKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }
    
    public static BmLiteralExpression String(string value) => 
        new(BmLiteralKind.String, value);
    
    public static BmLiteralExpression Integer(long value) => 
        new(BmLiteralKind.Integer, value);
    
    public static BmLiteralExpression Decimal(decimal value) => 
        new(BmLiteralKind.Decimal, value);
    
    public static BmLiteralExpression Boolean(bool value) => 
        new(BmLiteralKind.Boolean, value);
    
    public static BmLiteralExpression Null() => 
        new(BmLiteralKind.Null, null);
    
    public static BmLiteralExpression EnumValue(string enumMember) => 
        new(BmLiteralKind.EnumValue, enumMember);
    
    public override string ToExpressionString()
    {
        return Kind switch
        {
            BmLiteralKind.String => $"'{Value}'",
            BmLiteralKind.Integer => Value?.ToString() ?? "0",
            BmLiteralKind.Decimal => Value?.ToString() ?? "0",
            BmLiteralKind.Boolean => Value is true ? "true" : "false",
            BmLiteralKind.Null => "null",
            BmLiteralKind.EnumValue => $"#{Value}",
            _ => Value?.ToString() ?? ""
        };
    }
}

/// <summary>
/// Identifier/path reference: fieldName, entity.field, a.b.c
/// </summary>
public class BmIdentifierExpression : BmExpression
{
    /// <summary>
    /// Path segments: ["customer", "address", "city"]
    /// </summary>
    public List<string> Path { get; set; } = new();
    
    public BmIdentifierExpression(params string[] path)
    {
        Path = path.ToList();
    }
    
    public BmIdentifierExpression(IEnumerable<string> path)
    {
        Path = path.ToList();
    }
    
    /// <summary>
    /// Get the full qualified name.
    /// </summary>
    public string FullPath => string.Join(".", Path);
    
    /// <summary>
    /// Get the first segment (root identifier).
    /// </summary>
    public string Root => Path.FirstOrDefault() ?? "";
    
    /// <summary>
    /// Whether this is a simple (single segment) identifier.
    /// </summary>
    public bool IsSimple => Path.Count == 1;
    
    public override string ToExpressionString() => FullPath;
    
    /// <summary>
    /// The semantic symbol this identifier resolves to, after Binding Pass.
    /// </summary>
    public INamedElement? BoundSymbol { get; set; }
}

/// <summary>
/// Context variable: $now, $today, $user.id, $tenant
/// </summary>
public class BmContextVariableExpression : BmExpression
{
    /// <summary>
    /// Path segments without the $ prefix: ["now"], ["user", "id"]
    /// </summary>
    public List<string> Path { get; set; } = new();
    
    public BmContextVariableExpression(params string[] path)
    {
        Path = path.ToList();
    }
    
    /// <summary>
    /// Get the full path with $ prefix.
    /// </summary>
    public string FullPath => "$" + string.Join(".", Path);
    
    /// <summary>
    /// Get the root variable name (without $).
    /// </summary>
    public string Root => Path.FirstOrDefault() ?? "";
    
    public override string ToExpressionString() => FullPath;
}

/// <summary>
/// Parameter reference: :customerId, :startDate
/// </summary>
public class BmParameterExpression : BmExpression
{
    public string Name { get; set; } = "";
    
    public BmParameterExpression(string name)
    {
        Name = name;
    }
    
    public override string ToExpressionString() => $":{Name}";
}

/// <summary>
/// Binary operators.
/// </summary>
public enum BmBinaryOperator
{
    // Arithmetic
    Add,        // +
    Subtract,   // -
    Multiply,   // *
    Divide,     // /
    Modulo,     // %
    
    // Comparison
    Equal,          // =
    NotEqual,       // <> or !=
    LessThan,       // <
    GreaterThan,    // >
    LessOrEqual,    // <=
    GreaterOrEqual, // >=
    
    // Logical
    And,    // AND
    Or,     // OR
    
    // String
    Concat  // ||
}

/// <summary>
/// Binary expression: left op right
/// </summary>
public class BmBinaryExpression : BmExpression
{
    public BmExpression Left { get; set; }
    public BmBinaryOperator Operator { get; set; }
    public BmExpression Right { get; set; }
    
    public BmBinaryExpression(BmExpression left, BmBinaryOperator op, BmExpression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
    
    public override string ToExpressionString()
    {
        var opStr = Operator switch
        {
            BmBinaryOperator.Add => "+",
            BmBinaryOperator.Subtract => "-",
            BmBinaryOperator.Multiply => "*",
            BmBinaryOperator.Divide => "/",
            BmBinaryOperator.Modulo => "%",
            BmBinaryOperator.Equal => "=",
            BmBinaryOperator.NotEqual => "<>",
            BmBinaryOperator.LessThan => "<",
            BmBinaryOperator.GreaterThan => ">",
            BmBinaryOperator.LessOrEqual => "<=",
            BmBinaryOperator.GreaterOrEqual => ">=",
            BmBinaryOperator.And => "AND",
            BmBinaryOperator.Or => "OR",
            BmBinaryOperator.Concat => "||",
            _ => "?"
        };
        
        return $"({Left.ToExpressionString()} {opStr} {Right.ToExpressionString()})";
    }
}

/// <summary>
/// Unary operators.
/// </summary>
public enum BmUnaryOperator
{
    Not,        // NOT
    Negate,     // - (negative)
    Plus        // + (positive, usually no-op)
}

/// <summary>
/// Unary expression: op operand
/// </summary>
public class BmUnaryExpression : BmExpression
{
    public BmUnaryOperator Operator { get; set; }
    public BmExpression Operand { get; set; }
    
    public BmUnaryExpression(BmUnaryOperator op, BmExpression operand)
    {
        Operator = op;
        Operand = operand;
    }
    
    public override string ToExpressionString()
    {
        var opStr = Operator switch
        {
            BmUnaryOperator.Not => "NOT ",
            BmUnaryOperator.Negate => "-",
            BmUnaryOperator.Plus => "+",
            _ => ""
        };
        
        return $"{opStr}{Operand.ToExpressionString()}";
    }
}

/// <summary>
/// Function call: UPPER(name), SUBSTRING(text, 1, 10)
/// </summary>
public class BmFunctionCallExpression : BmExpression
{
    public string FunctionName { get; set; } = "";
    public List<BmExpression> Arguments { get; set; } = new();
    
    /// <summary>
    /// Named argument labels (parallel to Arguments list). Null entry means positional argument.
    /// </summary>
    public List<string?> ArgumentLabels { get; set; } = new();
    
    public BmFunctionCallExpression(string functionName, params BmExpression[] args)
    {
        FunctionName = functionName;
        Arguments = args.ToList();
        ArgumentLabels = args.Select(_ => (string?)null).ToList();
    }
    
    public override string ToExpressionString()
    {
        var argStrings = Arguments.Select((a, i) =>
        {
            var label = i < ArgumentLabels.Count ? ArgumentLabels[i] : null;
            return label != null ? $"{label}: {a.ToExpressionString()}" : a.ToExpressionString();
        });
        return $"{FunctionName}({string.Join(", ", argStrings)})";
    }
}

/// <summary>
/// Aggregate functions.
/// </summary>
public enum BmAggregateFunction
{
    Count,
    Sum,
    Avg,
    Min,
    Max,
    StdDev,
    Variance
}

/// <summary>
/// Aggregate expression: SUM(amount), COUNT(DISTINCT customer), COUNT(items where status = #Active)
/// </summary>
public class BmAggregateExpression : BmExpression
{
    public BmAggregateFunction Function { get; set; }
    public BmExpression? Argument { get; set; }  // null for COUNT(*)
    public bool IsDistinct { get; set; }
    
    /// <summary>
    /// Optional WHERE condition for conditional aggregates.
    /// </summary>
    public BmExpression? WhereCondition { get; set; }
    
    public BmAggregateExpression(BmAggregateFunction function, BmExpression? argument = null, bool isDistinct = false, BmExpression? whereCondition = null)
    {
        Function = function;
        Argument = argument;
        IsDistinct = isDistinct;
        WhereCondition = whereCondition;
    }
    
    public override string ToExpressionString()
    {
        if (Function == BmAggregateFunction.Count && Argument == null)
            return "COUNT(*)";
        
        var distinct = IsDistinct ? "DISTINCT " : "";
        var arg = Argument?.ToExpressionString() ?? "*";
        var where = WhereCondition != null ? $" WHERE {WhereCondition.ToExpressionString()}" : "";
        return $"{Function.ToString().ToUpper()}({distinct}{arg}{where})";
    }
}

/// <summary>
/// Represents a window function expression: func() OVER (PARTITION BY ... ORDER BY ... frame)
/// </summary>
public class BmWindowExpression : BmExpression
{
    public string FunctionName { get; set; } = "";
    public List<BmExpression> FunctionArguments { get; set; } = new();
    public List<BmExpression> PartitionBy { get; set; } = new();
    public List<BmOrderByItem> OrderBy { get; set; } = new();
    public BmWindowFrame? Frame { get; set; }
    
    public override string ToExpressionString()
    {
        var args = FunctionArguments.Count > 0
            ? string.Join(", ", FunctionArguments.Select(a => a.ToExpressionString()))
            : "";
        var partitionBy = PartitionBy.Count > 0
            ? "PARTITION BY " + string.Join(", ", PartitionBy.Select(p => p.ToExpressionString()))
            : "";
        var orderBy = OrderBy.Count > 0
            ? "ORDER BY " + string.Join(", ", OrderBy.Select(o => o.ToExpressionString()))
            : "";
        var frame = Frame?.ToExpressionString() ?? "";
        
        var spec = string.Join(" ", new[] { partitionBy, orderBy, frame }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"{FunctionName}({args}) OVER ({spec})";
    }
}

public class BmOrderByItem
{
    public BmExpression Expression { get; set; } = null!;
    public bool Descending { get; set; }
    public NullsPosition? Nulls { get; set; }
    
    public string ToExpressionString()
    {
        var dir = Descending ? " DESC" : "";
        var nulls = Nulls switch
        {
            NullsPosition.First => " NULLS FIRST",
            NullsPosition.Last => " NULLS LAST",
            _ => ""
        };
        return $"{Expression.ToExpressionString()}{dir}{nulls}";
    }
}

public enum NullsPosition { First, Last }

public class BmWindowFrame
{
    public string Type { get; set; } = "ROWS"; // ROWS or RANGE
    public BmFrameBound Start { get; set; } = null!;
    public BmFrameBound? End { get; set; }
    
    public string ToExpressionString()
    {
        if (End != null)
            return $"{Type} BETWEEN {Start.ToExpressionString()} AND {End.ToExpressionString()}";
        return $"{Type} {Start.ToExpressionString()}";
    }
}

public class BmFrameBound
{
    public BmFrameBoundType BoundType { get; set; }
    public BmExpression? Offset { get; set; }
    
    public string ToExpressionString() => BoundType switch
    {
        BmFrameBoundType.UnboundedPreceding => "UNBOUNDED PRECEDING",
        BmFrameBoundType.UnboundedFollowing => "UNBOUNDED FOLLOWING",
        BmFrameBoundType.CurrentRow => "CURRENT ROW",
        BmFrameBoundType.Preceding => $"{Offset?.ToExpressionString()} PRECEDING",
        BmFrameBoundType.Following => $"{Offset?.ToExpressionString()} FOLLOWING",
        _ => ""
    };
}

public enum BmFrameBoundType
{
    UnboundedPreceding,
    UnboundedFollowing,
    CurrentRow,
    Preceding,
    Following
}

/// <summary>
/// CASE expression.
/// </summary>
public class BmCaseExpression : BmExpression
{
    /// <summary>
    /// Input expression for simple CASE (null for searched CASE).
    /// </summary>
    public BmExpression? InputExpression { get; set; }
    
    /// <summary>
    /// WHEN-THEN clauses.
    /// </summary>
    public List<(BmExpression When, BmExpression Then)> WhenClauses { get; set; } = new();
    
    /// <summary>
    /// ELSE result (optional).
    /// </summary>
    public BmExpression? ElseResult { get; set; }
    
    public override string ToExpressionString()
    {
        var result = "CASE";
        if (InputExpression != null)
            result += $" {InputExpression.ToExpressionString()}";
        
        foreach (var (when, then) in WhenClauses)
        {
            result += $" WHEN {when.ToExpressionString()} THEN {then.ToExpressionString()}";
        }
        
        if (ElseResult != null)
            result += $" ELSE {ElseResult.ToExpressionString()}";
        
        result += " END";
        return result;
    }
}

/// <summary>
/// CAST expression: CAST(expr AS type)
/// </summary>
public class BmCastExpression : BmExpression
{
    public BmExpression Expression { get; set; }
    public Types.BmTypeReference TargetType { get; set; }
    
    public BmCastExpression(BmExpression expression, Types.BmTypeReference targetType)
    {
        Expression = expression;
        TargetType = targetType;
    }
    
    public override string ToExpressionString()
    {
        return $"CAST({Expression.ToExpressionString()} AS {TargetType.ToTypeString()})";
    }
}

/// <summary>
/// Ternary expression: condition ? thenExpr : elseExpr
/// </summary>
public class BmTernaryExpression : BmExpression
{
    public BmExpression Condition { get; set; }
    public BmExpression ThenExpression { get; set; }
    public BmExpression ElseExpression { get; set; }
    
    public BmTernaryExpression(BmExpression condition, BmExpression thenExpr, BmExpression elseExpr)
    {
        Condition = condition;
        ThenExpression = thenExpr;
        ElseExpression = elseExpr;
    }
    
    public override string ToExpressionString()
    {
        return $"({Condition.ToExpressionString()} ? {ThenExpression.ToExpressionString()} : {ElseExpression.ToExpressionString()})";
    }
}

/// <summary>
/// IN expression: expr [NOT] IN (values...)
/// </summary>
public class BmInExpression : BmExpression
{
    public BmExpression Expression { get; set; }
    public bool IsNot { get; set; }
    public List<BmExpression> Values { get; set; } = new();

    /// <summary>
    /// Subquery for IN (SELECT ...) form. When set, Values is empty.
    /// </summary>
    public BmSubqueryExpression? Subquery { get; set; }

    public BmInExpression(BmExpression expression, IEnumerable<BmExpression> values, bool isNot = false)
    {
        Expression = expression;
        Values = values.ToList();
        IsNot = isNot;
    }

    public BmInExpression(BmExpression expression, BmSubqueryExpression subquery, bool isNot = false)
    {
        Expression = expression;
        Subquery = subquery;
        IsNot = isNot;
    }

    public override string ToExpressionString()
    {
        var notStr = IsNot ? "NOT " : "";
        if (Subquery != null)
            return $"{Expression.ToExpressionString()} {notStr}IN ({Subquery.ToExpressionString()})";
        var vals = string.Join(", ", Values.Select(v => v.ToExpressionString()));
        return $"{Expression.ToExpressionString()} {notStr}IN ({vals})";
    }
}

/// <summary>
/// BETWEEN expression: expr [NOT] BETWEEN low AND high
/// </summary>
public class BmBetweenExpression : BmExpression
{
    public BmExpression Expression { get; set; }
    public bool IsNot { get; set; }
    public BmExpression Low { get; set; }
    public BmExpression High { get; set; }
    
    public BmBetweenExpression(BmExpression expression, BmExpression low, BmExpression high, bool isNot = false)
    {
        Expression = expression;
        Low = low;
        High = high;
        IsNot = isNot;
    }
    
    public override string ToExpressionString()
    {
        var notStr = IsNot ? "NOT " : "";
        return $"{Expression.ToExpressionString()} {notStr}BETWEEN {Low.ToExpressionString()} AND {High.ToExpressionString()}";
    }
}

/// <summary>
/// LIKE expression: expr [NOT] LIKE pattern
/// </summary>
public class BmLikeExpression : BmExpression
{
    public BmExpression Expression { get; set; }
    public bool IsNot { get; set; }
    public BmExpression Pattern { get; set; }
    
    public BmLikeExpression(BmExpression expression, BmExpression pattern, bool isNot = false)
    {
        Expression = expression;
        Pattern = pattern;
        IsNot = isNot;
    }
    
    public override string ToExpressionString()
    {
        var notStr = IsNot ? "NOT " : "";
        return $"{Expression.ToExpressionString()} {notStr}LIKE {Pattern.ToExpressionString()}";
    }
}

/// <summary>
/// IS NULL expression: expr IS [NOT] NULL
/// </summary>
public class BmIsNullExpression : BmExpression
{
    public BmExpression Expression { get; set; }
    public bool IsNot { get; set; }
    
    public BmIsNullExpression(BmExpression expression, bool isNot = false)
    {
        Expression = expression;
        IsNot = isNot;
    }
    
    public override string ToExpressionString()
    {
        var notStr = IsNot ? "NOT " : "";
        return $"{Expression.ToExpressionString()} IS {notStr}NULL";
    }
}

/// <summary>
/// Parenthesized expression for grouping.
/// </summary>
public class BmParenExpression : BmExpression
{
    public BmExpression Inner { get; set; }
    
    public BmParenExpression(BmExpression inner)
    {
        Inner = inner;
    }
    
    public override string ToExpressionString()
    {
        return $"({Inner.ToExpressionString()})";
    }
}

/// <summary>
/// Subquery expression: (SELECT ... FROM ... WHERE ...)
/// </summary>
public class BmSubqueryExpression : BmExpression
{
    /// <summary>
    /// The raw select statement text from the DSL source.
    /// </summary>
    public string SelectStatement { get; set; } = "";

    /// <summary>
    /// Parsed AST of the select statement. Null if parsing failed.
    /// </summary>
    public Structure.BmSelectStatement? ParsedSelect { get; set; }

    public BmSubqueryExpression(string selectStatement)
    {
        SelectStatement = selectStatement;
    }

    public override string ToExpressionString()
    {
        return $"({SelectStatement})";
    }
}

/// <summary>
/// Temporal binary operators for interval comparisons.
/// </summary>
public enum TemporalBinaryOperator
{
    Overlaps,   // Two intervals share at least one point in time
    Contains,   // First interval fully contains second
    Precedes,   // First interval ends before second begins
    Meets       // First interval ends exactly where second begins
}

/// <summary>
/// Temporal binary expression: left OVERLAPS|CONTAINS|PRECEDES|MEETS right
/// Each operand represents a temporal interval (pair of date/datetime values).
/// </summary>
public class BmTemporalBinaryExpression : BmExpression
{
    public BmExpression Left { get; set; }
    public TemporalBinaryOperator Operator { get; set; }
    public BmExpression Right { get; set; }

    public BmTemporalBinaryExpression(BmExpression left, TemporalBinaryOperator op, BmExpression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public override string ToExpressionString()
    {
        var opStr = Operator switch
        {
            TemporalBinaryOperator.Overlaps => "OVERLAPS",
            TemporalBinaryOperator.Contains => "CONTAINS",
            TemporalBinaryOperator.Precedes => "PRECEDES",
            TemporalBinaryOperator.Meets => "MEETS",
            _ => "?"
        };

        return $"({Left.ToExpressionString()} {opStr} {Right.ToExpressionString()})";
    }
}

/// <summary>
/// EXISTS expression: EXISTS (SELECT ... FROM ... WHERE ...)
/// </summary>
public class BmExistsExpression : BmExpression
{
    /// <summary>
    /// The raw select statement text from the DSL source.
    /// </summary>
    public string SelectStatement { get; set; } = "";

    /// <summary>
    /// Parsed AST of the select statement. Null if parsing failed.
    /// </summary>
    public Structure.BmSelectStatement? ParsedSelect { get; set; }

    public BmExistsExpression(string selectStatement)
    {
        SelectStatement = selectStatement;
    }

    public override string ToExpressionString()
    {
        return $"EXISTS ({SelectStatement})";
    }
}
