using BMMDL.MetaModel.Expressions;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities.Normalized;

namespace BMMDL.Registry.Repositories.Serialization;

/// <summary>
/// Serializes/deserializes BmExpression trees to/from ExpressionNode DB records.
/// </summary>
internal sealed class ExpressionAstSerializer
{
    private readonly RegistryDbContext _db;

    public ExpressionAstSerializer(RegistryDbContext db)
    {
        _db = db;
    }

    // ============================================================
    // Save: BmExpression → ExpressionNode records
    // ============================================================

    public Guid? SaveExpressionNodes(
        BmExpression? expression,
        Guid ownerId,
        string ownerType,
        string ownerField)
    {
        if (expression == null) return null;

        var nodes = new List<ExpressionNode>();
        var rootId = MapExpressionNode(expression, ownerId, ownerType, ownerField, null, null, 0, nodes);
        _db.ExpressionNodes.AddRange(nodes);
        return rootId;
    }

    private Guid MapExpressionNode(
        BmExpression expr,
        Guid ownerId,
        string ownerType,
        string ownerField,
        Guid? parentId,
        string? parentRole,
        int position,
        List<ExpressionNode> nodes)
    {
        var node = new ExpressionNode
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            OwnerType = ownerType,
            OwnerField = ownerField,
            ParentId = parentId,
            ParentRole = parentRole,
            Position = position,
            NodeType = GetNodeType(expr)
        };

        switch (expr)
        {
            case BmLiteralExpression lit:
                node.LiteralKind = lit.Kind.ToString().ToLower();
                node.LiteralValue = lit.Value?.ToString();
                break;

            case BmIdentifierExpression id:
                node.IdentifierPath = id.Path.ToList();
                break;

            case BmContextVariableExpression ctx:
                node.IdentifierPath = ctx.Path.ToList();
                break;

            case BmParameterExpression param:
                node.IdentifierPath = new List<string> { param.Name };
                break;

            case BmBinaryExpression bin:
                node.Operator = bin.Operator.ToString();
                nodes.Add(node);
                MapExpressionNode(bin.Left, ownerId, ownerType, ownerField, node.Id, "left", 0, nodes);
                MapExpressionNode(bin.Right, ownerId, ownerType, ownerField, node.Id, "right", 1, nodes);
                return node.Id;

            case BmUnaryExpression unary:
                node.Operator = unary.Operator.ToString();
                nodes.Add(node);
                MapExpressionNode(unary.Operand, ownerId, ownerType, ownerField, node.Id, "operand", 0, nodes);
                return node.Id;

            case BmFunctionCallExpression func:
                node.FunctionName = func.FunctionName;
                nodes.Add(node);
                for (int i = 0; i < func.Arguments.Count; i++)
                    MapExpressionNode(func.Arguments[i], ownerId, ownerType, ownerField, node.Id, $"arg_{i}", i, nodes);
                return node.Id;

            case BmAggregateExpression agg:
                node.AggregateFunction = agg.Function.ToString();
                node.IsDistinct = agg.IsDistinct;
                nodes.Add(node);
                if (agg.Argument != null)
                    MapExpressionNode(agg.Argument, ownerId, ownerType, ownerField, node.Id, "argument", 0, nodes);
                if (agg.WhereCondition != null)
                    MapExpressionNode(agg.WhereCondition, ownerId, ownerType, ownerField, node.Id, "where", 1, nodes);
                return node.Id;

            case BmCaseExpression caseExpr:
                nodes.Add(node);
                if (caseExpr.InputExpression != null)
                    MapExpressionNode(caseExpr.InputExpression, ownerId, ownerType, ownerField, node.Id, "case_operand", 0, nodes);
                for (int i = 0; i < caseExpr.WhenClauses.Count; i++)
                {
                    var when = caseExpr.WhenClauses[i];
                    MapExpressionNode(when.When, ownerId, ownerType, ownerField, node.Id, $"when_{i}", i * 2, nodes);
                    MapExpressionNode(when.Then, ownerId, ownerType, ownerField, node.Id, $"then_{i}", i * 2 + 1, nodes);
                }
                if (caseExpr.ElseResult != null)
                    MapExpressionNode(caseExpr.ElseResult, ownerId, ownerType, ownerField, node.Id, "else", 999, nodes);
                return node.Id;

            case BmCastExpression cast:
                node.TargetType = cast.TargetType?.ToTypeString();
                nodes.Add(node);
                MapExpressionNode(cast.Expression, ownerId, ownerType, ownerField, node.Id, "expression", 0, nodes);
                return node.Id;

            case BmTernaryExpression tern:
                nodes.Add(node);
                MapExpressionNode(tern.Condition, ownerId, ownerType, ownerField, node.Id, "condition", 0, nodes);
                MapExpressionNode(tern.ThenExpression, ownerId, ownerType, ownerField, node.Id, "then", 1, nodes);
                MapExpressionNode(tern.ElseExpression, ownerId, ownerType, ownerField, node.Id, "else", 2, nodes);
                return node.Id;

            case BmInExpression inExpr:
                node.IsNot = inExpr.IsNot;
                nodes.Add(node);
                MapExpressionNode(inExpr.Expression, ownerId, ownerType, ownerField, node.Id, "expression", 0, nodes);
                for (int i = 0; i < inExpr.Values.Count; i++)
                    MapExpressionNode(inExpr.Values[i], ownerId, ownerType, ownerField, node.Id, $"value_{i}", i + 1, nodes);
                return node.Id;

            case BmBetweenExpression between:
                node.IsNot = between.IsNot;
                nodes.Add(node);
                MapExpressionNode(between.Expression, ownerId, ownerType, ownerField, node.Id, "expression", 0, nodes);
                MapExpressionNode(between.Low, ownerId, ownerType, ownerField, node.Id, "low", 1, nodes);
                MapExpressionNode(between.High, ownerId, ownerType, ownerField, node.Id, "high", 2, nodes);
                return node.Id;

            case BmLikeExpression like:
                node.IsNot = like.IsNot;
                nodes.Add(node);
                MapExpressionNode(like.Expression, ownerId, ownerType, ownerField, node.Id, "expression", 0, nodes);
                MapExpressionNode(like.Pattern, ownerId, ownerType, ownerField, node.Id, "pattern", 1, nodes);
                return node.Id;

            case BmIsNullExpression isNull:
                node.IsNot = isNull.IsNot;
                nodes.Add(node);
                MapExpressionNode(isNull.Expression, ownerId, ownerType, ownerField, node.Id, "expression", 0, nodes);
                return node.Id;

            case BmSubqueryExpression subquery:
                node.LiteralValue = subquery.SelectStatement;
                nodes.Add(node);
                return node.Id;

            case BmExistsExpression exists:
                node.LiteralValue = exists.SelectStatement;
                nodes.Add(node);
                return node.Id;

            case BmTemporalBinaryExpression temporal:
                node.Operator = temporal.Operator.ToString();
                nodes.Add(node);
                MapExpressionNode(temporal.Left, ownerId, ownerType, ownerField, node.Id, "left", 0, nodes);
                MapExpressionNode(temporal.Right, ownerId, ownerType, ownerField, node.Id, "right", 1, nodes);
                return node.Id;

            case BmParenExpression paren:
                nodes.Add(node);
                MapExpressionNode(paren.Inner, ownerId, ownerType, ownerField, node.Id, "inner", 0, nodes);
                return node.Id;
        }

        nodes.Add(node);
        return node.Id;
    }

    internal static string GetNodeType(BmExpression expr) => expr switch
    {
        BmLiteralExpression => "literal",
        BmIdentifierExpression => "identifier",
        BmContextVariableExpression => "context_variable",
        BmParameterExpression => "parameter",
        BmBinaryExpression => "binary",
        BmUnaryExpression => "unary",
        BmFunctionCallExpression => "function_call",
        BmAggregateExpression => "aggregate",
        BmCaseExpression => "case",
        BmCastExpression => "cast",
        BmTernaryExpression => "ternary",
        BmInExpression => "in",
        BmBetweenExpression => "between",
        BmLikeExpression => "like",
        BmIsNullExpression => "is_null",
        BmParenExpression => "paren",
        BmSubqueryExpression => "subquery",
        BmExistsExpression => "exists",
        BmTemporalBinaryExpression => "temporal_binary",
        _ => "unknown"
    };

    // ============================================================
    // Load: ExpressionNode records → BmExpression
    // ============================================================

    public BmExpression? ReconstructBmExpression(Guid rootId, List<ExpressionNode> allNodes)
    {
        var nodeById = allNodes
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToDictionary(n => n.Id);
        if (!nodeById.TryGetValue(rootId, out var rootNode))
            return null;

        return ReconstructNode(rootNode, nodeById);
    }

    public BmExpression ReconstructExpressionTree(List<ExpressionNode> nodes, Guid rootId)
    {
        return ReconstructBmExpression(rootId, nodes) ?? BmLiteralExpression.Null();
    }

    private BmExpression ReconstructNode(ExpressionNode node, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var children = nodeById.Values
            .Where(n => n.ParentId == node.Id)
            .OrderBy(n => n.Position)
            .ToList();

        return node.NodeType.ToLower() switch
        {
            "literal" => new BmLiteralExpression(ParseLiteralKind(node.LiteralKind), ParseLiteralValue(node.LiteralKind, node.LiteralValue)),
            "identifier" => new BmIdentifierExpression((node.IdentifierPath ?? new List<string>()).ToArray()),
            "context_variable" => new BmContextVariableExpression((node.IdentifierPath ?? new List<string>()).ToArray()),
            "parameter" => new BmParameterExpression(node.IdentifierPath?.FirstOrDefault() ?? ""),
            "binary" => ReconstructBinary(node, children, nodeById),
            "unary" => ReconstructUnary(node, children, nodeById),
            "paren" => ReconstructParen(children, nodeById),
            "function_call" => ReconstructFunctionCall(node, children, nodeById),
            "aggregate" => ReconstructAggregate(node, children, nodeById),
            "ternary" => ReconstructTernary(children, nodeById),
            "case" => ReconstructCase(children, nodeById),
            "cast" => ReconstructCast(node, children, nodeById),
            "in" => ReconstructIn(node, children, nodeById),
            "between" => ReconstructBetween(node, children, nodeById),
            "like" => ReconstructLike(node, children, nodeById),
            "is_null" => ReconstructIsNull(node, children, nodeById),
            "subquery" => new BmSubqueryExpression(node.LiteralValue ?? ""),
            "exists" => new BmExistsExpression(node.LiteralValue ?? ""),
            "temporal_binary" => ReconstructTemporalBinary(node, children, nodeById),
            _ => new BmLiteralExpression(BmLiteralKind.Null, null)
        };
    }

    private BmBinaryExpression ReconstructBinary(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var left = children.FirstOrDefault(c => c.ParentRole == "left");
        var right = children.FirstOrDefault(c => c.ParentRole == "right");
        return new BmBinaryExpression(
            left != null ? ReconstructNode(left, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            ParseBinaryOperator(node.Operator),
            right != null ? ReconstructNode(right, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null)
        );
    }

    private BmUnaryExpression ReconstructUnary(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var operand = children.FirstOrDefault(c => c.ParentRole == "operand");
        return new BmUnaryExpression(
            ParseUnaryOperator(node.Operator),
            operand != null ? ReconstructNode(operand, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null)
        );
    }

    private BmParenExpression ReconstructParen(List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var inner = children.FirstOrDefault(c => c.ParentRole == "inner");
        return new BmParenExpression(
            inner != null ? ReconstructNode(inner, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null)
        );
    }

    private BmFunctionCallExpression ReconstructFunctionCall(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var args = children.OrderBy(c => c.Position).Select(c => ReconstructNode(c, nodeById)).ToArray();
        return new BmFunctionCallExpression(node.FunctionName ?? "", args);
    }

    private BmAggregateExpression ReconstructAggregate(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var argument = children.FirstOrDefault(c => c.ParentRole == "argument");
        var whereClause = children.FirstOrDefault(c => c.ParentRole == "where");
        return new BmAggregateExpression(
            ParseAggregateFunction(node.AggregateFunction),
            argument != null ? ReconstructNode(argument, nodeById) : null,
            node.IsDistinct,
            whereClause != null ? ReconstructNode(whereClause, nodeById) : null
        );
    }

    private BmTernaryExpression ReconstructTernary(List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var condition = children.FirstOrDefault(c => c.ParentRole == "condition");
        var thenExpr = children.FirstOrDefault(c => c.ParentRole == "then");
        var elseExpr = children.FirstOrDefault(c => c.ParentRole == "else");
        return new BmTernaryExpression(
            condition != null ? ReconstructNode(condition, nodeById) : new BmLiteralExpression(BmLiteralKind.Boolean, true),
            thenExpr != null ? ReconstructNode(thenExpr, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            elseExpr != null ? ReconstructNode(elseExpr, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null)
        );
    }

    private BmCaseExpression ReconstructCase(List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var caseExpr = new BmCaseExpression();
        var caseOperand = children.FirstOrDefault(c => c.ParentRole == "case_operand");
        if (caseOperand != null)
            caseExpr.InputExpression = ReconstructNode(caseOperand, nodeById);
        var whenNodes = children.Where(c => c.ParentRole != null && c.ParentRole.StartsWith("when_")).OrderBy(c => c.Position).ToList();
        var thenNodes = children.Where(c => c.ParentRole != null && c.ParentRole.StartsWith("then_")).OrderBy(c => c.Position).ToList();
        for (int i = 0; i < whenNodes.Count; i++)
        {
            var whenExpr = ReconstructNode(whenNodes[i], nodeById);
            var thenExpr = i < thenNodes.Count
                ? ReconstructNode(thenNodes[i], nodeById)
                : new BmLiteralExpression(BmLiteralKind.Null, null);
            caseExpr.WhenClauses.Add((whenExpr, thenExpr));
        }
        var elseNode = children.FirstOrDefault(c => c.ParentRole == "else");
        if (elseNode != null)
            caseExpr.ElseResult = ReconstructNode(elseNode, nodeById);
        return caseExpr;
    }

    private BmCastExpression ReconstructCast(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var expression = children.FirstOrDefault(c => c.ParentRole == "expression");
        var typeRef = new BMMDL.MetaModel.Types.BmTypeReferenceBuilder().Parse(node.TargetType ?? "String");
        return new BmCastExpression(
            expression != null ? ReconstructNode(expression, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            typeRef
        );
    }

    private BmInExpression ReconstructIn(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var expression = children.FirstOrDefault(c => c.ParentRole == "expression");
        var values = children
            .Where(c => c.ParentRole != null && c.ParentRole.StartsWith("value_"))
            .OrderBy(c => c.Position)
            .Select(c => ReconstructNode(c, nodeById))
            .ToList();
        return new BmInExpression(
            expression != null ? ReconstructNode(expression, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            values,
            node.IsNot
        );
    }

    private BmBetweenExpression ReconstructBetween(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var expression = children.FirstOrDefault(c => c.ParentRole == "expression");
        var low = children.FirstOrDefault(c => c.ParentRole == "low");
        var high = children.FirstOrDefault(c => c.ParentRole == "high");
        return new BmBetweenExpression(
            expression != null ? ReconstructNode(expression, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            low != null ? ReconstructNode(low, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            high != null ? ReconstructNode(high, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            node.IsNot
        );
    }

    private BmLikeExpression ReconstructLike(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var expression = children.FirstOrDefault(c => c.ParentRole == "expression");
        var pattern = children.FirstOrDefault(c => c.ParentRole == "pattern");
        return new BmLikeExpression(
            expression != null ? ReconstructNode(expression, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            pattern != null ? ReconstructNode(pattern, nodeById) : new BmLiteralExpression(BmLiteralKind.String, "%"),
            node.IsNot
        );
    }

    private BmIsNullExpression ReconstructIsNull(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var expression = children.FirstOrDefault(c => c.ParentRole == "expression");
        return new BmIsNullExpression(
            expression != null ? ReconstructNode(expression, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            node.IsNot
        );
    }

    private BmTemporalBinaryExpression ReconstructTemporalBinary(ExpressionNode node, List<ExpressionNode> children, Dictionary<Guid, ExpressionNode> nodeById)
    {
        var left = children.FirstOrDefault(c => c.ParentRole == "left");
        var right = children.FirstOrDefault(c => c.ParentRole == "right");
        return new BmTemporalBinaryExpression(
            left != null ? ReconstructNode(left, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null),
            ParseTemporalBinaryOperator(node.Operator),
            right != null ? ReconstructNode(right, nodeById) : new BmLiteralExpression(BmLiteralKind.Null, null)
        );
    }

    // ============================================================
    // Parse helpers (static)
    // ============================================================

    internal static BmLiteralKind ParseLiteralKind(string? kind) => kind?.ToLower() switch
    {
        "string" => BmLiteralKind.String,
        "integer" => BmLiteralKind.Integer,
        "decimal" => BmLiteralKind.Decimal,
        "boolean" => BmLiteralKind.Boolean,
        "null" => BmLiteralKind.Null,
        "enum_value" => BmLiteralKind.EnumValue,
        _ => BmLiteralKind.String
    };

    internal static object? ParseLiteralValue(string? kind, string? value)
    {
        if (value == null) return null;
        return kind?.ToLower() switch
        {
            "integer" => long.TryParse(value, out var i) ? i : 0L,
            "decimal" => decimal.TryParse(value, out var d) ? d : 0m,
            "boolean" => bool.TryParse(value, out var b) && b,
            "null" => null,
            _ => value
        };
    }

    internal static BmBinaryOperator ParseBinaryOperator(string? op) => op?.ToLower() switch
    {
        "add" => BmBinaryOperator.Add,
        "subtract" => BmBinaryOperator.Subtract,
        "multiply" => BmBinaryOperator.Multiply,
        "divide" => BmBinaryOperator.Divide,
        "modulo" => BmBinaryOperator.Modulo,
        "equal" => BmBinaryOperator.Equal,
        "notequal" => BmBinaryOperator.NotEqual,
        "lessthan" => BmBinaryOperator.LessThan,
        "lessorequal" => BmBinaryOperator.LessOrEqual,
        "lessthanorequal" => BmBinaryOperator.LessOrEqual,
        "greaterthan" => BmBinaryOperator.GreaterThan,
        "greaterorequal" => BmBinaryOperator.GreaterOrEqual,
        "greaterthanorequal" => BmBinaryOperator.GreaterOrEqual,
        "and" => BmBinaryOperator.And,
        "or" => BmBinaryOperator.Or,
        _ => BmBinaryOperator.Equal
    };

    internal static BmUnaryOperator ParseUnaryOperator(string? op) => op?.ToLower() switch
    {
        "not" => BmUnaryOperator.Not,
        "negate" => BmUnaryOperator.Negate,
        _ => BmUnaryOperator.Not
    };

    internal static TemporalBinaryOperator ParseTemporalBinaryOperator(string? op) => op?.ToLower() switch
    {
        "overlaps" => TemporalBinaryOperator.Overlaps,
        "contains" => TemporalBinaryOperator.Contains,
        "precedes" => TemporalBinaryOperator.Precedes,
        "meets" => TemporalBinaryOperator.Meets,
        _ => TemporalBinaryOperator.Overlaps
    };

    internal static BmAggregateFunction ParseAggregateFunction(string? func) => func?.ToLower() switch
    {
        "count" => BmAggregateFunction.Count,
        "sum" => BmAggregateFunction.Sum,
        "avg" => BmAggregateFunction.Avg,
        "min" => BmAggregateFunction.Min,
        "max" => BmAggregateFunction.Max,
        _ => BmAggregateFunction.Count
    };
}
