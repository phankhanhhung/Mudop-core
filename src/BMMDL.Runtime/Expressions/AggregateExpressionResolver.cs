namespace BMMDL.Runtime.Expressions;

using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Resolves aggregate expressions (COUNT, SUM, AVG, MIN, MAX) by executing
/// parameterized SQL subqueries against the database.
/// Used by the rule engine to evaluate aggregates in business rule conditions.
/// </summary>
public class AggregateExpressionResolver
{
    private readonly IMetaModelCache _cache;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<AggregateExpressionResolver> _logger;
    private readonly string? _defaultSchema;

    public AggregateExpressionResolver(
        IMetaModelCache cache,
        IQueryExecutor queryExecutor,
        ILogger<AggregateExpressionResolver> logger,
        string? defaultSchema = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultSchema = defaultSchema;
    }

    /// <summary>
    /// Resolve an aggregate expression by building and executing a SQL subquery.
    /// </summary>
    public async Task<object?> ResolveAsync(BmAggregateExpression aggregate, EvaluationContext context)
    {
        if (string.IsNullOrEmpty(context.EntityName))
            throw new InvalidOperationException("EntityName must be set in context for aggregate resolution.");

        var parentEntity = _cache.GetEntity(context.EntityName);
        if (parentEntity == null)
            throw new InvalidOperationException($"Entity '{context.EntityName}' not found in meta-model cache.");

        // Extract the navigation property name and optional field from the argument
        var (navigationName, fieldName) = ParseAggregateArgument(aggregate);

        // Find the navigation (composition or association) on the parent entity
        var (childEntity, fkColumnName) = ResolveNavigation(parentEntity, navigationName);

        // Get the parent entity's ID from context
        var parentId = GetParentId(context);
        if (parentId == null)
            throw new InvalidOperationException("Parent entity ID not found in evaluation context.");

        // Build and execute the aggregate SQL
        var (sql, parameters) = BuildAggregateSql(aggregate, childEntity, fkColumnName, fieldName, parentId.Value, context.TenantId);

        _logger.LogDebug("Executing aggregate SQL: {Sql}", sql);

        var result = await _queryExecutor.ExecuteScalarAsync<object>(sql, parameters);

        return NormalizeResult(result, aggregate.Function);
    }

    /// <summary>
    /// Parse the aggregate argument to extract navigation name and optional field name.
    /// count(orders) → ("orders", null)
    /// sum(orders.amount) → ("orders", "amount")
    /// </summary>
    private static (string NavigationName, string? FieldName) ParseAggregateArgument(BmAggregateExpression aggregate)
    {
        if (aggregate.Function == BmAggregateFunction.Count && aggregate.Argument == null)
        {
            // COUNT(*) without argument — not valid for navigation-based aggregates
            throw new InvalidOperationException("COUNT(*) requires a collection argument (e.g., count(orders)).");
        }

        if (aggregate.Argument is BmIdentifierExpression identifier)
        {
            if (identifier.Path.Count == 1)
            {
                // count(orders) — just a navigation name
                return (identifier.Path[0], null);
            }
            if (identifier.Path.Count == 2)
            {
                // sum(orders.amount) — navigation + field
                return (identifier.Path[0], identifier.Path[1]);
            }
            throw new InvalidOperationException($"Aggregate argument path too deep: {identifier.FullPath}. Expected navigation or navigation.field.");
        }

        throw new InvalidOperationException($"Unsupported aggregate argument type: {aggregate.Argument?.GetType().Name ?? "null"}. Expected an identifier (e.g., orders or orders.amount).");
    }

    /// <summary>
    /// Resolve a navigation property (association or composition) to its child entity and FK column.
    /// </summary>
    private (BmEntity ChildEntity, string FkColumnName) ResolveNavigation(BmEntity parentEntity, string navigationName)
    {
        // Check compositions first (most common for aggregates like count(orders))
        var composition = parentEntity.Compositions.FirstOrDefault(
            c => c.Name.Equals(navigationName, StringComparison.OrdinalIgnoreCase));
        if (composition != null)
        {
            var childEntity = _cache.GetEntity(composition.TargetEntity);
            if (childEntity == null)
                throw new InvalidOperationException($"Composition target entity '{composition.TargetEntity}' not found.");

            // Composition FK is on the child: parentEntityName_id
            var fkColumn = NamingConvention.GetFkColumnName(parentEntity.Name);
            return (childEntity, fkColumn);
        }

        // Check associations (OneToMany)
        var association = parentEntity.Associations.FirstOrDefault(
            a => a.Name.Equals(navigationName, StringComparison.OrdinalIgnoreCase)
                 && a.Cardinality == BmCardinality.OneToMany);
        if (association != null)
        {
            var childEntity = _cache.GetEntity(association.TargetEntity);
            if (childEntity == null)
                throw new InvalidOperationException($"Association target entity '{association.TargetEntity}' not found.");

            // OneToMany FK is on the child: parentEntityName_id
            var fkColumn = NamingConvention.GetFkColumnName(parentEntity.Name);
            return (childEntity, fkColumn);
        }

        throw new InvalidOperationException(
            $"Navigation property '{navigationName}' not found on entity '{parentEntity.Name}' " +
            $"(or it is not a OneToMany association/composition suitable for aggregation).");
    }

    /// <summary>
    /// Get the parent entity's ID from the evaluation context.
    /// </summary>
    private static Guid? GetParentId(EvaluationContext context)
    {
        // Try common ID field names
        foreach (var key in new[] { "Id", "ID", "id" })
        {
            if (context.EntityData.TryGetValue(key, out var value) && value != null)
            {
                if (value is Guid guid) return guid;
                if (Guid.TryParse(value.ToString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    /// <summary>
    /// Build a parameterized aggregate SQL query.
    /// </summary>
    private (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildAggregateSql(
        BmAggregateExpression aggregate,
        BmEntity childEntity,
        string fkColumnName,
        string? fieldName,
        Guid parentId,
        Guid? tenantId)
    {
        var tableName = GetTableName(childEntity);
        var parameters = new List<NpgsqlParameter>();

        // Build the aggregate function SQL
        var aggregateColumn = aggregate.Function switch
        {
            BmAggregateFunction.Count => aggregate.IsDistinct && fieldName != null
                ? $"COUNT(DISTINCT \"{NamingConvention.ToSnakeCase(fieldName)}\")"
                : "COUNT(*)",
            BmAggregateFunction.Sum => BuildFieldAggregate("SUM", fieldName, aggregate.IsDistinct),
            BmAggregateFunction.Avg => BuildFieldAggregate("AVG", fieldName, aggregate.IsDistinct),
            BmAggregateFunction.Min => BuildFieldAggregate("MIN", fieldName, aggregate.IsDistinct),
            BmAggregateFunction.Max => BuildFieldAggregate("MAX", fieldName, aggregate.IsDistinct),
            BmAggregateFunction.StdDev => BuildFieldAggregate("STDDEV", fieldName, aggregate.IsDistinct),
            BmAggregateFunction.Variance => BuildFieldAggregate("VARIANCE", fieldName, aggregate.IsDistinct),
            _ => throw new NotSupportedException($"Aggregate function not supported: {aggregate.Function}")
        };

        // Build WHERE clause
        var whereClause = $"\"{fkColumnName}\" = @parentId";
        parameters.Add(new NpgsqlParameter("parentId", parentId));

        // Add tenant isolation if applicable
        if (tenantId.HasValue && childEntity.HasAnnotation("TenantScoped"))
        {
            whereClause += " AND \"tenant_id\" = @tenantId";
            parameters.Add(new NpgsqlParameter("tenantId", tenantId.Value));
        }

        // Incorporate the aggregate's WHERE condition (e.g., count(orders WHERE status = #Active))
        if (aggregate.WhereCondition != null)
        {
            var conditionSql = TranslateConditionToSql(aggregate.WhereCondition, childEntity, parameters);
            whereClause += $" AND {conditionSql}";
        }

        var sql = $"SELECT {aggregateColumn} FROM {tableName} WHERE {whereClause}";

        return (sql, parameters);
    }

    /// <summary>
    /// Translate a BmExpression condition to SQL for use in aggregate WHERE clauses.
    /// Handles common patterns: binary comparisons, literals, identifiers, logical operators.
    /// </summary>
    private static string TranslateConditionToSql(BmExpression expr, BmEntity childEntity, List<NpgsqlParameter> parameters)
    {
        return expr switch
        {
            BmBinaryExpression bin => TranslateBinaryToSql(bin, childEntity, parameters),
            BmIdentifierExpression id => TranslateIdentifierToSql(id),
            BmLiteralExpression lit => TranslateLiteralToSql(lit, parameters),
            BmUnaryExpression un when un.Operator == BmUnaryOperator.Not =>
                $"NOT ({TranslateConditionToSql(un.Operand, childEntity, parameters)})",
            BmIsNullExpression isNull =>
                $"{TranslateConditionToSql(isNull.Expression, childEntity, parameters)} IS {(isNull.IsNot ? "NOT " : "")}NULL",
            BmInExpression inExpr => TranslateInToSql(inExpr, childEntity, parameters),
            BmParenExpression paren =>
                $"({TranslateConditionToSql(paren.Inner, childEntity, parameters)})",
            _ => throw new NotSupportedException(
                $"Expression type {expr.GetType().Name} is not supported in aggregate WHERE conditions")
        };
    }

    private static string TranslateBinaryToSql(BmBinaryExpression bin, BmEntity childEntity, List<NpgsqlParameter> parameters)
    {
        var left = TranslateConditionToSql(bin.Left, childEntity, parameters);
        var right = TranslateConditionToSql(bin.Right, childEntity, parameters);
        var op = bin.Operator switch
        {
            BmBinaryOperator.Equal => "=",
            BmBinaryOperator.NotEqual => "<>",
            BmBinaryOperator.LessThan => "<",
            BmBinaryOperator.GreaterThan => ">",
            BmBinaryOperator.LessOrEqual => "<=",
            BmBinaryOperator.GreaterOrEqual => ">=",
            BmBinaryOperator.And => "AND",
            BmBinaryOperator.Or => "OR",
            BmBinaryOperator.Add => "+",
            BmBinaryOperator.Subtract => "-",
            BmBinaryOperator.Multiply => "*",
            BmBinaryOperator.Divide => "/",
            _ => throw new NotSupportedException($"Operator {bin.Operator} not supported in aggregate WHERE")
        };
        return $"{left} {op} {right}";
    }

    private static string TranslateIdentifierToSql(BmIdentifierExpression id)
    {
        // Single field name → snake_case column
        if (id.Path.Count == 1)
            return $"\"{NamingConvention.ToSnakeCase(id.Path[0])}\"";
        // Multi-part: join with dots in snake_case
        return string.Join(".", id.Path.Select(p => $"\"{NamingConvention.ToSnakeCase(p)}\""));
    }

    private static string TranslateLiteralToSql(BmLiteralExpression lit, List<NpgsqlParameter> parameters)
    {
        return lit.Kind switch
        {
            BmLiteralKind.String => $"'{SanitizeStringLiteral(lit.Value?.ToString() ?? "")}'",
            BmLiteralKind.Integer => lit.Value?.ToString() ?? "0",
            BmLiteralKind.Decimal => Convert.ToDecimal(lit.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            BmLiteralKind.Boolean => lit.Value is true ? "TRUE" : "FALSE",
            BmLiteralKind.Null => "NULL",
            BmLiteralKind.EnumValue => $"'{SanitizeStringLiteral(lit.Value?.ToString() ?? "")}'",
            _ => throw new NotSupportedException($"Literal kind {lit.Kind} not supported")
        };
    }

    /// <summary>
    /// Defense-in-depth validation for string literals from compiled BMMDL expressions.
    /// Rejects values containing SQL injection patterns (semicolons, comments, DDL/DML keywords)
    /// and applies standard single-quote escaping for safe values.
    /// </summary>
    private static string SanitizeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Check for dangerous SQL patterns — these should never appear in compiled BMMDL string literals
        if (ContainsDangerousPattern(value))
        {
            // Log via static warning — callers should review their BMMDL source if this triggers
            System.Diagnostics.Trace.TraceWarning(
                "AggregateExpressionResolver: Rejected string literal containing dangerous SQL pattern: '{0}'",
                value.Length > 50 ? value[..50] + "..." : value);
            return ""; // Return empty string as safe fallback
        }

        return value.Replace("'", "''");
    }

    /// <summary>
    /// Check if a string literal contains patterns that indicate SQL injection attempts.
    /// </summary>
    private static bool ContainsDangerousPattern(string value)
    {
        // Semicolons can terminate statements and start new ones
        if (value.Contains(';'))
            return true;

        // SQL comment sequences
        if (value.Contains("--") || value.Contains("/*") || value.Contains("*/"))
            return true;

        // Check for DDL/DML keywords as whole words (case-insensitive)
        // These should never appear in legitimate BMMDL string literal values
        ReadOnlySpan<string> dangerousKeywords =
        [
            "DROP", "ALTER", "TRUNCATE", "CREATE", "INSERT", "UPDATE", "DELETE",
            "EXEC", "EXECUTE", "UNION", "GRANT", "REVOKE"
        ];

        foreach (var keyword in dangerousKeywords)
        {
            var idx = value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // Check that it appears as a whole word (not part of a larger identifier)
            var before = idx > 0 ? value[idx - 1] : ' ';
            var after = idx + keyword.Length < value.Length ? value[idx + keyword.Length] : ' ';

            if (!char.IsLetterOrDigit(before) && before != '_' &&
                !char.IsLetterOrDigit(after) && after != '_')
            {
                return true;
            }
        }

        return false;
    }

    private static string TranslateInToSql(BmInExpression inExpr, BmEntity childEntity, List<NpgsqlParameter> parameters)
    {
        var expr = TranslateConditionToSql(inExpr.Expression, childEntity, parameters);
        var values = string.Join(", ", inExpr.Values.Select(v => TranslateConditionToSql(v, childEntity, parameters)));
        var notStr = inExpr.IsNot ? "NOT " : "";
        return $"{expr} {notStr}IN ({values})";
    }

    private static string BuildFieldAggregate(string function, string? fieldName, bool isDistinct)
    {
        if (string.IsNullOrEmpty(fieldName))
            throw new InvalidOperationException($"{function} requires a field argument (e.g., {function.ToLower()}(orders.amount)).");

        var column = $"\"{NamingConvention.ToSnakeCase(fieldName)}\"";
        var distinct = isDistinct ? "DISTINCT " : "";
        return $"{function}({distinct}{column})";
    }

    private string GetTableName(BmEntity entity)
    {
        var schema = !string.IsNullOrEmpty(entity.Namespace)
            ? NamingConvention.GetSchemaName(entity.Namespace)
            : _defaultSchema;

        return NamingConvention.GetTableName(entity.Name, schema);
    }

    /// <summary>
    /// Normalize the DB result to a consistent type.
    /// NULL → 0 for COUNT, 0 for SUM, null for others.
    /// </summary>
    private static object? NormalizeResult(object? result, BmAggregateFunction function)
    {
        if (result == null || result == DBNull.Value)
        {
            return function switch
            {
                BmAggregateFunction.Count => 0L,
                BmAggregateFunction.Sum => 0m,
                _ => null // MIN, MAX, AVG return null for empty sets
            };
        }

        // Convert to consistent numeric types
        return result switch
        {
            long l => l,
            int i => (long)i,
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            _ => result
        };
    }
}
