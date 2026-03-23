using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities.Normalized;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmRule objects.
/// </summary>
internal sealed class RulePersister
{
    private readonly RepositoryContext _ctx;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _ruleAnnotationsByOwner;

    public RulePersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SaveRuleAsync(BmRule rule, CancellationToken ct = default)
    {
        var existing = await _ctx.Db.Rules.Include(r => r.Triggers).Include(r => r.Statements)
            .FirstOrDefaultAsync(r => r.Name == rule.Name && r.TargetEntityName == rule.TargetEntity, ct);

        RuleRecord record;
        if (existing != null)
        {
            var ruleId = existing.Id;
            await _ctx.Db.Database.ExecuteSqlRawAsync(
                "DELETE FROM registry.expression_nodes WHERE \"OwnerType\" = 'rule_statement' AND \"OwnerId\" IN (SELECT \"Id\" FROM registry.rule_statements WHERE \"RuleId\" = {0})", ruleId);
            await _ctx.Db.Database.ExecuteSqlRawAsync(
                "DELETE FROM registry.rule_statements WHERE \"RuleId\" = {0}", ruleId);
            await _ctx.Db.Database.ExecuteSqlRawAsync(
                "DELETE FROM registry.rule_trigger_fields WHERE \"TriggerId\" IN (SELECT \"Id\" FROM registry.rule_triggers WHERE \"RuleId\" = {0})", ruleId);
            await _ctx.Db.Database.ExecuteSqlRawAsync(
                "DELETE FROM registry.rule_triggers WHERE \"RuleId\" = {0}", ruleId);

            await _ctx.Db.NormalizedAnnotations
                .Where(a => a.OwnerType == "rule" && a.OwnerId == ruleId)
                .ExecuteDeleteAsync(ct);

            foreach (var stmt in existing.Statements.ToList())
                _ctx.Db.Entry(stmt).State = EntityState.Detached;
            foreach (var trigger in existing.Triggers.ToList())
                _ctx.Db.Entry(trigger).State = EntityState.Detached;
            existing.Statements.Clear();
            existing.Triggers.Clear();
            record = existing;
        }
        else
        {
            record = new RuleRecord
            {
                Id = Guid.NewGuid(), TenantId = _ctx.TenantId,
                Name = rule.Name, TargetEntityName = rule.TargetEntity,
                CreatedAt = DateTime.UtcNow
            };
            _ctx.Db.Rules.Add(record);
        }

        int pos = 0;
        foreach (var trig in rule.Triggers)
        {
            var trigRecord = new RuleTrigger
            {
                Id = Guid.NewGuid(), RuleId = record.Id,
                Timing = trig.Timing.ToString().ToLower(),
                Operation = trig.Operation.ToString().ToLower(),
                Position = pos++
            };
            foreach (var f in trig.ChangeFields)
                trigRecord.ChangeFields.Add(new RuleTriggerField { TriggerId = trigRecord.Id, FieldName = f });
            record.Triggers.Add(trigRecord);
        }

        pos = 0;
        foreach (var stmt in rule.Statements)
            MapRuleStatementSave(stmt, record.Id, null, pos++, record.Statements);

        AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, rule.Annotations, "rule", record.Id);
        await _ctx.Db.SaveChangesAsync(ct);
    }

    private void MapRuleStatementSave(BmRuleStatement stmt, Guid ruleId, Guid? parentId, int pos, ICollection<RuleStatement> targetCollection, bool isElse = false)
    {
        string stmtType;
        string? target = null, expression = null, message = null, severity = null;

        switch (stmt)
        {
            case BmValidateStatement v: stmtType = "validate"; expression = v.Expression; message = v.Message; severity = v.Severity.ToString().ToLower(); break;
            case BmComputeStatement c: stmtType = "compute"; target = c.Target; expression = c.Expression; break;
            case BmWhenStatement w: stmtType = "when"; expression = w.Condition; break;
            case BmCallStatement call: stmtType = "call"; target = call.Target; break;
            case BmForeachStatement f: stmtType = "foreach"; target = f.VariableName; expression = f.Collection; break;
            case BmLetStatement l: stmtType = "let"; target = l.VariableName; expression = l.Expression; break;
            case BmRaiseStatement r: stmtType = "raise"; message = r.Message; severity = r.Severity.ToString().ToLower(); break;
            case BmReturnStatement ret: stmtType = "return"; expression = ret.Expression; break;
            case BmEmitStatement emit: stmtType = "emit"; target = emit.EventName; break;
            case BmRejectStatement: stmtType = "reject"; break;
            default: stmtType = "unknown"; break;
        }

        var record = new RuleStatement
        {
            Id = Guid.NewGuid(), RuleId = ruleId, ParentStatementId = parentId,
            StatementType = stmtType, Target = target, Expression = expression,
            Message = message, Severity = severity, IsElseBranch = isElse, Position = pos
        };

        BmExpression? exprAst = stmt switch
        {
            BmValidateStatement v => v.ExpressionAst,
            BmComputeStatement c => c.ExpressionAst,
            BmWhenStatement w => w.ConditionAst,
            BmForeachStatement f => f.CollectionAst,
            BmLetStatement l => l.ExpressionAst,
            BmReturnStatement ret => ret.ExpressionAst,
            BmRejectStatement reject => reject.Message,
            _ => null
        };
        if (exprAst != null)
            record.ExpressionRootId = _ctx.ExprSerializer.SaveExpressionNodes(exprAst, record.Id, "rule_statement", "expression");

        if (stmt is BmCallStatement callStmt)
        {
            for (int i = 0; i < callStmt.Arguments.Count; i++)
                _ctx.ExprSerializer.SaveExpressionNodes(callStmt.Arguments[i], record.Id, "rule_statement", $"call_arg_{i}");
        }
        if (stmt is BmEmitStatement emitStmt)
        {
            int i = 0;
            foreach (var (fieldName, fieldExpr) in emitStmt.FieldAssignments)
                _ctx.ExprSerializer.SaveExpressionNodes(fieldExpr, record.Id, "rule_statement", $"emit_field_{i++}_{fieldName}");
        }

        targetCollection.Add(record);

        if (stmt is BmWhenStatement whenStmt)
        {
            int childPos = 0;
            foreach (var child in whenStmt.ThenStatements)
                MapRuleStatementSave(child, ruleId, record.Id, childPos++, targetCollection, false);
            foreach (var child in whenStmt.ElseStatements)
                MapRuleStatementSave(child, ruleId, record.Id, childPos++, targetCollection, true);
        }
        if (stmt is BmForeachStatement foreachStmt)
        {
            int childPos = 0;
            foreach (var child in foreachStmt.Body)
                MapRuleStatementSave(child, ruleId, record.Id, childPos++, targetCollection, false);
        }
    }

    public async Task SaveRulesAsync(IEnumerable<BmRule> rules, CancellationToken ct = default)
    {
        foreach (var r in rules) await SaveRuleAsync(r, ct);
    }

    public async Task<List<RuleRecord>> LoadRulesAsync(CancellationToken ct)
    {
        var rules = await _ctx.Db.Rules
            .AsNoTracking()
            .Where(r => r.TenantId == _ctx.TenantId)
            .Include(r => r.Triggers).ThenInclude(t => t.ChangeFields)
            .Include(r => r.Statements)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (rules.Count > 0)
        {
            var ruleIds = rules.Select(r => r.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations
                .AsNoTracking()
                .Where(a => a.OwnerType == "rule" && ruleIds.Contains(a.OwnerId))
                .ToListAsync(ct);
            _ruleAnnotationsByOwner = annotations.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
        }
        else
        {
            _ruleAnnotationsByOwner = new Dictionary<Guid, List<NormalizedAnnotation>>();
        }

        return rules;
    }

    public BmRule MapToBmRule(RuleRecord record, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        var rule = new BmRule { Name = record.Name, TargetEntity = record.TargetEntityName };

        foreach (var trigger in record.Triggers.OrderBy(t => t.Position))
        {
            if (Enum.TryParse<BmTriggerTiming>(trigger.Timing, true, out var timing) &&
                Enum.TryParse<BmTriggerOperation>(trigger.Operation, true, out var operation))
            {
                var triggerEvent = new BmTriggerEvent { Timing = timing, Operation = operation };
                foreach (var cf in trigger.ChangeFields)
                    triggerEvent.ChangeFields.Add(cf.FieldName);
                rule.Triggers.Add(triggerEvent);
            }
        }

        foreach (var stmt in record.Statements.Where(s => s.ParentStatementId == null).OrderBy(s => s.Position))
        {
            var mappedStmt = MapRuleStatementLoad(stmt, record.Statements, nodesByOwner);
            if (mappedStmt != null)
                rule.Statements.Add(mappedStmt);
        }

        if (_ruleAnnotationsByOwner != null && _ruleAnnotationsByOwner.TryGetValue(record.Id, out var ruleAnnotations))
        {
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(ruleAnnotations))
                rule.Annotations.Add(ann);
        }

        return rule;
    }

    private BmRuleStatement? MapRuleStatementLoad(RuleStatement stmt, ICollection<RuleStatement> allStatements, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        BmExpression? expressionAst = null;
        if (stmt.ExpressionRootId.HasValue && nodesByOwner.TryGetValue(stmt.Id, out var nodes))
            expressionAst = _ctx.ExprSerializer.ReconstructBmExpression(stmt.ExpressionRootId.Value, nodes);

        return stmt.StatementType.ToLower() switch
        {
            "validate" => new BmValidateStatement
            {
                Expression = stmt.Expression ?? "", ExpressionAst = expressionAst,
                Message = stmt.Message ?? "",
                Severity = Enum.TryParse<BmSeverity>(stmt.Severity, true, out var sev) ? sev : BmSeverity.Error
            },
            "compute" => new BmComputeStatement { Target = stmt.Target ?? "", Expression = stmt.Expression ?? "", ExpressionAst = expressionAst },
            "when" => MapWhenStatement(stmt, allStatements, nodesByOwner),
            "call" => MapCallStatementFromRule(stmt, nodesByOwner),
            "foreach" => MapForeachStatementFromRule(stmt, allStatements, nodesByOwner),
            "let" => new BmLetStatement { VariableName = stmt.Target ?? "", Expression = stmt.Expression ?? "", ExpressionAst = expressionAst },
            "raise" => new BmRaiseStatement
            {
                Message = stmt.Message ?? "",
                Severity = Enum.TryParse<BmSeverity>(stmt.Severity, true, out var sev2) ? sev2 : BmSeverity.Error
            },
            "return" => new BmReturnStatement { Expression = stmt.Expression ?? "", ExpressionAst = expressionAst },
            "emit" => MapEmitStatementFromRule(stmt, nodesByOwner),
            "reject" => new BmRejectStatement { Message = expressionAst },
            _ => null
        };
    }

    private BmWhenStatement MapWhenStatement(RuleStatement stmt, ICollection<RuleStatement> allStatements, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        var whenStmt = new BmWhenStatement { Condition = stmt.Expression ?? "" };
        if (stmt.ExpressionRootId.HasValue && nodesByOwner.TryGetValue(stmt.Id, out var nodes))
            whenStmt.ConditionAst = _ctx.ExprSerializer.ReconstructBmExpression(stmt.ExpressionRootId.Value, nodes);
        foreach (var child in allStatements.Where(s => s.ParentStatementId == stmt.Id && !s.IsElseBranch).OrderBy(s => s.Position))
        {
            var mapped = MapRuleStatementLoad(child, allStatements, nodesByOwner);
            if (mapped != null) whenStmt.ThenStatements.Add(mapped);
        }
        foreach (var child in allStatements.Where(s => s.ParentStatementId == stmt.Id && s.IsElseBranch).OrderBy(s => s.Position))
        {
            var mapped = MapRuleStatementLoad(child, allStatements, nodesByOwner);
            if (mapped != null) whenStmt.ElseStatements.Add(mapped);
        }
        return whenStmt;
    }

    private BmCallStatement MapCallStatementFromRule(RuleStatement stmt, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        var callStmt = new BmCallStatement { Target = stmt.Target ?? "" };
        if (nodesByOwner.TryGetValue(stmt.Id, out var exprNodes))
        {
            var argGroups = exprNodes
                .Where(e => e.OwnerField != null && e.OwnerField.StartsWith("call_arg_") && e.ParentId == null)
                .OrderBy(e => e.OwnerField)
                .ToList();
            foreach (var argRoot in argGroups)
            {
                var argAst = _ctx.ExprSerializer.ReconstructBmExpression(argRoot.Id, exprNodes);
                if (argAst != null) callStmt.Arguments.Add(argAst);
            }
        }
        return callStmt;
    }

    private BmForeachStatement MapForeachStatementFromRule(RuleStatement stmt, ICollection<RuleStatement> allStatements, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        var foreachStmt = new BmForeachStatement { VariableName = stmt.Target ?? "", Collection = stmt.Expression ?? "" };
        if (stmt.ExpressionRootId.HasValue && nodesByOwner.TryGetValue(stmt.Id, out var nodes))
            foreachStmt.CollectionAst = _ctx.ExprSerializer.ReconstructBmExpression(stmt.ExpressionRootId.Value, nodes);
        foreach (var child in allStatements.Where(s => s.ParentStatementId == stmt.Id).OrderBy(s => s.Position))
        {
            var mapped = MapRuleStatementLoad(child, allStatements, nodesByOwner);
            if (mapped != null) foreachStmt.Body.Add(mapped);
        }
        return foreachStmt;
    }

    private BmEmitStatement MapEmitStatementFromRule(RuleStatement stmt, Dictionary<Guid, List<ExpressionNode>> nodesByOwner)
    {
        var emitStmt = new BmEmitStatement { EventName = stmt.Target ?? "" };
        if (nodesByOwner.TryGetValue(stmt.Id, out var exprNodes))
        {
            var fieldGroups = exprNodes
                .Where(e => e.OwnerField != null && e.OwnerField.StartsWith("emit_field_") && e.ParentId == null)
                .OrderBy(e => e.OwnerField)
                .ToList();
            foreach (var fieldRoot in fieldGroups)
            {
                var parts = fieldRoot.OwnerField!.Split('_', 4);
                if (parts.Length >= 4)
                {
                    var fieldName = parts[3];
                    var fieldAst = _ctx.ExprSerializer.ReconstructBmExpression(fieldRoot.Id, exprNodes);
                    if (fieldAst != null) emitStmt.FieldAssignments[fieldName] = fieldAst;
                }
            }
        }
        return emitStmt;
    }
}
