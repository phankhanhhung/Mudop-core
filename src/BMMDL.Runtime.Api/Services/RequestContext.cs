namespace BMMDL.Runtime.Api.Services;

using BMMDL.Runtime.Expressions;
using RuntimeModelsUserContext = BMMDL.Runtime.Models.UserContext;

/// <summary>
/// Lightweight DTO for passing HTTP context information to service methods.
/// Avoids coupling services to HttpContext directly.
/// </summary>
public record RequestContext(
    Guid? TenantId,
    Guid? UserId,
    string CorrelationId,
    string? Locale,
    RuntimeModelsUserContext? UserContext
)
{
    /// <summary>
    /// Create an EvaluationContext from this request context for rule/expression evaluation.
    /// </summary>
    public EvaluationContext ToEvaluationContext()
    {
        return new EvaluationContext
        {
            TenantId = TenantId,
            User = UserContext != null ? new UserContext
            {
                Id = UserContext.UserId,
                Username = UserContext.Username,
                Email = UserContext.Email,
                TenantId = UserContext.TenantId,
                Roles = UserContext.Roles.ToList()
            } : null
        };
    }

    /// <summary>
    /// Get effective tenant ID based on entity's tenant-scoped flag.
    /// </summary>
    public Guid? GetEffectiveTenantId(BMMDL.MetaModel.Structure.BmEntity entityDef)
        => entityDef.TenantScoped ? TenantId : null;
}
