using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities.Normalized;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmAccessControl objects.
/// </summary>
internal sealed class AccessControlPersister
{
    private readonly RepositoryContext _ctx;

    public AccessControlPersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SaveAccessControlAsync(BmAccessControl ac, CancellationToken ct = default)
    {
        var existing = await _ctx.Db.AccessControls
            .Include(a => a.Rules).ThenInclude(r => r.Operations)
            .Include(a => a.Rules).ThenInclude(r => r.Principals)
            .Include(a => a.Rules).ThenInclude(r => r.FieldRestrictions)
            .FirstOrDefaultAsync(a => a.Name == ac.Name, ct);

        AccessControlRecord record;
        if (existing != null)
        {
            var ruleIds = existing.Rules.Select(r => r.Id).ToList();
            if (ruleIds.Count > 0)
            {
                await _ctx.Db.ExpressionNodes
                    .Where(n => n.OwnerType == "access_rule" && ruleIds.Contains(n.OwnerId))
                    .ExecuteDeleteAsync(ct);
                var frIds = existing.Rules.SelectMany(r => r.FieldRestrictions).Select(f => f.Id).ToList();
                if (frIds.Count > 0)
                {
                    await _ctx.Db.ExpressionNodes
                        .Where(n => n.OwnerType == "field_restriction" && frIds.Contains(n.OwnerId))
                        .ExecuteDeleteAsync(ct);
                }
            }
            existing.TargetEntityName = ac.TargetEntity;
            existing.ExtendsFrom = ac.ExtendsFrom;
            existing.Rules.Clear();
            record = existing;
        }
        else
        {
            record = new AccessControlRecord
            {
                Id = Guid.NewGuid(), TenantId = _ctx.TenantId,
                Name = ac.Name, TargetEntityName = ac.TargetEntity,
                ExtendsFrom = ac.ExtendsFrom, CreatedAt = DateTime.UtcNow
            };
            _ctx.Db.AccessControls.Add(record);
        }

        int pos = 0;
        foreach (var rule in ac.Rules)
        {
            var ruleRecord = new AccessRule
            {
                Id = Guid.NewGuid(), AccessControlId = record.Id,
                RuleType = rule.RuleType.ToString().ToLower(),
                Scope = rule.Scope?.ToString(),
                WhereCondition = rule.WhereCondition,
                Position = pos++
            };
            if (rule.WhereConditionExpr != null)
                ruleRecord.WhereConditionExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(rule.WhereConditionExpr, ruleRecord.Id, "access_rule", "where_condition");
            foreach (var op in rule.Operations)
                ruleRecord.Operations.Add(new AccessRuleOperation { RuleId = ruleRecord.Id, Operation = op.ToLower() });
            if (rule.Principal != null)
            {
                foreach (var v in rule.Principal.Values)
                    ruleRecord.Principals.Add(new AccessRulePrincipal
                    {
                        Id = Guid.NewGuid(), RuleId = ruleRecord.Id,
                        PrincipalType = rule.Principal.Type.ToString().ToLower(),
                        PrincipalValue = v
                    });
            }
            foreach (var fr in rule.FieldRestrictions)
            {
                var frRecord = new AccessFieldRestriction
                {
                    Id = Guid.NewGuid(), RuleId = ruleRecord.Id,
                    FieldName = fr.FieldName,
                    AccessType = fr.AccessType.ToString().ToLower(),
                    Condition = fr.Condition, MaskType = fr.MaskType
                };
                if (fr.ConditionExpr != null)
                    frRecord.ConditionExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(fr.ConditionExpr, frRecord.Id, "field_restriction", "condition");
                ruleRecord.FieldRestrictions.Add(frRecord);
            }
            record.Rules.Add(ruleRecord);
        }

        await _ctx.Db.SaveChangesAsync(ct);
    }

    public async Task SaveAccessControlsAsync(IEnumerable<BmAccessControl> accessControls, CancellationToken ct = default)
    {
        foreach (var ac in accessControls) await SaveAccessControlAsync(ac, ct);
    }

    public async Task<List<AccessControlRecord>> LoadAccessControlsAsync(CancellationToken ct)
    {
        return await _ctx.Db.AccessControls
            .AsNoTracking()
            .Where(ac => ac.TenantId == _ctx.TenantId)
            .Include(ac => ac.Rules).ThenInclude(r => r.Operations)
            .Include(ac => ac.Rules).ThenInclude(r => r.Principals)
            .Include(ac => ac.Rules).ThenInclude(r => r.FieldRestrictions)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public BmAccessControl MapToBmAccessControl(
        AccessControlRecord record,
        Dictionary<Guid, List<ExpressionNode>>? nodesByOwner = null,
        Dictionary<Guid, List<ExpressionNode>>? frNodesByOwner = null)
    {
        var ac = new BmAccessControl
        {
            Name = record.Name, TargetEntity = record.TargetEntityName,
            ExtendsFrom = record.ExtendsFrom
        };

        foreach (var rule in record.Rules.OrderBy(r => r.Position))
        {
            var bmRule = new BmAccessRule
            {
                RuleType = Enum.TryParse<BmAccessRuleType>(rule.RuleType, true, out var rt) ? rt : BmAccessRuleType.Grant,
                WhereCondition = rule.WhereCondition,
                Scope = Enum.TryParse<BmAccessScope>(rule.Scope, true, out var scope) ? scope : null
            };
            if (rule.WhereConditionExprRootId.HasValue && nodesByOwner != null
                && nodesByOwner.TryGetValue(rule.Id, out var exprNodes))
            {
                bmRule.WhereConditionExpr = _ctx.ExprSerializer.ReconstructBmExpression(rule.WhereConditionExprRootId.Value, exprNodes);
            }
            foreach (var op in rule.Operations)
                bmRule.Operations.Add(op.Operation);
            if (rule.Principals.Count > 0)
            {
                var firstPrincipal = rule.Principals.First();
                bmRule.Principal = new BmPrincipal
                {
                    Type = Enum.TryParse<BmPrincipalType>(firstPrincipal.PrincipalType, true, out var pt) ? pt : BmPrincipalType.Authenticated
                };
                foreach (var p in rule.Principals)
                {
                    if (!string.IsNullOrEmpty(p.PrincipalValue))
                        bmRule.Principal.Values.Add(p.PrincipalValue);
                }
            }
            foreach (var fr in rule.FieldRestrictions)
            {
                var bmFr = new BmFieldRestriction
                {
                    FieldName = fr.FieldName,
                    AccessType = Enum.TryParse<BmFieldAccessType>(fr.AccessType, true, out var fat) ? fat : BmFieldAccessType.Visible,
                    Condition = fr.Condition, MaskType = fr.MaskType
                };
                if (fr.ConditionExprRootId.HasValue && frNodesByOwner != null
                    && frNodesByOwner.TryGetValue(fr.Id, out var frExprNodes))
                {
                    bmFr.ConditionExpr = _ctx.ExprSerializer.ReconstructBmExpression(fr.ConditionExprRootId.Value, frExprNodes);
                }
                bmRule.FieldRestrictions.Add(bmFr);
            }
            ac.Rules.Add(bmRule);
        }

        return ac;
    }
}
