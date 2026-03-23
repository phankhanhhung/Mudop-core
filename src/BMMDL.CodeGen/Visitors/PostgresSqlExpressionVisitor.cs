using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using System.Text;

namespace BMMDL.CodeGen.Visitors;

/// <summary>
/// Delegate to resolve entity by name for association navigation.
/// </summary>
public delegate BmEntity? EntityResolver(string entityName);

/// <summary>
/// AST visitor that translates BMMDL expressions to PostgreSQL SQL.
/// This is the PROPER way to work with expressions - traverses AST directly, no string shortcuts.
/// </summary>
public class PostgresSqlExpressionVisitor : IExpressionVisitor<string>
{
    private readonly BmEntity _entity;
    private readonly FunctionMappingRegistry _functionRegistry;
    private readonly EntityResolver? _entityResolver;
    
    public PostgresSqlExpressionVisitor(BmEntity entity)
        : this(entity, new FunctionMappingRegistry(), null)
    {
    }
    
    public PostgresSqlExpressionVisitor(BmEntity entity, FunctionMappingRegistry functionRegistry)
        : this(entity, functionRegistry, null)
    {
    }
    
    public PostgresSqlExpressionVisitor(BmEntity entity, FunctionMappingRegistry functionRegistry, EntityResolver? entityResolver)
    {
        _entity = entity;
        _functionRegistry = functionRegistry;
        _entityResolver = entityResolver;
    }
    
    /// <summary>
    /// Main entry point - dispatches to appropriate visitor method
    /// </summary>
    public string Visit(BmExpression expression)
    {
        return expression switch
        {
            BmLiteralExpression lit => VisitLiteral(lit),
            BmIdentifierExpression id => VisitIdentifier(id),
            BmContextVariableExpression ctx => VisitContextVariable(ctx),
            BmParameterExpression param => VisitParameter(param),
            BmBinaryExpression bin => VisitBinary(bin),
            BmUnaryExpression un => VisitUnary(un),
            BmFunctionCallExpression func => VisitFunctionCall(func),
            BmCastExpression cast => VisitCast(cast),
            BmCaseExpression caseExpr => VisitCase(caseExpr),
            BmSubqueryExpression subquery => VisitSubquery(subquery),
            BmExistsExpression exists => VisitExists(exists),
            BmTemporalBinaryExpression temporal => VisitTemporalBinary(temporal),
            BmAggregateExpression agg => VisitAggregate(agg),
            BmWindowExpression win => VisitWindow(win),
            BmTernaryExpression ternary => VisitTernary(ternary),
            BmInExpression inExpr => VisitIn(inExpr),
            BmBetweenExpression between => VisitBetween(between),
            BmLikeExpression like => VisitLike(like),
            BmIsNullExpression isNull => VisitIsNull(isNull),
            BmParenExpression paren => VisitParen(paren),
            _ => throw new NotSupportedException(
                $"Expression type {expression.GetType().Name} is not supported for SQL translation")
        };
    }
    
    /// <summary>
    /// Visit literal: 'text', 123, 45.67, true, null, #EnumValue
    /// </summary>
    public string VisitLiteral(BmLiteralExpression literal)
    {
        return literal.Kind switch
        {
            BmLiteralKind.String => EscapeSqlString(literal.Value?.ToString() ?? ""),
            BmLiteralKind.Integer => literal.Value?.ToString() ?? "0",
            BmLiteralKind.Decimal => FormatDecimal(literal.Value),
            BmLiteralKind.Boolean => literal.Value is true ? "TRUE" : "FALSE",
            BmLiteralKind.Null => "NULL",
            BmLiteralKind.EnumValue => EscapeSqlString(literal.Value?.ToString() ?? ""),
            _ => throw new NotSupportedException($"Literal kind {literal.Kind} not supported")
        };
    }
    
    /// <summary>
    /// Visit identifier: fieldName -> field_name
    /// Handles association navigation: order.customer.name generates correlated subquery
    /// </summary>
    public string VisitIdentifier(BmIdentifierExpression identifier)
    {
        // Guard: empty path should not happen, but protect against index-out-of-range
        if (identifier.Path.Count == 0)
            return "unknown";

        // Single field reference
        if (identifier.Path.Count == 1)
        {
            var fieldName = identifier.Path[0];
            
            // Find field in entity
            var field = _entity.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field != null)
            {
                // Map to column name using naming convention
                return NamingConvention.GetColumnName(field.Name);
            }
            
            // Not found - could be a typo or cross-entity reference
            // Return as-is in snake_case (validation should catch errors)
            return NamingConvention.ToSnakeCase(fieldName);
        }
        
        // Multi-part path: association.field or nested.path
        // Try to resolve as association navigation
        if (_entityResolver != null && identifier.Path.Count >= 2)
        {
            var result = TryResolveAssociationNavigation(identifier.Path);
            if (result != null)
                return result;
        }
        
        // Fallback: convert all parts to snake_case (for simple table.column refs)
        var parts = identifier.Path.Select(p => NamingConvention.ToSnakeCase(p));
        return string.Join(".", parts);
    }
    
    /// <summary>
    /// Try to resolve multi-part path as association navigation.
    /// Example: order.customer.name -> (SELECT c."name" FROM "customer" c WHERE c."id" = "customer_id")
    /// </summary>
    private string? TryResolveAssociationNavigation(IReadOnlyList<string> path)
    {
        var currentEntity = _entity;
        string? fkColumn = null;
        
        // Walk the path, following associations
        for (int i = 0; i < path.Count - 1; i++)
        {
            var assocName = path[i];
            
            // Find association in current entity
            var assoc = currentEntity.Associations.FirstOrDefault(a => 
                a.Name.Equals(assocName, StringComparison.OrdinalIgnoreCase));
            
            if (assoc == null)
            {
                // Not an association - fallback to default behavior
                return null;
            }
            
            // Resolve target entity
            var targetEntity = _entityResolver!(assoc.TargetEntity);
            if (targetEntity == null)
            {
                // Target entity not found in resolver — return null to indicate navigation
                // couldn't be resolved. Caller (VisitIdentifier) will fall back to
                // snake_case string representation of the dotted path.
                return null;
            }
            
            // Store FK column for the subquery join condition
            // Convention: FK column is association_name + "_id" or target_entity + "_id"
            fkColumn = NamingConvention.GetFkColumnName(assocName);
            
            currentEntity = targetEntity;
        }
        
        // Last part is the actual field we want
        var fieldName = path[path.Count - 1];
        var field = currentEntity.Fields.FirstOrDefault(f => 
            f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        
        if (field == null)
        {
            // Field not found in target entity
            return null;
        }
        
        // Generate correlated subquery
        var tableName = NamingConvention.ToSnakeCase(currentEntity.Name);
        var columnName = NamingConvention.GetColumnName(field.Name);
        var alias = tableName[0].ToString().ToLower(); // First letter as alias
        
        // Find the primary key of the target entity
        var pkField = currentEntity.Fields.FirstOrDefault(f => f.IsKey);
        if (pkField == null)
            return null; // No primary key defined — let caller handle missing PK

        var pkColumn = NamingConvention.GetColumnName(pkField.Name);

        var quotedFkColumn = NamingConvention.QuoteIdentifier(fkColumn);
        return $"(SELECT {alias}.{NamingConvention.QuoteIdentifier(columnName)} FROM {NamingConvention.QuoteIdentifier(tableName)} {alias} WHERE {alias}.{NamingConvention.QuoteIdentifier(pkColumn)} = {quotedFkColumn})";
    }
    
    /// <summary>
    /// Visit context variable: $now, $user, $tenant
    /// </summary>
    public string VisitContextVariable(BmContextVariableExpression contextVar)
    {
        // Guard: empty path should not happen, but protect against index-out-of-range
        if (contextVar.Path.Count == 0)
            return "unknown";

        // Map common context variables to PostgreSQL equivalents
        var varName = contextVar.Path[0].ToLower();
        
        return varName switch
        {
            "now" => "NOW()",
            "today" => "CURRENT_DATE",
            "user" => "current_setting('app.user_id', true)",
            "tenant" => "current_setting('app.tenant_id', true)",
            "company" => "current_setting('app.company_id', true)",
            _ => $"current_setting('app.{NamingConvention.ToSnakeCase(varName)}', true)"
        };
    }
    
    /// <summary>
    /// Visit parameter: :customerId -> $1, $2, etc. (for prepared statements)
    /// </summary>
    public string VisitParameter(BmParameterExpression parameter)
    {
        // For computed fields, parameters shouldn't appear
        // But if they do, pass through with $ prefix (PostgreSQL placeholder style)
        return $":{parameter.Name}";
    }
    
    /// <summary>
    /// Visit binary expression: left op right
    /// </summary>
    public string VisitBinary(BmBinaryExpression binary)
    {
        var left = Visit(binary.Left);
        var right = Visit(binary.Right);
        var op = MapBinaryOperator(binary.Operator);
        
        // Handle operator precedence with parentheses
        // Wrap each side if it's a binary expression with lower precedence
        if (binary.Left is BmBinaryExpression leftBin && NeedsParen(leftBin.Operator, binary.Operator, isLeft: true))
        {
            left = $"({left})";
        }
        
        if (binary.Right is BmBinaryExpression rightBin && NeedsParen(rightBin.Operator, binary.Operator, isLeft: false))
        {
            right = $"({right})";
        }
        
        return $"{left} {op} {right}";
    }
    
    /// <summary>
    /// Visit unary expression: NOT x, -amount
    /// </summary>
    public string VisitUnary(BmUnaryExpression unary)
    {
        var operand = Visit(unary.Operand);
        
        return unary.Operator switch
        {
            BmUnaryOperator.Not => $"NOT {operand}",
            BmUnaryOperator.Negate => $"-{operand}",
            BmUnaryOperator.Plus => $"+{operand}",
            _ => throw new NotSupportedException($"Unary operator {unary.Operator} not supported")
        };
    }
    
    /// <summary>
    /// Visit function call: UPPER(name), SUBSTRING(text, 1, 10)
    /// </summary>
    public string VisitFunctionCall(BmFunctionCallExpression functionCall)
    {
        // Recursively translate arguments
        var translatedArgs = functionCall.Arguments
            .Select(arg => Visit(arg))
            .ToArray();
        
        // Look up function mapping in registry
        var mapping = _functionRegistry.GetMapping(functionCall.FunctionName);
        
        if (mapping != null)
        {
            // Use registered translator
            return mapping.Translate(translatedArgs);
        }
        
        // Fallback: pass-through with uppercase name
        var funcName = functionCall.FunctionName.ToUpper();
        return $"{funcName}({string.Join(", ", translatedArgs)})";
    }
    
    /// <summary>
    /// Visit type cast: price::INTEGER, CAST(amount AS DECIMAL)
    /// </summary>
    public string VisitCast(BmCastExpression cast)
    {
        var expr = Visit(cast.Expression);
        var targetType = MapTypeToPostgres(cast.TargetType.ToTypeString());
        
        // Use PostgreSQL :: syntax (simpler and more common)
        return $"({expr})::{targetType}";
    }
    
    /// <summary>
    /// Visit CASE expression: CASE WHEN ... THEN ... ELSE ... END
    /// </summary>
    public string VisitCase(BmCaseExpression caseExpr)
    {
        var sb = new StringBuilder("CASE");

        // Simple CASE form: CASE inputExpr WHEN value THEN result ...
        if (caseExpr.InputExpression != null)
        {
            sb.Append($" {Visit(caseExpr.InputExpression)}");
        }

        foreach (var (when, then) in caseExpr.WhenClauses)
        {
            var condSql = Visit(when);
            var resultSql = Visit(then);
            sb.Append($" WHEN {condSql} THEN {resultSql}");
        }
        
        if (caseExpr.ElseResult != null)
        {
            var elseResult = Visit(caseExpr.ElseResult);
            sb.Append($" ELSE {elseResult}");
        }
        
        sb.Append(" END");
        return sb.ToString();
    }

    public string VisitSubquery(BmSubqueryExpression subquery)
    {
        return $"({subquery.SelectStatement})";
    }

    public string VisitExists(BmExistsExpression exists)
    {
        return $"EXISTS ({exists.SelectStatement})";
    }

    /// <summary>
    /// Visit temporal binary expression: OVERLAPS, CONTAINS, PRECEDES, MEETS
    /// Each operand is expected to be a pair (start, end) represented as expressions.
    /// </summary>
    public string VisitTemporalBinary(BmTemporalBinaryExpression temporal)
    {
        var left = Visit(temporal.Left);
        var right = Visit(temporal.Right);

        return temporal.Operator switch
        {
            // OVERLAPS: Use PostgreSQL range overlap operator (&&) for tstzrange/daterange values.
            // PostgreSQL native OVERLAPS requires pair syntax: (start1, end1) OVERLAPS (start2, end2),
            // which doesn't work with single range expressions. The && operator works on range types
            // and is the correct way to check range overlap.
            TemporalBinaryOperator.Overlaps => $"({left} && {right})",
            // CONTAINS: first range fully contains second — PostgreSQL @> operator
            TemporalBinaryOperator.Contains => $"({left} @> {right})",
            // PRECEDES: first range entirely before second — PostgreSQL << operator
            TemporalBinaryOperator.Precedes => $"({left} << {right})",
            // MEETS: first range adjacent to second — PostgreSQL -|- operator
            TemporalBinaryOperator.Meets => $"({left} -|- {right})",
            _ => throw new NotSupportedException($"Temporal operator {temporal.Operator} not supported")
        };
    }

    /// <summary>
    /// Visit aggregate expression: COUNT(*), SUM(amount), AVG(DISTINCT price), COUNT(items WHERE status = 'Active')
    /// </summary>
    public string VisitAggregate(BmAggregateExpression aggregate)
    {
        var funcName = aggregate.Function switch
        {
            BmAggregateFunction.Count => "COUNT",
            BmAggregateFunction.Sum => "SUM",
            BmAggregateFunction.Avg => "AVG",
            BmAggregateFunction.Min => "MIN",
            BmAggregateFunction.Max => "MAX",
            BmAggregateFunction.StdDev => "STDDEV",
            BmAggregateFunction.Variance => "VARIANCE",
            _ => throw new NotSupportedException($"Aggregate function {aggregate.Function} not supported")
        };

        // COUNT(*) — no argument
        if (aggregate.Function == BmAggregateFunction.Count && aggregate.Argument == null)
        {
            if (aggregate.WhereCondition != null)
            {
                var whereClause = Visit(aggregate.WhereCondition);
                return $"{funcName}(*) FILTER (WHERE {whereClause})";
            }
            return $"{funcName}(*)";
        }

        var distinct = aggregate.IsDistinct ? "DISTINCT " : "";
        var arg = aggregate.Argument != null ? Visit(aggregate.Argument) : "*";

        if (aggregate.WhereCondition != null)
        {
            var whereClause = Visit(aggregate.WhereCondition);
            return $"{funcName}({distinct}{arg}) FILTER (WHERE {whereClause})";
        }

        return $"{funcName}({distinct}{arg})";
    }

    public string VisitWindow(BmWindowExpression window)
    {
        var args = window.FunctionArguments.Count > 0
            ? string.Join(", ", window.FunctionArguments.Select(a => Visit(a)))
            : "";
        
        var specParts = new List<string>();
        
        if (window.PartitionBy.Count > 0)
            specParts.Add("PARTITION BY " + string.Join(", ", window.PartitionBy.Select(p => Visit(p))));
        
        if (window.OrderBy.Count > 0)
        {
            var orderItems = window.OrderBy.Select(o =>
            {
                var sql = Visit(o.Expression);
                if (o.Descending) sql += " DESC";
                if (o.Nulls == NullsPosition.First) sql += " NULLS FIRST";
                else if (o.Nulls == NullsPosition.Last) sql += " NULLS LAST";
                return sql;
            });
            specParts.Add("ORDER BY " + string.Join(", ", orderItems));
        }
        
        if (window.Frame != null)
        {
            var frameSql = window.Frame.Type;
            if (window.Frame.End != null)
                frameSql += $" BETWEEN {FormatFrameBound(window.Frame.Start)} AND {FormatFrameBound(window.Frame.End)}";
            else
                frameSql += $" {FormatFrameBound(window.Frame.Start)}";
            specParts.Add(frameSql);
        }
        
        var spec = string.Join(" ", specParts);
        return $"{window.FunctionName}({args}) OVER ({spec})";
    }
    
    private string FormatFrameBound(BmFrameBound bound) => bound.BoundType switch
    {
        BmFrameBoundType.UnboundedPreceding => "UNBOUNDED PRECEDING",
        BmFrameBoundType.UnboundedFollowing => "UNBOUNDED FOLLOWING",
        BmFrameBoundType.CurrentRow => "CURRENT ROW",
        BmFrameBoundType.Preceding => $"{Visit(bound.Offset!)} PRECEDING",
        BmFrameBoundType.Following => $"{Visit(bound.Offset!)} FOLLOWING",
        _ => throw new NotSupportedException($"Frame bound type {bound.BoundType} not supported")
    };

    /// <summary>
    /// Visit ternary/conditional expression: condition ? then : else
    /// Maps to PostgreSQL CASE WHEN condition THEN thenExpr ELSE elseExpr END
    /// </summary>
    public string VisitTernary(BmTernaryExpression ternary)
    {
        var condition = Visit(ternary.Condition);
        var thenExpr = Visit(ternary.ThenExpression);
        var elseExpr = Visit(ternary.ElseExpression);

        return $"CASE WHEN {condition} THEN {thenExpr} ELSE {elseExpr} END";
    }

    /// <summary>
    /// Visit IN expression: expr [NOT] IN (value1, value2, ...)
    /// </summary>
    public string VisitIn(BmInExpression inExpr)
    {
        var expr = Visit(inExpr.Expression);
        var notStr = inExpr.IsNot ? "NOT " : "";

        // Subquery form: expr IN (SELECT ...)
        if (inExpr.Subquery != null)
        {
            var subquerySql = VisitSubquery(inExpr.Subquery);
            return $"{expr} {notStr}IN {subquerySql}";
        }

        // List form: expr IN (value1, value2, ...)
        var values = inExpr.Values.Select(v => Visit(v));
        return $"{expr} {notStr}IN ({string.Join(", ", values)})";
    }

    /// <summary>
    /// Visit BETWEEN expression: expr [NOT] BETWEEN low AND high
    /// </summary>
    public string VisitBetween(BmBetweenExpression between)
    {
        var expr = Visit(between.Expression);
        var low = Visit(between.Low);
        var high = Visit(between.High);
        var notStr = between.IsNot ? "NOT " : "";

        return $"{expr} {notStr}BETWEEN {low} AND {high}";
    }

    /// <summary>
    /// Visit LIKE expression: expr [NOT] LIKE pattern
    /// </summary>
    public string VisitLike(BmLikeExpression like)
    {
        var expr = Visit(like.Expression);
        var pattern = Visit(like.Pattern);
        var notStr = like.IsNot ? "NOT " : "";

        return $"{expr} {notStr}LIKE {pattern}";
    }

    /// <summary>
    /// Visit IS NULL expression: expr IS [NOT] NULL
    /// </summary>
    public string VisitIsNull(BmIsNullExpression isNull)
    {
        var expr = Visit(isNull.Expression);
        var notStr = isNull.IsNot ? "NOT " : "";

        return $"{expr} IS {notStr}NULL";
    }

    /// <summary>
    /// Visit parenthesized expression: (inner)
    /// </summary>
    public string VisitParen(BmParenExpression paren)
    {
        var inner = Visit(paren.Inner);
        return $"({inner})";
    }

    #region Helper Methods
    
    /// <summary>
    /// Escape SQL string literal with single quotes
    /// </summary>
    private string EscapeSqlString(string value)
    {
        // Escape single quotes by doubling them
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }
    
    /// <summary>
    /// Format decimal literal
    /// </summary>
    private string FormatDecimal(object? value)
    {
        if (value == null) return "0.0";
        
        // Use invariant culture to ensure . as decimal separator
        return Convert.ToDecimal(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    
    /// <summary>
    /// Map BmBinaryOperator enum to SQL operator string
    /// </summary>
    private string MapBinaryOperator(BmBinaryOperator op)
    {
        return op switch
        {
            // Arithmetic
            BmBinaryOperator.Add => "+",
            BmBinaryOperator.Subtract => "-",
            BmBinaryOperator.Multiply => "*",
            BmBinaryOperator.Divide => "/",
            BmBinaryOperator.Modulo => "%",
            
            // Comparison
            BmBinaryOperator.Equal => "=",
            BmBinaryOperator.NotEqual => "<>",
            BmBinaryOperator.LessThan => "<",
            BmBinaryOperator.GreaterThan => ">",
            BmBinaryOperator.LessOrEqual => "<=",
            BmBinaryOperator.GreaterOrEqual => ">=",
            
            // Logical
            BmBinaryOperator.And => "AND",
            BmBinaryOperator.Or => "OR",
            
            // String
            BmBinaryOperator.Concat => "||",
            
            _ => throw new NotSupportedException($"Binary operator {op} not supported")
        };
    }
    
    /// <summary>
    /// Check if parentheses are needed based on operator precedence
    /// </summary>
    private bool NeedsParen(BmBinaryOperator childOp, BmBinaryOperator parentOp, bool isLeft)
    {
        int childPrec = GetPrecedence(childOp);
        int parentPrec = GetPrecedence(parentOp);
        
        // Lower precedence always needs parens
        if (childPrec < parentPrec) return true;
        
        // Same precedence: right side needs parens for right-associative ops
        if (childPrec == parentPrec && !isLeft)
        {
            // Subtraction and division are left-associative
            return parentOp is BmBinaryOperator.Subtract or BmBinaryOperator.Divide;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get operator precedence (higher = tighter binding)
    /// </summary>
    private int GetPrecedence(BmBinaryOperator op)
    {
        return op switch
        {
            BmBinaryOperator.Or => 1,
            BmBinaryOperator.And => 2,
            BmBinaryOperator.Equal or BmBinaryOperator.NotEqual or
            BmBinaryOperator.LessThan or BmBinaryOperator.GreaterThan or
            BmBinaryOperator.LessOrEqual or BmBinaryOperator.GreaterOrEqual => 3,
            BmBinaryOperator.Add or BmBinaryOperator.Subtract or BmBinaryOperator.Concat => 4,
            BmBinaryOperator.Multiply or BmBinaryOperator.Divide or BmBinaryOperator.Modulo => 5,
            _ => 0
        };
    }
    
    /// <summary>
    /// Map BMMDL type names to PostgreSQL types
    /// </summary>
    private string MapTypeToPostgres(string bmmdlType)
    {
        return bmmdlType.ToUpper() switch
        {
            "STRING" => "TEXT",
            "INTEGER" => "INTEGER",
            "DECIMAL" => "NUMERIC",
            "BOOLEAN" => "BOOLEAN",
            "DATE" => "DATE",
            "DATETIME" => "TIMESTAMP",
            "UUID" => "UUID",
            _ => bmmdlType.ToLower() // Pass through custom types
        };
    }
    
    #endregion
}
