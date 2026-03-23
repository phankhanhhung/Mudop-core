namespace BMMDL.Runtime.Api.Middleware;

using BMMDL.MetaModel;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Services;

/// <summary>
/// Middleware to check authorization for the requested resource.
/// Phase 4: Enforces JWT authentication for OData routes.
/// </summary>
public class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationMiddleware> _logger;

    public AuthorizationMiddleware(RequestDelegate next, ILogger<AuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IJwtService jwtService, IMetaModelCache metaModelCache, IQueryExecutor queryExecutor)
    {
        // Skip auth for non-OData routes (auth, health, openapi)
        if (!context.Request.Path.StartsWithSegments("/api/v1") && 
            !context.Request.Path.StartsWithSegments("/api/odata"))
        {
            await _next(context);
            return;
        }

        // Skip health check
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // OData routes require authentication
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("Unauthorized access attempt to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Authentication required"));
            return;
        }

        // Get user context from JWT
        var userContext = jwtService.GetUserContext(context.User);
        if (userContext == null)
        {
            _logger.LogWarning("Invalid user context for authenticated request to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid authentication token"));
            return;
        }

        // Get context info from TenantContextMiddleware
        var tenantId = context.GetTenantId();
        var module = context.GetModule();
        var entity = context.GetEntity();

        // Update UserContext with tenant from request (not from token)
        if (tenantId.HasValue)
        {
            userContext = userContext with { TenantId = tenantId.Value };
        }

        // Validate tenant access for ALL OData routes when a tenant is present.
        // Previously this only ran for entity-specific routes (module+entity),
        // leaving root metadata queries (e.g. $metadata, service document) unvalidated.
        // NOTE: UserContext is stored AFTER tenant validation to prevent downstream
        // controllers from accessing a context with an unvalidated tenant.
        if (tenantId.HasValue)
        {
            bool needsTenantValidation;

            if (module != null && entity != null)
            {
                // Entity-specific route: validate only for tenant-scoped entities
                var qualifiedName = $"{module}.{entity}";
                var entityDef = metaModelCache.GetEntity(qualifiedName);

                // Check both annotation AND field presence
                var hasTenantIdField = entityDef?.Fields?.Any(f =>
                    f.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase)) ?? false;
                var isTenantScoped = entityDef?.TenantScoped ?? hasTenantIdField;
                needsTenantValidation = isTenantScoped;

                if (!needsTenantValidation)
                {
                    _logger.LogDebug(
                        "[TENANT_VALIDATION] SKIPPED - Entity is not tenant-scoped for {Module}.{Entity}",
                        module, entity);
                }
            }
            else
            {
                // Root OData route (e.g. $metadata, service document, $batch):
                // Always validate tenant access to prevent cross-tenant metadata leaks
                needsTenantValidation = true;
            }

            if (needsTenantValidation)
            {
                var routeDesc = module != null && entity != null
                    ? $"{module}.{entity}"
                    : context.Request.Path.Value ?? "unknown";

                _logger.LogInformation(
                    "[TENANT_VALIDATION] Checking tenant access for {Route}",
                    routeDesc);

                try
                {
                    // Query core.user to check if user has access to this tenant
                    var sql = "SELECT COUNT(*) FROM core.user WHERE identity_id = @identityId AND tenant_id = @tenantId AND status = 'active'";
                    var parameters = new[]
                    {
                        new Npgsql.NpgsqlParameter("identityId", userContext.UserId),
                        new Npgsql.NpgsqlParameter("tenantId", tenantId.Value)
                    };

                    var count = await queryExecutor.ExecuteScalarAsync<long>(sql, parameters);

                    if (count == 0)
                    {
                        _logger.LogWarning(
                            "[TENANT_VALIDATION] DENIED - Access denied for {Route}",
                            routeDesc);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied, "Access to this tenant is not allowed"));
                        return;
                    }

                    _logger.LogDebug(
                        "[TENANT_VALIDATION] ALLOWED - Access granted for {Route}",
                        routeDesc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[TENANT_VALIDATION] ERROR during validation for {Route}",
                        routeDesc);
                    // Re-throw to fail fast - don't allow access on error
                    throw;
                }
            }
        }

        // Store UserContext only after tenant validation succeeds, so downstream
        // controllers (including $batch) never see an unvalidated tenant context.
        context.Items["UserContext"] = userContext;

        _logger.LogDebug(
            "Authorization passed for {Path} by user {Username} in tenant {TenantId}",
            context.Request.Path, userContext.Username, tenantId);

        await _next(context);
    }
}

/// <summary>
/// Extension methods for HttpContext to access user context.
/// </summary>
public static class UserContextExtensions
{
    /// <summary>
    /// Get the authenticated user context from the request.
    /// </summary>
    public static BMMDL.Runtime.Models.UserContext? GetUserContext(this HttpContext context)
    {
        return context.Items.TryGetValue("UserContext", out var value)
            ? value as BMMDL.Runtime.Models.UserContext
            : null;
    }
}

/// <summary>
/// Extension methods for registering authorization middleware.
/// </summary>
public static class AuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthorizationMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuthorizationMiddleware>();
    }
}

