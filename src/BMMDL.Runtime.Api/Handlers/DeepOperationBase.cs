namespace BMMDL.Runtime.Api.Handlers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using System.Text.Json;

/// <summary>
/// Shared base class for DeepInsertHandler and DeepUpdateHandler.
/// Extracts common logic: navigation property detection, nested data extraction,
/// FK resolution, and rule execution patterns.
/// </summary>
public abstract class DeepOperationBase
{
    protected readonly IMetaModelCache _cache;
    protected readonly IDynamicSqlBuilder _sqlBuilder;
    protected readonly IQueryExecutor _queryExecutor;
    protected readonly ILogger _logger;
    protected readonly ReferentialIntegrityService? _refIntegrity;
    protected readonly IRuleEngine? _ruleEngine;

    protected DeepOperationBase(
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger logger,
        ReferentialIntegrityService? refIntegrity = null,
        IRuleEngine? ruleEngine = null)
    {
        _cache = cache;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _logger = logger;
        _refIntegrity = refIntegrity;
        _ruleEngine = ruleEngine;
    }

    /// <summary>
    /// Represents a nested object extracted from request body.
    /// </summary>
    protected record NestedOperation
    {
        public required string NavigationName { get; init; }
        public required string TargetEntityName { get; init; }
        public required BmCardinality Cardinality { get; init; }
        public object? Data { get; init; }
    }

    /// <summary>
    /// Check if the data contains nested objects that require deep processing.
    /// </summary>
    public bool HasNestedObjects(BmEntity entityDef, Dictionary<string, object?> data)
    {
        foreach (var kvp in data)
        {
            // Check both associations and compositions
            var assoc = entityDef.Associations.FirstOrDefault(a =>
                string.Equals(a.Name, kvp.Key, StringComparison.OrdinalIgnoreCase))
                ?? (BmAssociation?)entityDef.Compositions.FirstOrDefault(c =>
                string.Equals(c.Name, kvp.Key, StringComparison.OrdinalIgnoreCase));

            if (assoc != null && kvp.Value != null)
            {
                if (kvp.Value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Object || je.ValueKind == JsonValueKind.Array)
                        return true;
                }
                else if (kvp.Value is Dictionary<string, object?> || kvp.Value is IEnumerable<object>)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Extract nested objects from request data based on entity associations and compositions.
    /// Unknown keys that look like navigation properties (objects/arrays) are logged as warnings and skipped.
    /// </summary>
    protected List<NestedOperation> ExtractNestedObjects(BmEntity entityDef, Dictionary<string, object?> data)
    {
        var result = new List<NestedOperation>();

        // Build set of known navigation property names for validation
        var allNavProps = entityDef.Associations.Cast<BmAssociation>()
            .Concat(entityDef.Compositions);

        var knownNavNames = new HashSet<string>(
            allNavProps.Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);

        // Also collect known scalar field names to distinguish unknown nav props from unknown fields
        var knownFieldNames = new HashSet<string>(
            entityDef.Fields.Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var assoc in allNavProps)
        {
            // Case-insensitive lookup: OData sends PascalCase but model uses camelCase
            var matchingKey = data.Keys.FirstOrDefault(k =>
                string.Equals(k, assoc.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingKey == null || !data.TryGetValue(matchingKey, out var value) || value == null)
                continue;

            object? nestedData = null;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    nestedData = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
                }
                else if (je.ValueKind == JsonValueKind.Array)
                {
                    nestedData = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(je.GetRawText());
                }
            }
            else if (value is Dictionary<string, object?> dict)
            {
                nestedData = dict;
            }
            else if (value is IEnumerable<object> list)
            {
                nestedData = list;
            }

            if (nestedData != null)
            {
                // Validate data shape matches cardinality
                var isCollection = nestedData is IList<Dictionary<string, object?>> or IEnumerable<object> and not Dictionary<string, object?>;
                if (isCollection && (assoc.Cardinality == BmCardinality.ManyToOne || assoc.Cardinality == BmCardinality.OneToOne))
                {
                    throw new InvalidOperationException(
                        $"Navigation property '{matchingKey}' has cardinality {assoc.Cardinality} " +
                        $"but received an array. Expected a single object.");
                }
                if (!isCollection && nestedData is Dictionary<string, object?> &&
                    assoc.Cardinality == BmCardinality.OneToMany)
                {
                    throw new InvalidOperationException(
                        $"Navigation property '{matchingKey}' has cardinality {assoc.Cardinality} " +
                        $"but received a single object. Expected an array.");
                }

                result.Add(new NestedOperation
                {
                    NavigationName = matchingKey, // Use the key from data for proper removal
                    TargetEntityName = assoc.TargetEntity,
                    Cardinality = assoc.Cardinality,
                    Data = nestedData
                });
            }
        }

        // V3: Log warnings for unknown keys that look like navigation properties (objects/arrays)
        foreach (var kvp in data)
        {
            if (knownNavNames.Contains(kvp.Key) || knownFieldNames.Contains(kvp.Key))
                continue;

            var isNestedStructure = kvp.Value switch
            {
                JsonElement je => je.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
                Dictionary<string, object?> => true,
                IEnumerable<object> => true,
                _ => false
            };

            if (isNestedStructure)
            {
                _logger.LogWarning(
                    "Deep operation on entity '{EntityName}': ignoring unknown key '{Key}' " +
                    "which contains a nested object/array but does not match any known navigation property",
                    entityDef.Name, kvp.Key);
            }
        }

        return result;
    }

    /// <summary>
    /// Get FK field name from association on condition.
    /// </summary>
    protected string? GetForeignKeyFieldName(BmEntity entityDef, string navigationName)
    {
        var assoc = entityDef.Associations.FirstOrDefault(a =>
            string.Equals(a.Name, navigationName, StringComparison.OrdinalIgnoreCase))
            ?? (BmAssociation?)entityDef.Compositions.FirstOrDefault(c =>
                string.Equals(c.Name, navigationName, StringComparison.OrdinalIgnoreCase));

        if (assoc?.OnConditionString != null)
        {
            // Parse "sourceField = $self.targetField" pattern
            var parts = assoc.OnConditionString.Split('=');
            if (parts.Length == 2)
            {
                return parts[0].Trim();
            }
        }

        // Convention: navName + "Id"
        return NamingConvention.GetFkFieldName(navigationName);
    }

    /// <summary>
    /// Parse an item from a collection into a dictionary, handling both Dictionary and JsonElement.
    /// </summary>
    protected static Dictionary<string, object?>? ParseItemData(object item)
    {
        if (item is Dictionary<string, object?> dict)
            return new Dictionary<string, object?>(dict);
        if (item is JsonElement je && je.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
        return null;
    }

    /// <summary>
    /// Execute "before create" rules and apply computed values.
    /// Throws InvalidOperationException if rules fail.
    /// </summary>
    protected async Task ApplyBeforeCreateRulesAsync(
        BmEntity entity,
        Dictionary<string, object?> data,
        EvaluationContext? evalContext,
        string errorContext)
    {
        if (_ruleEngine == null) return;

        var childContext = EvaluationContext.CreateForEntity(evalContext, entity.Name);
        var ruleResult = await _ruleEngine.ExecuteBeforeCreateAsync(entity, data, childContext);
        if (!ruleResult.Success)
        {
            throw new InvalidOperationException(
                $"{errorContext}: {string.Join("; ", ruleResult.Errors.Select(e => e.Message))}");
        }
        foreach (var (field, value) in ruleResult.ComputedValues)
        {
            data[field] = value;
        }
    }

    /// <summary>
    /// Execute "after create" rules (best-effort, logged on failure).
    /// </summary>
    protected async Task InvokeAfterCreateRulesAsync(
        BmEntity entity,
        Dictionary<string, object?> created,
        EvaluationContext? evalContext)
    {
        if (_ruleEngine == null) return;

        try
        {
            var afterContext = EvaluationContext.CreateForEntity(evalContext, entity.Name);
            await _ruleEngine.ExecuteAfterCreateAsync(entity, created, afterContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "After-create rules failed for child {Entity}", entity.Name);
        }
    }

    /// <summary>
    /// Execute "before update" rules and apply computed values.
    /// Throws InvalidOperationException if rules fail.
    /// </summary>
    protected async Task ApplyBeforeUpdateRulesAsync(
        BmEntity entity,
        Dictionary<string, object?> currentData,
        Dictionary<string, object?> updateData,
        EvaluationContext? evalContext,
        string errorContext)
    {
        if (_ruleEngine == null) return;

        var childContext = EvaluationContext.CreateForEntity(evalContext, entity.Name);
        var ruleResult = await _ruleEngine.ExecuteBeforeUpdateAsync(entity, currentData, updateData, childContext);
        if (!ruleResult.Success)
        {
            throw new InvalidOperationException(
                $"{errorContext}: {string.Join("; ", ruleResult.Errors.Select(e => e.Message))}");
        }
        foreach (var (field, value) in ruleResult.ComputedValues)
        {
            updateData[field] = value;
        }
    }

    /// <summary>
    /// Execute "after update" rules (best-effort, logged on failure).
    /// </summary>
    protected async Task InvokeAfterUpdateRulesAsync(
        BmEntity entity,
        Dictionary<string, object?> currentData,
        Dictionary<string, object?> updated,
        EvaluationContext? evalContext)
    {
        if (_ruleEngine == null) return;

        try
        {
            var afterContext = EvaluationContext.CreateForEntity(evalContext, entity.Name);
            await _ruleEngine.ExecuteAfterUpdateAsync(entity, currentData, updated, afterContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "After-update rules failed for child {Entity}", entity.Name);
        }
    }

    /// <summary>
    /// Execute "before delete" rules. Throws if rules fail.
    /// </summary>
    protected async Task ApplyBeforeDeleteRulesAsync(
        BmEntity entity,
        Dictionary<string, object?> data,
        EvaluationContext? evalContext,
        string errorContext)
    {
        if (_ruleEngine == null) return;

        var childContext = EvaluationContext.CreateForEntity(evalContext, entity.Name);
        var ruleResult = await _ruleEngine.ExecuteBeforeDeleteAsync(entity, data, childContext);
        if (!ruleResult.Success)
        {
            throw new InvalidOperationException(
                $"{errorContext}: {string.Join("; ", ruleResult.Errors.Select(e => e.Message))}");
        }
    }
}
