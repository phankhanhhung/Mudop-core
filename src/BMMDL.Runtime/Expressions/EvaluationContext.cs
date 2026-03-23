namespace BMMDL.Runtime.Expressions;

using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Utilities;

/// <summary>
/// Context for evaluating expressions at runtime.
/// Contains the current entity data, parameters, and system context variables.
/// </summary>
public class EvaluationContext
{
    /// <summary>
    /// Current entity data being evaluated.
    /// Keys are field names (camelCase), values are field values.
    /// </summary>
    public Dictionary<string, object?> EntityData { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Named parameters passed to the expression.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Related entities for navigation (e.g., order.customer.name).
    /// Key is the navigation path root, value is the related entity data.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>> RelatedEntities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Previous entity data before an update (for $old context variable).
    /// </summary>
    public Dictionary<string, object?>? OldEntityData { get; set; }

    /// <summary>
    /// Current user information.
    /// </summary>
    public UserContext? User { get; set; }

    /// <summary>
    /// Current tenant ID.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Evaluation timestamp (for $now, $today).
    /// </summary>
    public DateTime EvaluationTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Name of the entity being evaluated (needed for aggregate resolution).
    /// </summary>
    public string? EntityName { get; set; }

    /// <summary>
    /// Current service name for call statement disambiguation.
    /// When resolving unbound call targets, actions from this service are preferred.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Async resolver for aggregate expressions (COUNT, SUM, AVG, MIN, MAX).
    /// Set by the rule engine when DB access is available.
    /// </summary>
    public Func<BmAggregateExpression, EvaluationContext, Task<object?>>? AggregateResolver { get; set; }

    /// <summary>
    /// Create a child context inheriting all state from this context.
    /// Parameters are shallow-cloned so child modifications don't affect the parent.
    /// Optionally merges additional parameters into the child.
    /// </summary>
    public EvaluationContext CreateChild(Dictionary<string, object?>? additionalParameters = null)
    {
        var child = new EvaluationContext
        {
            EntityData = EntityData,
            Parameters = new Dictionary<string, object?>(Parameters, StringComparer.OrdinalIgnoreCase),
            RelatedEntities = RelatedEntities,
            User = User,
            TenantId = TenantId,
            EvaluationTime = EvaluationTime,
            ServiceName = ServiceName,
            AggregateResolver = AggregateResolver,
            EntityName = EntityName,
            OldEntityData = OldEntityData
        };

        if (additionalParameters != null)
        {
            foreach (var p in additionalParameters)
                child.Parameters[p.Key] = p.Value;
        }

        return child;
    }

    /// <summary>
    /// Create a child context for a different entity (used by deep insert/update handlers).
    /// Only copies tenant/user/time context — not entity data or parameters.
    /// </summary>
    public static EvaluationContext CreateForEntity(EvaluationContext? parent, string entityName)
    {
        if (parent != null)
        {
            return new EvaluationContext
            {
                TenantId = parent.TenantId,
                User = parent.User,
                EvaluationTime = parent.EvaluationTime,
                EntityName = entityName
            };
        }
        return new EvaluationContext { EntityName = entityName };
    }

    /// <summary>
    /// Create an empty context.
    /// </summary>
    public static EvaluationContext Empty() => new();

    /// <summary>
    /// Create a context with entity data.
    /// </summary>
    public static EvaluationContext FromEntity(Dictionary<string, object?> entityData) =>
        new() { EntityData = entityData };

    /// <summary>
    /// Create a context with entity data and parameters.
    /// </summary>
    public static EvaluationContext FromEntityAndParameters(
        Dictionary<string, object?> entityData,
        Dictionary<string, object?> parameters) =>
        new() { EntityData = entityData, Parameters = parameters };
}

/// <summary>
/// User context for evaluating $user expressions.
/// </summary>
public class UserContext
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid? TenantId { get; set; }
    public List<string> Roles { get; set; } = new();
    public Dictionary<string, object?> Claims { get; set; } = new();
    
    /// <summary>
    /// Get a property value by name.
    /// </summary>
    public object? GetProperty(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "id" => Id,
            "username" => Username,
            "email" => Email,
            "tenantid" or SchemaConstants.TenantIdColumn => TenantId,
            "roles" => Roles,
            _ => Claims.TryGetValue(name, out var value) ? value : null
        };
    }
}
