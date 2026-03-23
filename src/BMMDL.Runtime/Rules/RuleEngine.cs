namespace BMMDL.Runtime.Rules;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Rule engine that executes business rules (validation, computed fields) at runtime.
/// Integrates with CRUD operations to enforce business logic.
/// </summary>
public class RuleEngine : IRuleEngine
{
    private readonly IMetaModelCache _metaModelCache;
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly AggregateExpressionResolver? _aggregateResolver;
    private readonly ILogger<RuleEngine> _logger;
    private readonly RuleStatementExecutor _statementExecutor;

    public RuleEngine(
        IMetaModelCache metaModelCache,
        IRuntimeExpressionEvaluator evaluator,
        ILogger<RuleEngine> logger,
        AggregateExpressionResolver? aggregateResolver = null,
        IUnitOfWork? unitOfWork = null,
        IEventPublisher? eventPublisher = null)
    {
        _metaModelCache = metaModelCache ?? throw new ArgumentNullException(nameof(metaModelCache));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aggregateResolver = aggregateResolver;
        _statementExecutor = new RuleStatementExecutor(evaluator, metaModelCache, logger, unitOfWork, eventPublisher);
    }

    public async Task<RuleExecutionResult> ExecuteBeforeCreateAsync(
        BmEntity entity,
        Dictionary<string, object?> data,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing BEFORE CREATE rules for {Entity}", entity.QualifiedName);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.Before, BmTriggerOperation.Create);
        return await ExecuteRulesAsync(rules, data, context, "create", entity.Name);
    }

    public async Task<RuleExecutionResult> ExecuteBeforeUpdateAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        Dictionary<string, object?> updateData,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing BEFORE UPDATE rules for {Entity}", entity.QualifiedName);

        context.OldEntityData = new Dictionary<string, object?>(existingData, StringComparer.OrdinalIgnoreCase);

        var mergedData = new Dictionary<string, object?>(existingData, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in updateData)
        {
            mergedData[kvp.Key] = kvp.Value;
        }

        var changedFields = DetectChangedFields(existingData, updateData);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.Before, BmTriggerOperation.Update);
        var onChangeRules = GetOnChangeRules(entity.QualifiedName, changedFields);

        var allRules = new List<BmRule>(rules);
        allRules.AddRange(onChangeRules);

        return await ExecuteRulesAsync(allRules, mergedData, context, "update", entity.Name);
    }

    public async Task<RuleExecutionResult> ExecuteBeforeDeleteAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing BEFORE DELETE rules for {Entity}", entity.QualifiedName);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.Before, BmTriggerOperation.Delete);
        return await ExecuteRulesAsync(rules, existingData, context, "delete", entity.Name);
    }

    public async Task ExecuteAfterCreateAsync(
        BmEntity entity,
        Dictionary<string, object?> createdData,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing AFTER CREATE rules for {Entity}", entity.QualifiedName);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.After, BmTriggerOperation.Create);
        await ExecuteRulesAsync(rules, createdData, context, "create", entity.Name);
    }

    public async Task ExecuteAfterUpdateAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        Dictionary<string, object?> updatedData,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing AFTER UPDATE rules for {Entity}", entity.QualifiedName);

        context.OldEntityData = new Dictionary<string, object?>(existingData, StringComparer.OrdinalIgnoreCase);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.After, BmTriggerOperation.Update);
        await ExecuteRulesAsync(rules, updatedData, context, "update", entity.Name);
    }

    public async Task ExecuteAfterDeleteAsync(
        BmEntity entity,
        Dictionary<string, object?> deletedData,
        EvaluationContext context)
    {
        var deletedId = deletedData.GetValueOrDefault("Id") ?? deletedData.GetValueOrDefault("id");
        _logger.LogDebug("Executing AFTER DELETE rules for {Entity} id={Id}", entity.QualifiedName, deletedId);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.After, BmTriggerOperation.Delete);
        await ExecuteRulesAsync(rules, deletedData, context, "delete", entity.Name);
    }

    public async Task<RuleExecutionResult> ExecuteBeforeReadAsync(
        BmEntity entity,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing BEFORE READ rules for {Entity}", entity.QualifiedName);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.Before, BmTriggerOperation.Read);
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        return await ExecuteRulesAsync(rules, data, context, "read", entity.Name);
    }

    public async Task<RuleExecutionResult> ExecuteAfterReadAsync(
        BmEntity entity,
        List<Dictionary<string, object?>> results,
        EvaluationContext context)
    {
        _logger.LogDebug("Executing AFTER READ rules for {Entity}, {Count} results", entity.QualifiedName, results.Count);

        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.After, BmTriggerOperation.Read);
        if (rules.Count == 0) return RuleExecutionResult.Ok();

        var aggregateResult = new RuleExecutionResult();
        foreach (var row in results)
        {
            var rowResult = await ExecuteRulesAsync(rules, row, context, "read", entity.Name);
            aggregateResult.Merge(rowResult);

            foreach (var kvp in rowResult.ComputedValues)
            {
                row[kvp.Key] = kvp.Value;
            }

            if (aggregateResult.Rejected) break;
        }

        return aggregateResult;
    }

    /// <summary>
    /// Execute a list of statements for bound actions/functions.
    /// Translates RuleExecutionResult semantics to action semantics:
    /// errors/rejections throw InvalidOperationException, return values propagate directly.
    /// </summary>
    public async Task<object?> ExecuteStatementsAsync(
        IList<BMMDL.MetaModel.BmRuleStatement> statements,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Executing {Count} statements for bound operation", statements.Count);

        var result = new Dictionary<string, object?>();

        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();

            var stmtResult = await _statementExecutor.ExecuteStatementAsync(statement, context);

            // Return statement: propagate value directly
            if (stmtResult.ShouldReturn) return stmtResult.ReturnValue;

            // Rejection/errors: throw (bound action semantics)
            if (stmtResult.Rejected || stmtResult.Errors.Count > 0)
            {
                var msg = stmtResult.Errors.FirstOrDefault()?.Message ?? "Operation rejected by business rule";
                throw new InvalidOperationException(msg);
            }

            // Collect computed values and update parameters
            foreach (var kvp in stmtResult.ComputedValues)
            {
                result[kvp.Key] = kvp.Value;
                context.Parameters[kvp.Key] = kvp.Value;
            }

            // Let statements also contribute to result dict
            if (statement is BmLetStatement letStmt
                && context.Parameters.TryGetValue(letStmt.VariableName, out var letVal))
            {
                result[letStmt.VariableName] = letVal;
            }
        }

        return result;
    }

    private async Task<RuleExecutionResult> ExecuteRulesAsync(
        IReadOnlyList<BmRule> rules,
        Dictionary<string, object?> data,
        EvaluationContext context,
        string operation,
        string? entityName = null)
    {
        var result = new RuleExecutionResult();

        if (rules.Count == 0)
        {
            _logger.LogDebug("No rules to execute for {Operation}", operation);
            return result;
        }

        _logger.LogDebug("Executing {Count} rules for {Operation}", rules.Count, operation);

        var caseInsensitiveData = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        var evalContext = new EvaluationContext
        {
            EntityData = caseInsensitiveData,
            OldEntityData = context.OldEntityData,
            Parameters = context.Parameters,
            RelatedEntities = context.RelatedEntities,
            User = context.User,
            TenantId = context.TenantId,
            EvaluationTime = context.EvaluationTime,
            EntityName = entityName ?? context.EntityName,
            ServiceName = context.ServiceName,
            AggregateResolver = _aggregateResolver != null
                ? (agg, ctx) => _aggregateResolver.ResolveAsync(agg, ctx)
                : context.AggregateResolver
        };

        foreach (var rule in rules)
        {
            try
            {
                var ruleResult = await ExecuteRuleAsync(rule, evalContext);
                result.Merge(ruleResult);

                if (result.ShouldReturn || result.Rejected) break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing rule {RuleName}", rule.Name);
                result.AddError("", $"Rule execution error: {ex.Message}", BmSeverity.Error);
            }
        }

        _logger.LogDebug("Rule execution complete. Errors: {ErrorCount}, Computed: {ComputedCount}",
            result.Errors.Count, result.ComputedValues.Count);

        return result;
    }

    private async Task<RuleExecutionResult> ExecuteRuleAsync(BmRule rule, EvaluationContext context)
    {
        var result = new RuleExecutionResult();

        _logger.LogDebug("Executing rule: {RuleName}", rule.Name);

        foreach (var statement in rule.Statements)
        {
            var stmtResult = await _statementExecutor.ExecuteStatementAsync(statement, context);
            result.Merge(stmtResult);
            if (result.Rejected || result.ShouldReturn) break;
        }

        return result;
    }

    private IReadOnlyList<BmRule> GetRulesForTrigger(string entityName, BmTriggerTiming timing, BmTriggerOperation operation)
    {
        var allRules = _metaModelCache.GetRulesForEntity(entityName);

        return allRules
            .Where(r => r.Triggers.Any(t => t.Timing == timing && t.Operation == operation))
            .ToList();
    }

    private IReadOnlyList<BmRule> GetOnChangeRules(string entityName, HashSet<string> changedFields)
    {
        if (changedFields.Count == 0) return Array.Empty<BmRule>();

        var allRules = _metaModelCache.GetRulesForEntity(entityName);

        return allRules
            .Where(r => r.Triggers.Any(t =>
                t.Timing == BmTriggerTiming.OnChange &&
                t.ChangeFields.Count > 0 &&
                t.ChangeFields.Any(f => changedFields.Contains(f))))
            .ToList();
    }

    private static HashSet<string> DetectChangedFields(
        Dictionary<string, object?> existingData,
        Dictionary<string, object?> updateData)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in updateData)
        {
            if (!existingData.TryGetValue(kvp.Key, out var existingValue))
            {
                changed.Add(kvp.Key);
            }
            else if (!ValuesEqual(existingValue, kvp.Value))
            {
                changed.Add(kvp.Key);
            }
        }

        return changed;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Equals(b)) return true;

        try
        {
            if (a is IConvertible ca && b is IConvertible cb)
            {
                var da = Convert.ToDouble(ca);
                var db = Convert.ToDouble(cb);
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                return da == db;
            }
        }
        catch
        {
            // Conversion failed — not equal
        }

        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}
