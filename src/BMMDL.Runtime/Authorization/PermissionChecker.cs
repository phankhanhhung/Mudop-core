namespace BMMDL.Runtime.Authorization;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default policy when no access control rules are defined for an entity.
/// </summary>
public enum DefaultAccessPolicy
{
    /// <summary>Deny access when no rules exist (secure by default, recommended for production).</summary>
    Deny,
    /// <summary>Allow access when no rules exist (useful for development and modules without access control).</summary>
    Allow
}

/// <summary>
/// Interface for permission checking.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>
    /// Check if an operation is allowed for the given user.
    /// </summary>
    Task<AccessDecision> CheckAccessAsync(
        BmEntity entity,
        CrudOperation operation,
        Models.UserContext user,
        Dictionary<string, object?>? data,
        EvaluationContext context);
}

/// <summary>
/// Evaluates access control rules to determine if an operation is allowed.
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IMetaModelCache _metaModelCache;
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly ILogger<PermissionChecker> _logger;
    private readonly DefaultAccessPolicy _defaultPolicy;

    public PermissionChecker(
        IMetaModelCache metaModelCache,
        IRuntimeExpressionEvaluator evaluator,
        ILogger<PermissionChecker> logger,
        DefaultAccessPolicy defaultPolicy = DefaultAccessPolicy.Deny)
    {
        _metaModelCache = metaModelCache;
        _evaluator = evaluator;
        _logger = logger;
        _defaultPolicy = defaultPolicy;
    }

    public async Task<AccessDecision> CheckAccessAsync(
        BmEntity entity,
        CrudOperation operation,
        Models.UserContext user,
        Dictionary<string, object?>? data,
        EvaluationContext context)
    {
        var operationStr = operation.ToOperationString();
        _logger.LogDebug("Checking {Operation} access for {Entity} by user {User}",
            operationStr, entity.QualifiedName, user.Username);

        // Get access control rules for the entity, resolving ExtendsFrom chains
        var accessControls = ResolveAccessControlChain(entity.QualifiedName);

        if (accessControls.Count == 0)
        {
            if (_defaultPolicy == DefaultAccessPolicy.Allow)
            {
                _logger.LogDebug(
                    "No access rules defined for {Entity}. Allowing {Operation} by {User} (default-allow policy)",
                    entity.QualifiedName, operationStr, user.Username);
                return AccessDecision.Allowed(["default-allow:no-rules"]);
            }

            // Fail-close: deny by default when no rules exist (secure for production)
            _logger.LogWarning(
                "No access rules defined for {Entity}. Denying {Operation} by {User} (fail-close policy)",
                entity.QualifiedName, operationStr, user.Username);
            return AccessDecision.Denied(
                $"No access rules defined for {entity.Name}. Configure access_control in BMMDL.");
        }

        var allowedRules = new List<string>();
        var deniedRules = new List<string>();

        foreach (var acl in accessControls)
        {
            foreach (var rule in acl.Rules)
            {
                // Check if rule applies to this operation
                if (!RuleMatchesOperation(rule, operationStr))
                    continue;

                // Check if rule applies to this user
                if (!RulePrincipalMatchesUser(rule, user))
                    continue;

                // Check scope: if rule has a scope restriction, verify it matches current context
                if (rule.Scope.HasValue && !RuleScopeMatchesContext(rule.Scope.Value, entity, user, data, context))
                {
                    _logger.LogDebug("Rule scope {Scope} does not match current context for {Entity}",
                        rule.Scope, entity.QualifiedName);
                    continue;
                }

                // Evaluate WHERE condition if present
                if (rule.WhereConditionExpr != null && data != null)
                {
                    var evalContext = new EvaluationContext
                    {
                        EntityData = data,
                        Parameters = context.Parameters,
                        RelatedEntities = context.RelatedEntities,
                        User = context.User,
                        TenantId = context.TenantId,
                        EvaluationTime = context.EvaluationTime
                    };

                    try
                    {
                        var conditionResult = await _evaluator.EvaluateAsync(rule.WhereConditionExpr, evalContext);
                        if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                        {
                            _logger.LogDebug("Rule WHERE condition not met: {Condition}", rule.WhereCondition);
                            continue; // Condition not met, rule doesn't apply
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error evaluating rule WHERE condition");
                        continue;
                    }
                }

                // Apply rule
                switch (rule.RuleType)
                {
                    case BmAccessRuleType.Grant:
                        allowedRules.Add($"{acl.Name}:grant");
                        _logger.LogDebug("GRANT matched: {AclName} for {Operation}", acl.Name, operationStr);
                        break;

                    case BmAccessRuleType.Deny:
                        deniedRules.Add($"{acl.Name}:deny");
                        _logger.LogDebug("DENY matched: {AclName} for {Operation}", acl.Name, operationStr);
                        // DENY takes precedence - return immediately
                        return AccessDecision.Denied(
                            $"Access denied by rule: {acl.Name}",
                            deniedRules.ToArray());
                }
            }
        }

        // If any grant matched, allow
        if (allowedRules.Count > 0)
        {
            return AccessDecision.Allowed(allowedRules.ToArray());
        }

        // No matching rules - deny by default (fail-close for security)
        _logger.LogDebug("No matching rules for {Operation} on {Entity} by {User}",
            operationStr, entity.QualifiedName, user.Username);
        return AccessDecision.Denied(
            $"No grant rule for {operationStr} on {entity.Name}");
    }

    /// <summary>
    /// Resolve the full access control chain by following both ACL-level ExtendsFrom references
    /// and entity-level inheritance (BmEntity.ExtendsFrom).
    /// Uses BFS to traverse the full inheritance tree with cycle detection via visited set.
    /// Child rules take precedence over parent rules for the same operation+principal.
    /// </summary>
    private List<BmAccessControl> ResolveAccessControlChain(string entityName)
    {
        var directAcls = _metaModelCache.GetAccessControlsForEntity(entityName);

        // BFS through both ACL ExtendsFrom and entity inheritance chains
        var result = new List<BmAccessControl>(directAcls);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entityName };
        var queue = new Queue<string>();

        // Seed queue with ACL-level ExtendsFrom references
        foreach (var acl in directAcls)
        {
            if (!string.IsNullOrEmpty(acl.ExtendsFrom) && !visited.Contains(acl.ExtendsFrom))
            {
                queue.Enqueue(acl.ExtendsFrom);
            }
        }

        // Also seed queue with entity-level inheritance (BmEntity.ExtendsFrom)
        var entity = _metaModelCache.GetEntity(entityName);
        if (entity != null && !string.IsNullOrEmpty(entity.ExtendsFrom) && !visited.Contains(entity.ExtendsFrom))
        {
            queue.Enqueue(entity.ExtendsFrom);
        }

        while (queue.Count > 0)
        {
            var parentName = queue.Dequeue();

            // Cycle detection: skip already-visited entities
            if (!visited.Add(parentName))
                continue;

            var parentAcls = _metaModelCache.GetAccessControlsForEntity(parentName);
            if (parentAcls.Count > 0)
            {
                result.AddRange(parentAcls);

                // Enqueue ACL-level ExtendsFrom references from parent ACLs
                foreach (var parentAcl in parentAcls)
                {
                    if (!string.IsNullOrEmpty(parentAcl.ExtendsFrom) && !visited.Contains(parentAcl.ExtendsFrom))
                    {
                        queue.Enqueue(parentAcl.ExtendsFrom);
                    }
                }
            }

            // Also follow entity-level inheritance from parent entity
            var parentEntity = _metaModelCache.GetEntity(parentName);
            if (parentEntity != null && !string.IsNullOrEmpty(parentEntity.ExtendsFrom) && !visited.Contains(parentEntity.ExtendsFrom))
            {
                queue.Enqueue(parentEntity.ExtendsFrom);
            }
        }

        return result;
    }

    /// <summary>
    /// Check if rule matches the operation.
    /// </summary>
    private static bool RuleMatchesOperation(BmAccessRule rule, string operation)
    {
        if (rule.Operations.Count == 0)
            return true; // No operations specified = all operations
            
        return rule.Operations.Any(op => 
            op.Equals(operation, StringComparison.OrdinalIgnoreCase) ||
            op.Equals("*", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            // WRITE expands to create, update, delete
            (op.Equals("write", StringComparison.OrdinalIgnoreCase) && 
             (operation.Equals("create", StringComparison.OrdinalIgnoreCase) ||
              operation.Equals("update", StringComparison.OrdinalIgnoreCase) ||
              operation.Equals("delete", StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// Check if rule principal matches the user.
    /// </summary>
    private static bool RulePrincipalMatchesUser(BmAccessRule rule, Models.UserContext user)
    {
        if (rule.Principal == null)
            return true; // No principal specified = all authenticated users

        return rule.Principal.Type switch
        {
            BmPrincipalType.Authenticated => true,
            BmPrincipalType.Anonymous => false, // User is authenticated
            BmPrincipalType.Role => rule.Principal.Values.Any(role =>
                user.Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase))),
            BmPrincipalType.User => rule.Principal.Values.Any(userId =>
                userId.Equals(user.UserId.ToString(), StringComparison.OrdinalIgnoreCase) ||
                userId.Equals(user.Username, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    /// <summary>
    /// Check if rule scope matches the current evaluation context.
    /// Global scope always matches. Tenant scope requires matching tenant_id.
    /// Company scope requires matching company_id field in entity data.
    /// </summary>
    private bool RuleScopeMatchesContext(
        BmAccessScope scope,
        BmEntity entity,
        Models.UserContext user,
        Dictionary<string, object?>? data,
        EvaluationContext context)
    {
        switch (scope)
        {
            case BmAccessScope.Global:
                return true;

            case BmAccessScope.Tenant:
                // Tenant-scoped: rule only applies if entity is tenant-scoped
                // and user's tenant matches the context tenant
                if (!entity.TenantScoped)
                    return true; // Non-tenant-scoped entities are accessible from any tenant scope
                if (!context.TenantId.HasValue)
                    return false; // No tenant context, tenant-scoped rules don't apply
                return user.TenantId == context.TenantId.Value;

            case BmAccessScope.Company:
                // Company-scoped: check if entity data has a company_id that matches user's context
                if (data == null)
                {
                    _logger.LogWarning(
                        "Company scope denied: no entity data available for {Entity}, denying access to user {User}",
                        entity.QualifiedName, user.Username);
                    return false;
                }
                var companyField = data.Keys
                    .FirstOrDefault(k => k.Equals("companyId", StringComparison.OrdinalIgnoreCase) ||
                                        k.Equals("company_id", StringComparison.OrdinalIgnoreCase));
                if (companyField == null)
                {
                    _logger.LogWarning(
                        "Company scope denied: entity {Entity} has no company_id field, denying access to user {User}",
                        entity.QualifiedName, user.Username);
                    return false;
                }
                // Check user's permissions include the company context
                var entityCompanyId = data[companyField]?.ToString();
                if (string.IsNullOrEmpty(entityCompanyId))
                {
                    _logger.LogWarning(
                        "Company scope denied: entity {Entity} has empty company_id value, denying access to user {User}",
                        entity.QualifiedName, user.Username);
                    return false;
                }
                // User must have permission for this company (via Permissions list)
                return user.HasPermission($"company:{entityCompanyId}") || user.HasRole("SystemAdmin");

            default:
                return true;
        }
    }

}
