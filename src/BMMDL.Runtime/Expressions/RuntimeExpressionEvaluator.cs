namespace BMMDL.Runtime.Expressions;

using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Utilities;

/// <summary>
/// Runtime expression evaluator that evaluates BmExpression AST at runtime.
/// Implements IExpressionVisitor to traverse and evaluate expression trees.
/// </summary>
public class RuntimeExpressionEvaluator : IRuntimeExpressionEvaluator, IExpressionVisitor<object?>
{
    private readonly FunctionRegistry _functions;
    private readonly bool _strictTypeConversion;
    // AsyncLocal properly flows context across async/await boundaries
    // Unlike ThreadLocal, this works correctly when an async operation resumes on a different thread
    private readonly AsyncLocal<EvaluationContext> _context = new();
    
    /// <summary>
    /// Get the current evaluation context, falling back to empty if not set.
    /// </summary>
    private EvaluationContext CurrentContext => _context.Value ?? EvaluationContext.Empty();

    public RuntimeExpressionEvaluator() : this(new FunctionRegistry(), strictTypeConversion: false) { }

    public RuntimeExpressionEvaluator(FunctionRegistry functions, bool strictTypeConversion = false)
    {
        _functions = functions;
        _strictTypeConversion = strictTypeConversion;
    }

    /// <summary>
    /// Evaluate an expression with the given context.
    /// </summary>
    public object? Evaluate(BmExpression expression, EvaluationContext context)
    {
        var previous = _context.Value;
        _context.Value = context;
        try
        {
            return Visit(expression);
        }
        finally
        {
            _context.Value = previous;
        }
    }

    /// <summary>
    /// Evaluate an expression with entity data only.
    /// </summary>
    public object? Evaluate(BmExpression expression, Dictionary<string, object?> entityData)
    {
        return Evaluate(expression, EvaluationContext.FromEntity(entityData));
    }

    /// <summary>
    /// Evaluate an expression asynchronously with the given context.
    /// Required for expressions that may contain aggregate sub-expressions
    /// which need async DB access.
    /// </summary>
    public async Task<object?> EvaluateAsync(BmExpression expression, EvaluationContext context)
    {
        var previous = _context.Value;
        _context.Value = context;
        try
        {
            return await VisitAsync(expression);
        }
        finally
        {
            _context.Value = previous;
        }
    }

    /// <summary>
    /// Dispatcher: visit any expression type (synchronous).
    /// </summary>
    public object? Visit(BmExpression expression)
    {
        return expression switch
        {
            BmLiteralExpression literal => VisitLiteral(literal),
            BmIdentifierExpression identifier => VisitIdentifier(identifier),
            BmContextVariableExpression contextVar => VisitContextVariable(contextVar),
            BmParameterExpression parameter => VisitParameter(parameter),
            BmBinaryExpression binary => VisitBinary(binary),
            BmUnaryExpression unary => VisitUnary(unary),
            BmFunctionCallExpression funcCall => VisitFunctionCall(funcCall),
            BmCastExpression cast => VisitCast(cast),
            BmCaseExpression caseExpr => VisitCase(caseExpr),
            BmTernaryExpression ternary => VisitTernary(ternary),
            BmInExpression inExpr => VisitIn(inExpr),
            BmBetweenExpression between => VisitBetween(between),
            BmLikeExpression like => VisitLike(like),
            BmIsNullExpression isNull => VisitIsNull(isNull),
            BmParenExpression paren => Visit(paren.Inner),
            BmAggregateExpression aggregate => VisitAggregate(aggregate),
            BmTemporalBinaryExpression temporal => VisitTemporalBinary(temporal),
            BmSubqueryExpression => throw new NotSupportedException("Subquery expressions are only supported in SQL context (views, computed columns)"),
            BmExistsExpression => throw new NotSupportedException("EXISTS expressions are only supported in SQL context (views, computed columns)"),
            BmWindowExpression => throw new NotSupportedException("Window function expressions are only supported in SQL context (views, SELECT statements)"),
            _ => throw new NotSupportedException($"Expression type not supported: {expression.GetType().Name}")
        };
    }

    /// <summary>
    /// Async dispatcher: handles aggregate expressions asynchronously while delegating
    /// all other expression types to the synchronous visitor.
    /// This avoids sync-over-async deadlock risk when evaluating aggregate sub-expressions.
    /// </summary>
    private async Task<object?> VisitAsync(BmExpression expression)
    {
        return expression switch
        {
            // Aggregate needs true async — it queries the database
            BmAggregateExpression aggregate => await VisitAggregateAsync(aggregate),
            // Binary/Unary/Ternary/Case may contain nested aggregates, so walk them async
            BmBinaryExpression binary => await VisitBinaryAsync(binary),
            BmUnaryExpression unary => await VisitUnaryAsync(unary),
            BmTernaryExpression ternary => await VisitTernaryAsync(ternary),
            BmCaseExpression caseExpr => await VisitCaseAsync(caseExpr),
            BmParenExpression paren => await VisitAsync(paren.Inner),
            BmInExpression inExpr => await VisitInAsync(inExpr),
            BmFunctionCallExpression funcCall => await VisitFunctionCallAsync(funcCall),
            BmCastExpression cast => await VisitCastAsync(cast),
            BmBetweenExpression between => await VisitBetweenAsync(between),
            // Leaf expressions and unsupported types — delegate to sync visitor (no I/O)
            _ => Visit(expression)
        };
    }

    private async Task<object?> VisitBinaryAsync(BmBinaryExpression binary)
    {
        var left = await VisitAsync(binary.Left);
        var right = await VisitAsync(binary.Right);

        return binary.Operator switch
        {
            BmBinaryOperator.Add => Add(left, right),
            BmBinaryOperator.Subtract => Subtract(left, right),
            BmBinaryOperator.Multiply => Multiply(left, right),
            BmBinaryOperator.Divide => Divide(left, right),
            BmBinaryOperator.Modulo => Modulo(left, right),
            BmBinaryOperator.Equal => Equals(left, right),
            BmBinaryOperator.NotEqual => !Equals(left, right),
            BmBinaryOperator.LessThan => Compare(left, right) < 0,
            BmBinaryOperator.GreaterThan => Compare(left, right) > 0,
            BmBinaryOperator.LessOrEqual => Compare(left, right) <= 0,
            BmBinaryOperator.GreaterOrEqual => Compare(left, right) >= 0,
            BmBinaryOperator.And => ToBool(left) && ToBool(right),
            BmBinaryOperator.Or => ToBool(left) || ToBool(right),
            BmBinaryOperator.Concat => Concat(left, right),
            _ => throw new NotSupportedException($"Operator not supported: {binary.Operator}")
        };
    }

    private async Task<object?> VisitUnaryAsync(BmUnaryExpression unary)
    {
        var operand = await VisitAsync(unary.Operand);
        return unary.Operator switch
        {
            BmUnaryOperator.Not => !ToBool(operand),
            BmUnaryOperator.Negate => Negate(operand),
            BmUnaryOperator.Plus => operand,
            _ => throw new NotSupportedException($"Unary operator not supported: {unary.Operator}")
        };
    }

    private async Task<object?> VisitTernaryAsync(BmTernaryExpression ternary)
    {
        var condition = ToBool(await VisitAsync(ternary.Condition));
        return condition ? await VisitAsync(ternary.ThenExpression) : await VisitAsync(ternary.ElseExpression);
    }

    private async Task<object?> VisitCaseAsync(BmCaseExpression caseExpr)
    {
        if (caseExpr.InputExpression != null)
        {
            var input = await VisitAsync(caseExpr.InputExpression);
            foreach (var (when, then) in caseExpr.WhenClauses)
            {
                var whenValue = await VisitAsync(when);
                if (Equals(input, whenValue))
                    return await VisitAsync(then);
            }
        }
        else
        {
            foreach (var (when, then) in caseExpr.WhenClauses)
            {
                var condition = ToBool(await VisitAsync(when));
                if (condition)
                    return await VisitAsync(then);
            }
        }
        return caseExpr.ElseResult != null ? await VisitAsync(caseExpr.ElseResult) : null;
    }

    private async Task<object?> VisitInAsync(BmInExpression inExpr)
    {
        var value = await VisitAsync(inExpr.Expression);
        var values = new List<object?>();
        foreach (var v in inExpr.Values)
            values.Add(await VisitAsync(v));
        var contains = values.Any(v => Equals(value, v));
        return inExpr.IsNot ? !contains : contains;
    }

    private async Task<object?> VisitFunctionCallAsync(BmFunctionCallExpression funcCall)
    {
        var args = new object?[funcCall.Arguments.Count];
        for (var i = 0; i < funcCall.Arguments.Count; i++)
            args[i] = await VisitAsync(funcCall.Arguments[i]);
        return _functions.Invoke(funcCall.FunctionName, args);
    }

    private async Task<object?> VisitCastAsync(BmCastExpression cast)
    {
        var value = await VisitAsync(cast.Expression);
        if (value == null) return null;

        var targetTypeName = cast.TargetType.ToTypeString().ToUpperInvariant();
        return targetTypeName switch
        {
            "STRING" or "VARCHAR" or "TEXT" => value.ToString(),
            "INTEGER" or "INT" or "INT32" => ToInt(value),
            "LONG" or "INT64" or "BIGINT" => ToLong(value),
            "DECIMAL" or "NUMERIC" => ToDecimal(value),
            "DOUBLE" or "FLOAT" => ToDouble(value),
            "BOOLEAN" or "BOOL" => ToBool(value),
            "DATE" => ToDate(value),
            "DATETIME" or "TIMESTAMP" => ToDateTime(value),
            "UUID" or "GUID" => ToGuid(value),
            _ => value
        };
    }

    private async Task<object?> VisitBetweenAsync(BmBetweenExpression between)
    {
        var value = await VisitAsync(between.Expression);
        var low = await VisitAsync(between.Low);
        var high = await VisitAsync(between.High);
        var inRange = Compare(value, low) >= 0 && Compare(value, high) <= 0;
        return between.IsNot ? !inRange : inRange;
    }

    // ==================== LITERAL ====================

    public object? VisitLiteral(BmLiteralExpression literal)
    {
        var result = literal.Kind switch
        {
            BmLiteralKind.String => literal.Value?.ToString(),
            BmLiteralKind.Integer => literal.Value,
            BmLiteralKind.Decimal => literal.Value,
            BmLiteralKind.Boolean => literal.Value,
            BmLiteralKind.Null => null,
            BmLiteralKind.EnumValue => literal.Value?.ToString(), // Return enum member as string
            _ => literal.Value
        };
        return result;
    }

    // ==================== IDENTIFIER ====================

    private const int MaxNavigationDepth = 20;

    public object? VisitIdentifier(BmIdentifierExpression identifier)
    {
        if (identifier.Path.Count == 0) return null;

        // Single segment: look up in entity data first, then parameters
        if (identifier.IsSimple)
        {
            var fieldName = identifier.Root;

            // Try EntityData first (exact match)
            if (CurrentContext.EntityData.TryGetValue(fieldName, out var value))
                return value;

            // Try PascalCase version
            var pascalCase = NamingConvention.ToPascalCase(fieldName);
            if (CurrentContext.EntityData.TryGetValue(pascalCase, out value))
                return value;

            // Try snake_case version in EntityData
            var snakeCase = NamingConvention.ToSnakeCase(fieldName);
            if (CurrentContext.EntityData.TryGetValue(snakeCase, out value))
                return value;

            // Try case-insensitive lookup
            var match = CurrentContext.EntityData.Keys.FirstOrDefault(k =>
                string.Equals(k, fieldName, StringComparison.OrdinalIgnoreCase));
            if (match != null && CurrentContext.EntityData.TryGetValue(match, out value))
            {
                return value;
            }

            // Fallback to Parameters (for let variables, action parameters, etc.)
            if (CurrentContext.Parameters.TryGetValue(fieldName, out value))
                return value;

            // Try PascalCase in Parameters
            if (CurrentContext.Parameters.TryGetValue(pascalCase, out value))
                return value;

            // Try snake_case in Parameters
            if (CurrentContext.Parameters.TryGetValue(snakeCase, out value))
                return value;

            return null;
        }

        // Multi-segment: navigate through related entities
        return ResolveNavigationPath(identifier.Path);
    }

    private object? ResolveNavigationPath(List<string> path, int depth = 0)
    {
        if (depth > MaxNavigationDepth)
            throw new InvalidOperationException($"Navigation path exceeded maximum depth of {MaxNavigationDepth}. Possible circular reference.");

        if (path.Count == 0) return null;

        var root = path[0];

        // Check if root is in entity data (could be a nested object)
        if (CurrentContext.EntityData.TryGetValue(root, out var rootValue))
        {
            if (path.Count == 1) return rootValue;

            // Navigate through nested object
            if (rootValue is Dictionary<string, object?> nestedDict)
            {
                var remainingPath = path.Skip(1).ToList();
                return NavigateDict(nestedDict, remainingPath, depth + 1);
            }
        }

        // Check related entities
        if (CurrentContext.RelatedEntities.TryGetValue(root, out var relatedEntity))
        {
            var remainingPath = path.Skip(1).ToList();
            return NavigateDict(relatedEntity, remainingPath, depth + 1);
        }

        return null;
    }

    private object? NavigateDict(Dictionary<string, object?> dict, List<string> path, int depth = 0)
    {
        if (depth > MaxNavigationDepth)
            throw new InvalidOperationException($"Navigation path exceeded maximum depth of {MaxNavigationDepth}. Possible circular reference.");

        if (path.Count == 0) return null;

        var key = path[0];
        if (!dict.TryGetValue(key, out var value))
        {
            // Try snake_case
            key = NamingConvention.ToSnakeCase(key);
            if (!dict.TryGetValue(key, out value))
                return null;
        }

        if (path.Count == 1) return value;

        // Continue navigation
        if (value is Dictionary<string, object?> nestedDict)
        {
            return NavigateDict(nestedDict, path.Skip(1).ToList(), depth + 1);
        }

        return null;
    }

    // ==================== CONTEXT VARIABLE ====================

    public object? VisitContextVariable(BmContextVariableExpression contextVar)
    {
        if (contextVar.Path.Count == 0) return null;

        var root = contextVar.Root.ToLowerInvariant();
        
        return root switch
        {
            "now" => CurrentContext.EvaluationTime,
            "today" => CurrentContext.EvaluationTime.Date,
            "tenant" or "tenantid" => CurrentContext.TenantId,
            "user" => ResolveUserPath(contextVar.Path.Skip(1).ToList()),
            "old" => ResolveOldPath(contextVar.Path.Skip(1).ToList()),
            _ => null
        };
    }

    /// <summary>
    /// Resolve $old.fieldName — previous entity data before an update.
    /// </summary>
    private object? ResolveOldPath(List<string> pathSegments)
    {
        if (CurrentContext.OldEntityData == null || pathSegments.Count == 0)
            return null;
        
        var fieldName = pathSegments[0];
        
        // OldEntityData is created with StringComparer.OrdinalIgnoreCase,
        // so TryGetValue handles case-insensitive matching directly
        if (CurrentContext.OldEntityData.TryGetValue(fieldName, out var value))
            return value;
        
        return null;
    }

    private object? ResolveUserPath(List<string> path)
    {
        if (CurrentContext.User == null) return null;
        if (path.Count == 0) return CurrentContext.User;
        
        return CurrentContext.User.GetProperty(path[0]);
    }

    // ==================== PARAMETER ====================

    public object? VisitParameter(BmParameterExpression parameter)
    {
        if (CurrentContext.Parameters.TryGetValue(parameter.Name, out var value))
            return value;
        
        return null;
    }

    // ==================== BINARY ====================

    public object? VisitBinary(BmBinaryExpression binary)
    {
        var left = Visit(binary.Left);
        var right = Visit(binary.Right);
        

        return binary.Operator switch
        {
            // Arithmetic
            BmBinaryOperator.Add => Add(left, right),
            BmBinaryOperator.Subtract => Subtract(left, right),
            BmBinaryOperator.Multiply => Multiply(left, right),
            BmBinaryOperator.Divide => Divide(left, right),
            BmBinaryOperator.Modulo => Modulo(left, right),
            
            // Comparison
            BmBinaryOperator.Equal => Equals(left, right),
            BmBinaryOperator.NotEqual => !Equals(left, right),
            BmBinaryOperator.LessThan => Compare(left, right) < 0,
            BmBinaryOperator.GreaterThan => Compare(left, right) > 0,
            BmBinaryOperator.LessOrEqual => Compare(left, right) <= 0,
            BmBinaryOperator.GreaterOrEqual => Compare(left, right) >= 0,
            
            // Logical
            BmBinaryOperator.And => ToBool(left) && ToBool(right),
            BmBinaryOperator.Or => ToBool(left) || ToBool(right),
            
            // String
            BmBinaryOperator.Concat => Concat(left, right),
            
            _ => throw new NotSupportedException($"Operator not supported: {binary.Operator}")
        };
    }

    // ==================== UNARY ====================

    public object? VisitUnary(BmUnaryExpression unary)
    {
        var operand = Visit(unary.Operand);

        return unary.Operator switch
        {
            BmUnaryOperator.Not => !ToBool(operand),
            BmUnaryOperator.Negate => Negate(operand),
            BmUnaryOperator.Plus => operand,
            _ => throw new NotSupportedException($"Unary operator not supported: {unary.Operator}")
        };
    }

    // ==================== FUNCTION CALL ====================

    public object? VisitFunctionCall(BmFunctionCallExpression funcCall)
    {
        var args = funcCall.Arguments.Select(Visit).ToArray();
        return _functions.Invoke(funcCall.FunctionName, args);
    }

    // ==================== CAST ====================

    public object? VisitCast(BmCastExpression cast)
    {
        var value = Visit(cast.Expression);
        if (value == null) return null;

        var targetTypeName = cast.TargetType.ToTypeString().ToUpperInvariant();
        
        return targetTypeName switch
        {
            "STRING" or "VARCHAR" or "TEXT" => value.ToString(),
            "INTEGER" or "INT" or "INT32" => ToInt(value),
            "LONG" or "INT64" or "BIGINT" => ToLong(value),
            "DECIMAL" or "NUMERIC" => ToDecimal(value),
            "DOUBLE" or "FLOAT" => ToDouble(value),
            "BOOLEAN" or "BOOL" => ToBool(value),
            "DATE" => ToDate(value),
            "DATETIME" or "TIMESTAMP" => ToDateTime(value),
            "UUID" or "GUID" => ToGuid(value),
            _ => value
        };
    }

    // ==================== CASE ====================

    public object? VisitCase(BmCaseExpression caseExpr)
    {
        // Simple CASE: CASE input WHEN value THEN result ...
        if (caseExpr.InputExpression != null)
        {
            var input = Visit(caseExpr.InputExpression);
            foreach (var (when, then) in caseExpr.WhenClauses)
            {
                var whenValue = Visit(when);
                if (Equals(input, whenValue))
                    return Visit(then);
            }
        }
        // Searched CASE: CASE WHEN condition THEN result ...
        else
        {
            foreach (var (when, then) in caseExpr.WhenClauses)
            {
                var condition = ToBool(Visit(when));
                if (condition)
                    return Visit(then);
            }
        }

        // ELSE clause
        return caseExpr.ElseResult != null ? Visit(caseExpr.ElseResult) : null;
    }

    // ==================== TERNARY ====================

    public object? VisitTernary(BmTernaryExpression ternary)
    {
        var condition = ToBool(Visit(ternary.Condition));
        return condition ? Visit(ternary.ThenExpression) : Visit(ternary.ElseExpression);
    }

    // ==================== IN ====================

    public object? VisitIn(BmInExpression inExpr)
    {
        var value = Visit(inExpr.Expression);
        var values = inExpr.Values.Select(Visit);
        
        var contains = values.Any(v => Equals(value, v));
        return inExpr.IsNot ? !contains : contains;
    }

    // ==================== BETWEEN ====================

    public object? VisitBetween(BmBetweenExpression between)
    {
        var value = Visit(between.Expression);
        var low = Visit(between.Low);
        var high = Visit(between.High);

        var inRange = Compare(value, low) >= 0 && Compare(value, high) <= 0;
        return between.IsNot ? !inRange : inRange;
    }

    // ==================== LIKE ====================

    public object? VisitLike(BmLikeExpression like)
    {
        var value = Visit(like.Expression)?.ToString() ?? "";
        var pattern = Visit(like.Pattern)?.ToString() ?? "";

        // Convert SQL LIKE pattern to regex
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("%", ".*")
            .Replace("_", ".") + "$";
        
        // Use case-sensitive matching to match PostgreSQL LIKE behavior
        var matches = System.Text.RegularExpressions.Regex.IsMatch(value, regex);
        
        return like.IsNot ? !matches : matches;
    }

    // ==================== IS NULL ====================

    public object? VisitIsNull(BmIsNullExpression isNull)
    {
        var value = Visit(isNull.Expression);
        var isNullResult = value == null;
        return isNull.IsNot ? !isNullResult : isNullResult;
    }

    // ==================== AGGREGATE ====================

    public object? VisitAggregate(BmAggregateExpression aggregate)
    {
        var resolver = CurrentContext.AggregateResolver;
        if (resolver == null)
            throw new NotSupportedException("Aggregate expressions require an AggregateResolver in the evaluation context. Use RuleEngine for rules with aggregates.");

        // Fallback for sync callers — prefer EvaluateAsync to avoid this path
        return resolver(aggregate, CurrentContext).GetAwaiter().GetResult();
    }

    private async Task<object?> VisitAggregateAsync(BmAggregateExpression aggregate)
    {
        var resolver = CurrentContext.AggregateResolver;
        if (resolver == null)
            throw new NotSupportedException("Aggregate expressions require an AggregateResolver in the evaluation context. Use RuleEngine for rules with aggregates.");

        return await resolver(aggregate, CurrentContext);
    }

    // ==================== TEMPORAL BINARY ====================

    public object? VisitTemporalBinary(BmTemporalBinaryExpression temporal)
    {
        var left = Visit(temporal.Left);
        var right = Visit(temporal.Right);

        // Both sides should resolve to DateTime values representing interval boundaries.
        // In a typical usage, the left/right are identifiers pointing to date/datetime fields.
        // For interval operators, we compare the temporal values directly.
        var leftDt = ToDateTime(left);
        var rightDt = ToDateTime(right);

        if (leftDt == null || rightDt == null)
            return null;

        // Note: These are single-value comparisons for the common case where left/right
        // are individual date/time values from entity fields. For full interval overlap
        // semantics (left_start <= right_end AND right_start <= left_end), both intervals
        // need start/end pairs which requires evaluation of two fields per side. This is
        // handled at the SQL level via DynamicSqlBuilder for query-time evaluation.
        // Here we provide sensible single-value semantics:
        return temporal.Operator switch
        {
            // Single-value overlap: left date is before or at right date (i.e. periods could overlap)
            TemporalBinaryOperator.Overlaps => leftDt.Value <= rightDt.Value,
            // Contains: left value encompasses or equals right value
            TemporalBinaryOperator.Contains => leftDt.Value >= rightDt.Value,
            // Precedes: left strictly before right
            TemporalBinaryOperator.Precedes => leftDt.Value < rightDt.Value,
            // Meets: left exactly equals right (end of one period meets start of another)
            TemporalBinaryOperator.Meets => leftDt.Value == rightDt.Value,
            _ => throw new NotSupportedException($"Temporal operator {temporal.Operator} not supported")
        };
    }

    // ==================== HELPER METHODS ====================

    private object? Add(object? left, object? right)
    {
        if (left == null || right == null) return null;
        
        // String concatenation
        if (left is string || right is string)
            return Concat(left, right);
        
        return ToDecimal(left) + ToDecimal(right);
    }

    private object? Subtract(object? left, object? right)
    {
        if (left == null || right == null) return null;
        return ToDecimal(left) - ToDecimal(right);
    }

    private object? Multiply(object? left, object? right)
    {
        if (left == null || right == null) return null;
        return ToDecimal(left) * ToDecimal(right);
    }

    private object? Divide(object? left, object? right)
    {
        if (left == null || right == null) return null;
        var rightVal = ToDecimal(right);
        if (rightVal == 0) throw new InvalidOperationException("Division by zero in expression evaluation");
        return ToDecimal(left) / rightVal;
    }

    private object? Modulo(object? left, object? right)
    {
        if (left == null || right == null) return null;
        var rightVal = ToDecimal(right);
        if (rightVal == 0) throw new InvalidOperationException("Division by zero in expression evaluation");
        return ToDecimal(left) % rightVal;
    }

    private object? Negate(object? operand)
    {
        if (operand == null) return null;
        return -ToDecimal(operand);
    }

    private static string Concat(object? left, object? right)
    {
        return (left?.ToString() ?? "") + (right?.ToString() ?? "");
    }

    private int Compare(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        // Compare by type
        if (left is string sLeft && right is string sRight)
            return string.Compare(sLeft, sRight, StringComparison.Ordinal);
        
        if (left is DateTime dtLeft && right is DateTime dtRight)
            return dtLeft.CompareTo(dtRight);

        // Numeric comparison
        return ToDecimal(left).CompareTo(ToDecimal(right));
    }

    private new bool Equals(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        // Type-aware comparison
        if (left.GetType() == right.GetType())
            return left.Equals(right);

        // String comparison
        if (left is string || right is string)
            return string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);

        // Numeric comparison
        if (IsNumeric(left) && IsNumeric(right))
            return ToDecimal(left) == ToDecimal(right);

        // GUID comparison
        if (left is Guid gLeft && right is Guid gRight)
            return gLeft == gRight;
        if ((left is Guid || right is Guid) && (left is string || right is string))
        {
            var leftGuid = ToGuid(left);
            var rightGuid = ToGuid(right);
            return leftGuid == rightGuid;
        }

        return left.Equals(right);
    }

    private static bool IsNumeric(object? value)
    {
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind == System.Text.Json.JsonValueKind.Number;
        }
        return value is int or long or decimal or double or float or short or byte;
    }

    /// <summary>
    /// Handle a type conversion failure by either throwing (strict mode) or returning default (lenient mode).
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="value">The value that couldn't be converted.</param>
    /// <param name="defaultValue">The default value to return in lenient mode.</param>
    /// <returns>The default value in lenient mode.</returns>
    /// <exception cref="InvalidCastException">Thrown in strict mode when conversion fails.</exception>
    private T HandleConversionFailure<T>(object? value, T defaultValue)
    {
        if (_strictTypeConversion)
        {
            var typeName = value?.GetType().Name ?? "null";
            throw new InvalidCastException(
                $"Cannot convert value of type '{typeName}' to {typeof(T).Name}. " +
                $"Value: {value ?? "(null)"}");
        }
        return defaultValue;
    }

    private decimal ToDecimal(object? value)
    {
        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => je.GetDecimal(),
                System.Text.Json.JsonValueKind.String => decimal.TryParse(je.GetString(), out var d) ? d : HandleConversionFailure<decimal>(value, 0m),
                _ => HandleConversionFailure<decimal>(value, 0m)
            };
        }
        
        return value switch
        {
            null => 0m,
            decimal d => d,
            double dbl => dbl > (double)decimal.MaxValue || dbl < (double)decimal.MinValue || double.IsNaN(dbl) || double.IsInfinity(dbl)
                ? HandleConversionFailure<decimal>(value, 0m) : (decimal)dbl,
            float f => float.IsNaN(f) || float.IsInfinity(f)
                ? HandleConversionFailure<decimal>(value, 0m) : (decimal)f,
            int i => i,
            long l => l,
            short sh => sh,
            byte b => b,
            string s => decimal.TryParse(s, out var result) ? result : HandleConversionFailure<decimal>(value, 0m),
            _ => HandleConversionFailure<decimal>(value, 0m)
        };
    }

    private double ToDouble(object? value)
    {
        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => je.GetDouble(),
                System.Text.Json.JsonValueKind.String => double.TryParse(je.GetString(), out var d) ? d : HandleConversionFailure<double>(value, 0.0),
                _ => HandleConversionFailure<double>(value, 0.0)
            };
        }
        
        return value switch
        {
            null => 0.0,
            double d => d,
            decimal dec => (double)dec,
            float f => f,
            int i => i,
            long l => l,
            short sh => sh,
            byte b => b,
            string s => double.TryParse(s, out var result) ? result : HandleConversionFailure<double>(value, 0.0),
            _ => HandleConversionFailure<double>(value, 0.0)
        };
    }

    private int ToInt(object? value)
    {
        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => je.GetInt32(),
                System.Text.Json.JsonValueKind.String => int.TryParse(je.GetString(), out var i) ? i : HandleConversionFailure<int>(value, 0),
                _ => HandleConversionFailure<int>(value, 0)
            };
        }
        
        return value switch
        {
            null => 0,
            int i => i,
            long l => l > int.MaxValue || l < int.MinValue
                ? HandleConversionFailure<int>(value, 0) : (int)l,
            short sh => sh,
            byte b => b,
            decimal d => d > int.MaxValue || d < int.MinValue
                ? HandleConversionFailure<int>(value, 0) : (int)d,
            double dbl => dbl > int.MaxValue || dbl < int.MinValue || double.IsNaN(dbl) || double.IsInfinity(dbl)
                ? HandleConversionFailure<int>(value, 0) : (int)dbl,
            float f => f > int.MaxValue || f < int.MinValue || float.IsNaN(f) || float.IsInfinity(f)
                ? HandleConversionFailure<int>(value, 0) : (int)f,
            string s => int.TryParse(s, out var result) ? result : HandleConversionFailure<int>(value, 0),
            _ => HandleConversionFailure<int>(value, 0)
        };
    }

    private long ToLong(object? value)
    {
        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => je.GetInt64(),
                System.Text.Json.JsonValueKind.String => long.TryParse(je.GetString(), out var l) ? l : HandleConversionFailure<long>(value, 0L),
                _ => HandleConversionFailure<long>(value, 0L)
            };
        }
        
        return value switch
        {
            null => 0L,
            long l => l,
            int i => i,
            short sh => sh,
            byte b => b,
            decimal d => d > long.MaxValue || d < long.MinValue
                ? HandleConversionFailure<long>(value, 0L) : (long)d,
            double dbl => dbl > long.MaxValue || dbl < long.MinValue || double.IsNaN(dbl) || double.IsInfinity(dbl)
                ? HandleConversionFailure<long>(value, 0L) : (long)dbl,
            float f => f > long.MaxValue || f < long.MinValue || float.IsNaN(f) || float.IsInfinity(f)
                ? HandleConversionFailure<long>(value, 0L) : (long)f,
            string s => long.TryParse(s, out var result) ? result : HandleConversionFailure<long>(value, 0L),
            _ => HandleConversionFailure<long>(value, 0L)
        };
    }

    private static bool ToBool(object? value)
    {
        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number => je.GetInt32() != 0,
                System.Text.Json.JsonValueKind.String => bool.TryParse(je.GetString(), out var b) && b,
                _ => false
            };
        }
        
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s => bool.TryParse(s, out var result) && result,
            _ => false
        };
    }

    private static DateTime? ToDateTime(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dto => dto.UtcDateTime,
            string s => DateTime.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static DateOnly? ToDate(object? value)
    {
        return value switch
        {
            null => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static Guid? ToGuid(object? value)
    {
        return value switch
        {
            null => null,
            Guid g => g,
            string s => Guid.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

}
