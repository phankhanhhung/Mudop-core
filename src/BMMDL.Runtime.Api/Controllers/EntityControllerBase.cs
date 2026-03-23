using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Observability;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Base controller for entity operations with shared dependencies.
/// </summary>
[ApiController]
[Authorize]
[Route("api/odata/{module}/{entity}")]
public abstract class EntityControllerBase : ControllerBase
{
    protected readonly MetaModelCacheManager CacheManager;
    protected readonly IDynamicSqlBuilder SqlBuilder;
    protected readonly IQueryExecutor QueryExecutor;
    protected readonly IRuleEngine RuleEngine;
    protected readonly IEventPublisher EventPublisher;
    protected readonly IFieldRestrictionApplier FieldRestrictionApplier;
    protected readonly BmmdlMetrics Metrics;
    protected readonly ILogger Logger;
    protected readonly IPermissionChecker PermissionChecker;
    protected readonly IEntityResolver EntityResolver;

    // Access cache through manager asynchronously
    protected Task<MetaModelCache> GetCacheAsync() => CacheManager.GetCacheAsync();

    protected EntityControllerBase(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IRuleEngine ruleEngine,
        IEventPublisher eventPublisher,
        IFieldRestrictionApplier fieldRestrictionApplier,
        BmmdlMetrics metrics,
        ILogger logger,
        IPermissionChecker permissionChecker,
        IEntityResolver entityResolver)
    {
        CacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        SqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
        QueryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        RuleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        EventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        FieldRestrictionApplier = fieldRestrictionApplier ?? throw new ArgumentNullException(nameof(fieldRestrictionApplier));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        EntityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
    }

    /// <summary>
    /// Get entity definition from cache, throwing if not found.
    /// When services are defined in the model, verifies the entity is exposed by at least one service.
    /// Delegates to IEntityResolver for service boundary filtering.
    /// </summary>
    protected async Task<BmEntity> GetEntityDefinitionAsync(string module, string entity)
    {
        var entityDef = await EntityResolver.ResolveEntityAsync(module, entity);
        if (entityDef == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", Guid.Empty);
        }
        return entityDef;
    }

    /// <summary>
    /// Verify that an entity record exists by ID. Throws EntityNotFoundException if not found.
    /// </summary>
    protected async Task<Dictionary<string, object?>> VerifyEntityExistsAsync(
        BmEntity entityDef, string module, string entity, Guid id, CancellationToken ct)
    {
        var effectiveTenantId = GetEffectiveTenantId(entityDef);
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, parameters) = SqlBuilder.BuildSelectQuery(entityDef, options, id);
        var result = await QueryExecutor.ExecuteSingleAsync(sql, parameters, ct);

        if (result == null)
        {
            throw new EntityNotFoundException($"{module}.{entity}", id);
        }

        return result;
    }

    /// <summary>
    /// Get tenant ID from HTTP context.
    /// </summary>
    protected Guid? GetTenantId() => HttpContext.GetTenantId();

    /// <summary>
    /// Get effective tenant ID based on entity's tenant-scoped flag.
    /// </summary>
    protected Guid? GetEffectiveTenantId(BmEntity entityDef)
    {
        return entityDef.TenantScoped ? GetTenantId() : null;
    }

    /// <summary>
    /// Convert field name to snake_case for database column.
    /// </summary>
    protected static string ToSnakeCase(string name) => NamingConvention.ToSnakeCase(name);

    /// <summary>
    /// Create evaluation context for rule/expression evaluation.
    /// </summary>
    protected EvaluationContext CreateEvaluationContext(
        Guid? tenantId,
        Dictionary<string, object?> entityData,
        Dictionary<string, object?>? parameters = null)
    {
        var runtimeUserContext = HttpContext.GetUserContext();
        return new EvaluationContext
        {
            TenantId = tenantId,
            EntityData = entityData,
            Parameters = parameters ?? new Dictionary<string, object?>(),
            User = runtimeUserContext != null ? new UserContext
            {
                Id = runtimeUserContext.UserId,
                Username = runtimeUserContext.Username,
                Email = runtimeUserContext.Email,
                TenantId = runtimeUserContext.TenantId,
                Roles = runtimeUserContext.Roles.ToList()
            } : null
        };
    }

    /// <summary>
    /// Check access control permission for the given entity and operation.
    /// Returns null if allowed, or a 403 Forbidden result if denied.
    /// </summary>
    protected async Task<IActionResult?> CheckPermissionAsync(
        BmEntity entityDef,
        CrudOperation operation,
        Dictionary<string, object?>? data = null)
    {
        var userContext = HttpContext.GetUserContext();
        if (userContext == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(
                "AccessDenied",
                "No authenticated user context available."));
        }

        var tenantId = GetTenantId();
        var evalContext = new EvaluationContext
        {
            TenantId = tenantId,
            EntityData = data ?? new Dictionary<string, object?>(),
            User = new UserContext
            {
                Id = userContext.UserId,
                Username = userContext.Username,
                Email = userContext.Email,
                TenantId = userContext.TenantId,
                Roles = userContext.Roles.ToList()
            }
        };

        var decision = await PermissionChecker.CheckAccessAsync(
            entityDef, operation, userContext, data, evalContext);

        if (!decision.IsAllowed)
        {
            Logger.LogWarning(
                "Access denied: {Operation} on {Entity} for user {User}. Reason: {Reason}",
                operation, entityDef.QualifiedName, userContext.Username, decision.DeniedReason);

            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(
                "AccessDenied",
                decision.DeniedReason ?? $"Access denied for {operation} on {entityDef.Name}."));
        }

        return null; // Allowed
    }

    /// <summary>
    /// Build a RequestContext from the current HTTP context.
    /// </summary>
    protected RequestContext BuildRequestContext()
    {
        return new RequestContext(
            HttpContext.GetTenantId(),
            GetCurrentUserId(),
            GetCorrelationId(),
            GetRequestLocale(),
            HttpContext.GetUserContext()
        );
    }

    /// <summary>
    /// Get the primary language from the Accept-Language header.
    /// </summary>
    protected string? GetRequestLocale()
    {
        var acceptLanguage = Request.Headers.AcceptLanguage.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;

        var primaryLang = acceptLanguage.Split(',')[0].Trim();
        var semiColon = primaryLang.IndexOf(';');
        if (semiColon > 0) primaryLang = primaryLang[..semiColon].Trim();

        return string.IsNullOrEmpty(primaryLang) ? null : primaryLang;
    }

    /// <summary>
    /// Get the correlation ID from the X-Correlation-Id header or the trace identifier.
    /// </summary>
    protected string GetCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue))
            return headerValue.ToString();
        return HttpContext.TraceIdentifier;
    }

    /// <summary>
    /// Get the current user ID from the authenticated user context.
    /// </summary>
    protected Guid? GetCurrentUserId()
    {
        var userContext = HttpContext.GetUserContext();
        return userContext?.UserId;
    }

    /// <summary>
    /// Execute an async action within the Unit of Work, handling rollback on failure.
    /// Begins a transaction, executes the action, commits on success, and rolls back on exception.
    /// </summary>
    /// <param name="unitOfWork">The unit of work to manage the transaction.</param>
    /// <param name="action">The async action to execute within the transaction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    protected async Task<IActionResult> ExecuteInUnitOfWorkAsync(
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<IActionResult>> action,
        CancellationToken ct)
    {
        await unitOfWork.BeginAsync(ct);
        unitOfWork.CorrelationId = GetCorrelationId();
        try
        {
            var result = await action(ct);
            if (!unitOfWork.IsCompleted)
                await unitOfWork.CommitAsync(ct);
            return result;
        }
        catch (Exception) when (!unitOfWork.IsStarted)
        {
            throw;
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }
}
