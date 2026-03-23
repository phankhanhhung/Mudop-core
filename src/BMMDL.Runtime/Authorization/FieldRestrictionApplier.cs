namespace BMMDL.Runtime.Authorization;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Interface for applying field restrictions.
/// </summary>
public interface IFieldRestrictionApplier
{
    /// <summary>
    /// Apply field restrictions to entity data based on user roles.
    /// </summary>
    Dictionary<string, object?> ApplyRestrictions(
        BmEntity entity,
        Dictionary<string, object?> data,
        Models.UserContext user,
        EvaluationContext context);

    /// <summary>
    /// Get field names that are readonly for the given user based on access control rules.
    /// </summary>
    HashSet<string> GetReadonlyFieldNames(
        BmEntity entity,
        Models.UserContext user,
        Dictionary<string, object?>? data = null);
}

/// <summary>
/// Applies field-level access restrictions (hidden, masked, readonly).
/// </summary>
public class FieldRestrictionApplier : IFieldRestrictionApplier
{
    private readonly IMetaModelCache _metaModelCache;
    private readonly IRuntimeExpressionEvaluator _evaluator;
    private readonly ILogger<FieldRestrictionApplier> _logger;

    public FieldRestrictionApplier(
        IMetaModelCache metaModelCache,
        IRuntimeExpressionEvaluator evaluator,
        ILogger<FieldRestrictionApplier> logger)
    {
        _metaModelCache = metaModelCache;
        _evaluator = evaluator;
        _logger = logger;
    }

    public Dictionary<string, object?> ApplyRestrictions(
        BmEntity entity,
        Dictionary<string, object?> data,
        Models.UserContext user,
        EvaluationContext context)
    {
        var accessControls = ResolveFieldRestrictionChain(entity);

        if (accessControls.Count == 0)
        {
            return data; // No restrictions
        }

        // Collect all field restrictions that apply to this user
        var restrictions = new Dictionary<string, BmFieldAccessType>(StringComparer.OrdinalIgnoreCase);
        var maskTypes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var acl in accessControls)
        {
            foreach (var rule in acl.Rules)
            {
                if (rule.RuleType != BmAccessRuleType.RestrictFields)
                    continue;

                // Check if rule applies to this user
                if (rule.Principal != null && !PrincipalMatchesUser(rule.Principal, user))
                    continue;

                foreach (var fieldRestriction in rule.FieldRestrictions)
                {
                    // Evaluate condition if present
                    if (fieldRestriction.ConditionExpr != null)
                    {
                        try
                        {
                            var evalContext = new EvaluationContext
                            {
                                EntityData = data,
                                User = new Expressions.UserContext
                                {
                                    Id = user.UserId,
                                    Username = user.Username,
                                    Email = user.Email,
                                    TenantId = user.TenantId,
                                    Roles = user.Roles.ToList()
                                },
                                TenantId = user.TenantId
                            };
                            
                            var conditionResult = _evaluator.Evaluate(fieldRestriction.ConditionExpr, evalContext);
                            if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                            {
                                continue; // Condition not met, skip this restriction
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error evaluating field restriction condition for {Field}", 
                                fieldRestriction.FieldName);
                            continue;
                        }
                    }

                    // Apply restriction (most restrictive wins)
                    var fieldName = fieldRestriction.FieldName;
                    var newAccessType = fieldRestriction.AccessType;
                    
                    if (restrictions.TryGetValue(fieldName, out var existingType))
                    {
                        // Hidden > Masked > Readonly > Visible
                        if (IsMoreRestrictive(newAccessType, existingType))
                        {
                            restrictions[fieldName] = newAccessType;
                            maskTypes[fieldName] = fieldRestriction.MaskType;
                        }
                    }
                    else
                    {
                        restrictions[fieldName] = newAccessType;
                        maskTypes[fieldName] = fieldRestriction.MaskType;
                    }
                }
            }
        }

        if (restrictions.Count == 0)
        {
            return data; // No field restrictions apply
        }

        // Apply restrictions to data
        var result = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);

        foreach (var (fieldName, accessType) in restrictions)
        {
            switch (accessType)
            {
                case BmFieldAccessType.Hidden:
                    // Remove field entirely
                    result.Remove(fieldName);
                    _logger.LogDebug("Field {Field} hidden for user {User}", fieldName, user.Username);
                    break;
                    
                case BmFieldAccessType.Masked:
                    // Mask the value
                    if (result.TryGetValue(fieldName, out var value) && value != null)
                    {
                        result[fieldName] = MaskValue(value, maskTypes.GetValueOrDefault(fieldName));
                        _logger.LogDebug("Field {Field} masked for user {User}", fieldName, user.Username);
                    }
                    break;
                    
                case BmFieldAccessType.Readonly:
                    // Readonly doesn't affect read operations
                    // This is enforced during update operations
                    break;
                    
                case BmFieldAccessType.Visible:
                    // Full access, no changes needed
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Mask a value based on mask type.
    /// </summary>
    private static object MaskValue(object value, string? maskType)
    {
        var strValue = value.ToString() ?? "";
        
        return maskType?.ToLowerInvariant() switch
        {
            "email" => MaskEmail(strValue),
            "phone" => MaskPhone(strValue),
            "partial" => MaskPartial(strValue),
            "full" or null => new string('*', Math.Min(strValue.Length, 8)),
            _ => new string('*', Math.Min(strValue.Length, 8))
        };
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "****";
        
        var localPart = email[..atIndex];
        var domain = email[atIndex..];
        
        if (localPart.Length <= 2)
            return $"**{domain}";
            
        return $"{localPart[0]}***{localPart[^1]}{domain}";
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return "****";
        return $"***-***-{phone[^4..]}";
    }

    private static string MaskPartial(string value)
    {
        if (value.Length <= 4) return "****";
        return $"{value[..2]}***{value[^2..]}";
    }

    private static bool IsMoreRestrictive(BmFieldAccessType newType, BmFieldAccessType existingType)
    {
        // Hidden > Masked > Readonly > Visible
        return (int)newType > (int)existingType;
    }

    private static bool PrincipalMatchesUser(BmPrincipal principal, Models.UserContext user)
    {
        return principal.Type switch
        {
            BmPrincipalType.Authenticated => true,
            BmPrincipalType.Role => principal.Values.Any(role =>
                user.Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase))),
            BmPrincipalType.User => principal.Values.Any(userId =>
                userId.Equals(user.UserId.ToString(), StringComparison.OrdinalIgnoreCase) ||
                userId.Equals(user.Username, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    public HashSet<string> GetReadonlyFieldNames(
        BmEntity entity,
        Models.UserContext user,
        Dictionary<string, object?>? data = null)
    {
        var readonlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accessControls = ResolveFieldRestrictionChain(entity);

        if (accessControls.Count == 0)
            return readonlyFields;

        foreach (var acl in accessControls)
        {
            foreach (var rule in acl.Rules)
            {
                if (rule.RuleType != BmAccessRuleType.RestrictFields)
                    continue;

                if (rule.Principal != null && !PrincipalMatchesUser(rule.Principal, user))
                    continue;

                foreach (var fieldRestriction in rule.FieldRestrictions)
                {
                    if (fieldRestriction.AccessType == BmFieldAccessType.Readonly)
                    {
                        // Evaluate condition if present
                        if (fieldRestriction.ConditionExpr != null && data != null)
                        {
                            try
                            {
                                var evalContext = new EvaluationContext
                                {
                                    EntityData = data,
                                    User = new Expressions.UserContext
                                    {
                                        Id = user.UserId,
                                        Username = user.Username,
                                        Email = user.Email,
                                        TenantId = user.TenantId,
                                        Roles = user.Roles.ToList()
                                    },
                                    TenantId = user.TenantId
                                };
                                var conditionResult = _evaluator.Evaluate(fieldRestriction.ConditionExpr, evalContext);
                                if (!TypeConversionHelpers.ConvertToBool(conditionResult))
                                    continue;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error evaluating readonly condition for {Field}",
                                    fieldRestriction.FieldName);
                                continue;
                            }
                        }
                        else if (fieldRestriction.ConditionExpr != null)
                        {
                            // Has condition but no data to evaluate against — skip
                            continue;
                        }

                        readonlyFields.Add(fieldRestriction.FieldName);
                    }
                }
            }
        }

        return readonlyFields;
    }

    /// <summary>
    /// Resolve the full field restriction chain by following the entity's ExtendsFrom references.
    /// Collects ACLs from the entity and all ancestors.
    /// </summary>
    private List<BmAccessControl> ResolveFieldRestrictionChain(BmEntity entity)
    {
        var directAcls = _metaModelCache.GetAccessControlsForEntity(entity.QualifiedName);

        if (string.IsNullOrEmpty(entity.ExtendsFrom))
            return directAcls is List<BmAccessControl> list ? list : new List<BmAccessControl>(directAcls);

        var result = new List<BmAccessControl>(directAcls);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entity.QualifiedName };
        const int maxDepth = 10;

        var parentName = entity.ExtendsFrom;
        var depth = 0;

        while (!string.IsNullOrEmpty(parentName) && depth < maxDepth && visited.Add(parentName))
        {
            var parentAcls = _metaModelCache.GetAccessControlsForEntity(parentName);
            if (parentAcls.Count > 0)
                result.AddRange(parentAcls);

            // Follow the parent entity's ExtendsFrom
            var parentEntity = _metaModelCache.GetEntity(parentName);
            parentName = parentEntity?.ExtendsFrom;
            depth++;
        }

        if (depth >= maxDepth)
        {
            _logger.LogWarning("ExtendsFrom chain exceeded max depth ({MaxDepth}) for entity {Entity} in field restrictions",
                maxDepth, entity.QualifiedName);
        }

        return result;
    }

}
