using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities.Normalized;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Serialization;

/// <summary>
/// Serializes/deserializes BmRuleStatement trees to/from StatementNode DB records.
/// </summary>
internal sealed class StatementAstSerializer
{
    private readonly RegistryDbContext _db;
    private readonly ExpressionAstSerializer _exprSerializer;
    private Dictionary<Guid, BmExpression>? _tempExpressionAstMap;

    public StatementAstSerializer(RegistryDbContext db, ExpressionAstSerializer exprSerializer)
    {
        _db = db;
        _exprSerializer = exprSerializer;
    }

    // ============================================================
    // Save: BmRuleStatement → StatementNode records
    // ============================================================

    public Guid? SaveStatementNodes(
        IReadOnlyList<BmRuleStatement> statements,
        Guid ownerId,
        string ownerType)
    {
        if (statements.Count == 0) return null;

        var nodes = new List<StatementNode>();
        Guid? rootId = null;

        for (int i = 0; i < statements.Count; i++)
        {
            var nodeId = MapStatementNode(statements[i], ownerId, ownerType, null, null, i, nodes);
            if (i == 0) rootId = nodeId;
        }

        _db.StatementNodes.AddRange(nodes);
        return rootId;
    }

    private Guid MapStatementNode(
        BmRuleStatement stmt,
        Guid ownerId,
        string ownerType,
        Guid? parentId,
        string? parentRole,
        int position,
        List<StatementNode> nodes)
    {
        var node = new StatementNode
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            OwnerType = ownerType,
            ParentId = parentId,
            ParentRole = parentRole,
            Position = position,
            NodeType = GetStatementNodeType(stmt)
        };

        switch (stmt)
        {
            case BmValidateStatement v:
                node.Message = v.Message;
                node.ConditionExprRootId = _exprSerializer.SaveExpressionNodes(v.ExpressionAst, node.Id, "statement", "condition");
                break;

            case BmComputeStatement c:
                node.TargetField = c.Target;
                node.ValueExprRootId = _exprSerializer.SaveExpressionNodes(c.ExpressionAst, node.Id, "statement", "value");
                break;

            case BmLetStatement l:
                node.VariableName = l.VariableName;
                node.ValueExprRootId = _exprSerializer.SaveExpressionNodes(l.ExpressionAst, node.Id, "statement", "value");
                break;

            case BmEmitStatement e:
                node.EventName = e.EventName;
                {
                    int emitFieldIdx = 0;
                    foreach (var (fieldName, fieldExpr) in e.FieldAssignments)
                        _exprSerializer.SaveExpressionNodes(fieldExpr, node.Id, "statement", $"emit_field_{emitFieldIdx++}_{fieldName}");
                }
                break;

            case BmReturnStatement r:
                node.ValueExprRootId = _exprSerializer.SaveExpressionNodes(r.ExpressionAst, node.Id, "statement", "value");
                break;

            case BmRaiseStatement raise:
                node.Severity = raise.Severity.ToString();
                node.Message = raise.Message;
                break;

            case BmWhenStatement w:
                node.ConditionExprRootId = _exprSerializer.SaveExpressionNodes(w.ConditionAst, node.Id, "statement", "condition");
                nodes.Add(node);
                for (int i = 0; i < w.ThenStatements.Count; i++)
                    MapStatementNode(w.ThenStatements[i], ownerId, ownerType, node.Id, "then", i, nodes);
                for (int i = 0; i < w.ElseStatements.Count; i++)
                    MapStatementNode(w.ElseStatements[i], ownerId, ownerType, node.Id, "else", i, nodes);
                return node.Id;

            case BmForeachStatement f:
                node.IteratorVariable = f.VariableName;
                node.CollectionExprRootId = _exprSerializer.SaveExpressionNodes(f.CollectionAst, node.Id, "statement", "collection");
                nodes.Add(node);
                for (int i = 0; i < f.Body.Count; i++)
                    MapStatementNode(f.Body[i], ownerId, ownerType, node.Id, "body", i, nodes);
                return node.Id;

            case BmCallStatement c:
                node.CallTarget = c.Target;
                nodes.Add(node);
                for (int i = 0; i < c.Arguments.Count; i++)
                    _exprSerializer.SaveExpressionNodes(c.Arguments[i], node.Id, "statement", $"call_arg_{i}");
                return node.Id;

            case BmRejectStatement rej:
                if (rej.Message != null)
                    node.ConditionExprRootId = _exprSerializer.SaveExpressionNodes(rej.Message, node.Id, "statement", "condition");
                break;
        }

        nodes.Add(node);
        return node.Id;
    }

    internal static string GetStatementNodeType(BmRuleStatement stmt) => stmt switch
    {
        BmValidateStatement => "validate",
        BmComputeStatement => "compute",
        BmLetStatement => "let",
        BmEmitStatement => "emit",
        BmReturnStatement => "return",
        BmRaiseStatement => "raise",
        BmWhenStatement => "when",
        BmForeachStatement => "foreach",
        BmCallStatement => "call",
        BmRejectStatement => "reject",
        _ => "unknown"
    };

    // ============================================================
    // Load: StatementNode records → BmRuleStatement
    // ============================================================

    public List<BmRuleStatement> LoadBodyStatements(Guid ownerId, string ownerType)
    {
        try
        {
            var statementNodes = _db.StatementNodes
                .AsNoTracking()
                .Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId)
                .Include(s => s.ConditionExprRoot)
                .Include(s => s.ValueExprRoot)
                .ToList();

            if (statementNodes.Count == 0)
                return new List<BmRuleStatement>();

            return LoadAndReconstructStatements(statementNodes);
        }
        catch
        {
            return new List<BmRuleStatement>();
        }
        finally
        {
            _tempExpressionAstMap = null;
        }
    }

    public List<BmRuleStatement> LoadOperationBodyStatements(Guid operationId)
    {
        try
        {
            var statementNodes = _db.StatementNodes
                .AsNoTracking()
                .Where(s => s.OwnerType == "operation" && s.OwnerId == operationId)
                .Include(s => s.ConditionExprRoot)
                .Include(s => s.ValueExprRoot)
                .ToList();

            if (statementNodes.Count == 0)
                return new List<BmRuleStatement>();

            return LoadAndReconstructStatements(statementNodes);
        }
        catch
        {
            return new List<BmRuleStatement>();
        }
        finally
        {
            _tempExpressionAstMap = null;
        }
    }

    private List<BmRuleStatement> LoadAndReconstructStatements(List<StatementNode> statementNodes)
    {
        var stmtIds = statementNodes.Select(s => s.Id).ToList();
        var exprRootIds = statementNodes
            .SelectMany(s => new[] { s.ConditionExprRootId, s.ValueExprRootId, s.CollectionExprRootId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var exprNodes = _db.ExpressionNodes
            .AsNoTracking()
            .Where(e => e.OwnerType == "statement" && stmtIds.Contains(e.OwnerId))
            .ToList();

        var extraRootIds = exprNodes
            .Where(e => e.OwnerField != null && (e.OwnerField.StartsWith("call_arg_") || e.OwnerField.StartsWith("emit_field_")) && e.ParentId == null)
            .Select(e => e.Id)
            .ToList();
        exprRootIds.AddRange(extraRootIds);

        if (exprRootIds.Count > 0)
        {
            var exprAstByRoot = new Dictionary<Guid, BmExpression>();
            foreach (var rootId in exprRootIds)
            {
                var relevantNodes = new List<ExpressionNode>();
                var toProcess = new Queue<Guid>();
                var processed = new HashSet<Guid>();

                toProcess.Enqueue(rootId);
                while (toProcess.Count > 0)
                {
                    var currentId = toProcess.Dequeue();
                    if (processed.Contains(currentId)) continue;
                    processed.Add(currentId);

                    var node = exprNodes.FirstOrDefault(e => e.Id == currentId);
                    if (node != null)
                    {
                        relevantNodes.Add(node);
                        foreach (var child in exprNodes.Where(e => e.ParentId == currentId))
                        {
                            if (!processed.Contains(child.Id))
                                toProcess.Enqueue(child.Id);
                        }
                    }
                }

                if (relevantNodes.Count > 0)
                {
                    exprAstByRoot[rootId] = _exprSerializer.ReconstructExpressionTree(relevantNodes, rootId);
                }
            }

            foreach (var stmt in statementNodes)
            {
                if (stmt.ConditionExprRootId.HasValue && exprAstByRoot.ContainsKey(stmt.ConditionExprRootId.Value))
                    stmt.ConditionExprRoot = new ExpressionNode { Id = stmt.ConditionExprRootId.Value };
                if (stmt.ValueExprRootId.HasValue && exprAstByRoot.ContainsKey(stmt.ValueExprRootId.Value))
                    stmt.ValueExprRoot = new ExpressionNode { Id = stmt.ValueExprRootId.Value };
            }

            _tempExpressionAstMap = exprAstByRoot;
        }

        return ReconstructBmStatements(statementNodes);
    }

    public List<BmRuleStatement> ReconstructBmStatements(IEnumerable<StatementNode> nodes)
    {
        var result = new List<BmRuleStatement>();
        var nodeDict = nodes.ToDictionary(n => n.Id);

        var rootNodes = nodes
            .Where(n => n.ParentId == null || !nodeDict.ContainsKey(n.ParentId.Value))
            .OrderBy(n => n.Position);

        foreach (var node in rootNodes)
        {
            var stmt = ReconstructStatementNode(node, nodeDict);
            if (stmt != null) result.Add(stmt);
        }

        return result;
    }

    private BmRuleStatement? ReconstructStatementNode(StatementNode node, Dictionary<Guid, StatementNode> nodeDict)
    {
        return node.NodeType switch
        {
            "validate" => new BmValidateStatement
            {
                Expression = node.TargetField ?? "",
                Message = node.Message,
                Severity = Enum.TryParse<BmSeverity>(node.Severity ?? "error", true, out var sev) ? sev : BmSeverity.Error,
                ExpressionAst = GetExpressionAst(node.ConditionExprRootId)
            },
            "compute" => new BmComputeStatement
            {
                Target = node.TargetField ?? "",
                Expression = "",
                ExpressionAst = GetExpressionAst(node.ValueExprRootId)
            },
            "let" => new BmLetStatement
            {
                VariableName = node.VariableName ?? "",
                ExpressionAst = GetExpressionAst(node.ValueExprRootId)
            },
            "emit" => ReconstructEmitStatement(node),
            "return" => new BmReturnStatement
            {
                ExpressionAst = GetExpressionAst(node.ValueExprRootId)
            },
            "raise" => new BmRaiseStatement
            {
                Severity = Enum.TryParse<BmSeverity>(node.Severity ?? "error", true, out var rs) ? rs : BmSeverity.Error,
                Message = node.Message ?? ""
            },
            "when" => ReconstructWhenStatement(node, nodeDict),
            "foreach" => ReconstructForeachStatement(node, nodeDict),
            "call" => ReconstructCallStatement(node),
            "reject" => new BmRejectStatement
            {
                Message = GetExpressionAst(node.ConditionExprRootId)
            },
            _ => null
        };
    }

    private BmWhenStatement ReconstructWhenStatement(StatementNode node, Dictionary<Guid, StatementNode> nodeDict)
    {
        var stmt = new BmWhenStatement { Condition = "" };
        var children = nodeDict.Values
            .Where(n => n.ParentId == node.Id)
            .OrderBy(n => n.Position);
        foreach (var child in children)
        {
            var childStmt = ReconstructStatementNode(child, nodeDict);
            if (childStmt != null)
            {
                if (child.ParentRole == "else")
                    stmt.ElseStatements.Add(childStmt);
                else
                    stmt.ThenStatements.Add(childStmt);
            }
        }
        return stmt;
    }

    private BmForeachStatement ReconstructForeachStatement(StatementNode node, Dictionary<Guid, StatementNode> nodeDict)
    {
        var stmt = new BmForeachStatement { VariableName = node.IteratorVariable ?? "" };
        var children = nodeDict.Values
            .Where(n => n.ParentId == node.Id && n.ParentRole == "body")
            .OrderBy(n => n.Position);
        foreach (var child in children)
        {
            var childStmt = ReconstructStatementNode(child, nodeDict);
            if (childStmt != null) stmt.Body.Add(childStmt);
        }
        return stmt;
    }

    private BmCallStatement ReconstructCallStatement(StatementNode node)
    {
        var stmt = new BmCallStatement { Target = node.CallTarget ?? "" };
        var argExprNodes = _db.ExpressionNodes
            .AsNoTracking()
            .Where(e => e.OwnerType == "statement" && e.OwnerId == node.Id
                && e.OwnerField != null && e.OwnerField.StartsWith("call_arg_"))
            .ToList();
        if (argExprNodes.Count > 0)
        {
            var argGroups = argExprNodes.GroupBy(e => e.OwnerField).OrderBy(g => g.Key);
            foreach (var group in argGroups)
            {
                var rootNode = group.FirstOrDefault(n => n.ParentId == null) ?? group.First();
                var expr = _exprSerializer.ReconstructExpressionTree(group.ToList(), rootNode.Id);
                stmt.Arguments.Add(expr);
            }
        }
        return stmt;
    }

    private BmEmitStatement ReconstructEmitStatement(StatementNode node)
    {
        var stmt = new BmEmitStatement { EventName = node.EventName ?? "" };
        var fieldExprNodes = _db.ExpressionNodes
            .AsNoTracking()
            .Where(e => e.OwnerType == "statement" && e.OwnerId == node.Id
                && e.OwnerField != null && e.OwnerField.StartsWith("emit_field_"))
            .ToList();
        if (fieldExprNodes.Count > 0)
        {
            var fieldGroups = fieldExprNodes.GroupBy(e => e.OwnerField).OrderBy(g => g.Key);
            foreach (var group in fieldGroups)
            {
                var parts = group.Key!.Split('_', 4);
                if (parts.Length >= 4)
                {
                    var fieldName = parts[3];
                    var rootNode = group.FirstOrDefault(n => n.ParentId == null) ?? group.First();
                    var expr = _exprSerializer.ReconstructExpressionTree(group.ToList(), rootNode.Id);
                    stmt.FieldAssignments[fieldName] = expr;
                }
            }
        }
        return stmt;
    }

    private BmExpression? GetExpressionAst(Guid? exprRootId)
    {
        if (!exprRootId.HasValue || _tempExpressionAstMap == null)
            return null;
        _tempExpressionAstMap.TryGetValue(exprRootId.Value, out var expr);
        return expr;
    }

    // ============================================================
    // Hashing
    // ============================================================

    public static string ComputeBodyHash(IReadOnlyList<BmRuleStatement> statements)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var stmt in statements)
            AppendStatementSignature(stmt, sb);
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static void AppendStatementSignature(BmRuleStatement stmt, System.Text.StringBuilder sb)
    {
        sb.Append(stmt.GetType().Name).Append('|');
        switch (stmt)
        {
            case BmValidateStatement v:
                sb.Append(v.Message ?? "");
                break;
            case BmComputeStatement c:
                sb.Append(c.Target ?? "");
                break;
            case BmLetStatement l:
                sb.Append(l.VariableName ?? "");
                break;
            case BmEmitStatement e:
                sb.Append(e.EventName ?? "");
                break;
            case BmRaiseStatement r:
                sb.Append(r.Severity.ToString()).Append('|').Append(r.Message ?? "");
                break;
            case BmWhenStatement w:
                foreach (var thenStmt in w.ThenStatements)
                    AppendStatementSignature(thenStmt, sb);
                foreach (var elseStmt in w.ElseStatements)
                    AppendStatementSignature(elseStmt, sb);
                break;
            case BmForeachStatement f:
                sb.Append(f.VariableName ?? "");
                foreach (var bodyStmt in f.Body)
                    AppendStatementSignature(bodyStmt, sb);
                break;
            case BmCallStatement call:
                sb.Append(call.Target ?? "");
                sb.Append('|').Append(call.Arguments.Count);
                break;
        }
        sb.Append(';');
    }
}
