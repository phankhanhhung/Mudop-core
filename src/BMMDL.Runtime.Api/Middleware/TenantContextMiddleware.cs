namespace BMMDL.Runtime.Api.Middleware;

using BMMDL.Runtime.Models;
using BMMDL.Runtime.Services;

/// <summary>
/// Middleware to extract and validate tenant context from the request.
/// Supports hybrid resolution: Header → Query → JWT claim (default).
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    /// <summary>
    /// Key for storing tenant ID in HttpContext.Items.
    /// </summary>
    public const string TenantIdKey = "TenantId";

    /// <summary>
    /// Key for storing module name in HttpContext.Items.
    /// </summary>
    public const string ModuleKey = "Module";

    /// <summary>
    /// Key for storing entity name in HttpContext.Items.
    /// </summary>
    public const string EntityKey = "Entity";

    /// <summary>
    /// Header name for explicit tenant override.
    /// </summary>
    public const string TenantIdHeader = "X-Tenant-Id";

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IJwtService jwtService)
    {
        // Process OData API routes: /api/odata/{module}/{entity}
        if (context.Request.Path.StartsWithSegments("/api/odata", out var remaining))
        {
            var segments = remaining.Value?.Trim('/').Split('/') ?? [];
            
            if (segments.Length >= 2)
            {
                context.Items[ModuleKey] = segments[0];
                context.Items[EntityKey] = segments[1];
                
                _logger.LogDebug(
                    "OData route parsed. Module: {Module}, Entity: {Entity}",
                    segments[0], segments[1]);
            }
        }
        // Legacy route support: /api/v1/{tenantId}/{module}/{entity}
        else if (context.Request.Path.StartsWithSegments("/api/v1", out var legacyRemaining))
        {
            var segments = legacyRemaining.Value?.Trim('/').Split('/') ?? [];

            if (segments.Length >= 3)
            {
                if (Guid.TryParse(segments[0], out var tenantId))
                {
                    _logger.LogWarning(
                        "Legacy /api/v1 route accessed by {User} for tenant {TenantId}. This route is deprecated; use /api/odata instead.",
                        context.User?.Identity?.Name, tenantId);

                    // Validate tenant access — same rules as modern routes
                    UserContext? userContext = null;
                    if (context.User?.Identity?.IsAuthenticated == true)
                    {
                        userContext = jwtService.GetUserContext(context.User);
                    }

                    if (userContext == null)
                    {
                        _logger.LogWarning(
                            "Legacy route tenant override denied: no authenticated user. Requested tenant was {RequestedTenant}.",
                            tenantId);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync("Tenant access denied: authentication required.");
                        return;
                    }

                    if (!userContext.CanAccessTenant(tenantId))
                    {
                        _logger.LogWarning(
                            "Legacy route tenant override denied: user {UserId} requested tenant {RequestedTenant} but JWT tenant is {JwtTenant}.",
                            userContext.UserId, tenantId, userContext.TenantId);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync("Tenant access denied: you do not have access to the requested tenant.");
                        return;
                    }

                    context.Items[TenantIdKey] = tenantId;
                    context.Items[ModuleKey] = segments[1];
                    context.Items[EntityKey] = segments[2];

                    _logger.LogDebug(
                        "Legacy tenant context set. TenantId: {TenantId}, Module: {Module}, Entity: {Entity}",
                        tenantId, segments[1], segments[2]);

                    await _next(context);
                    return;
                }
                else
                {
                    _logger.LogWarning("Invalid tenant ID format in legacy route: {TenantId}", segments[0]);
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid tenant ID format" });
                    return;
                }
            }
        }

        // Hybrid tenant resolution for new routes
        var resolvedTenantId = ResolveTenantId(context, jwtService);
        if (resolvedTenantId.HasValue)
        {
            context.Items[TenantIdKey] = resolvedTenantId.Value;
            _logger.LogDebug("Tenant resolved: {TenantId}", resolvedTenantId.Value);
        }

        // Diagnostic header for testing
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Resolved-Tenant"] = resolvedTenantId?.ToString() ?? "null";
            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Resolve tenant ID using hybrid approach:
    /// 1. X-Tenant-Id header (explicit override) — validated against JWT
    /// 2. tenantId query parameter (explicit override) — validated against JWT
    /// 3. JWT tenant_id claim (default)
    /// </summary>
    private Guid? ResolveTenantId(HttpContext context, IJwtService jwtService)
    {
        // Extract JWT user context once for validation
        UserContext? userContext = null;
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            userContext = jwtService.GetUserContext(context.User);
        }

        // 1. Header override — validate user has access to the requested tenant
        if (context.Request.Headers.TryGetValue(TenantIdHeader, out var headerValue)
            && Guid.TryParse(headerValue.FirstOrDefault(), out var headerTenant))
        {
            // If no authenticated user, ignore the header entirely to prevent
            // unauthenticated requests from selecting an arbitrary tenant.
            if (userContext == null)
            {
                _logger.LogWarning(
                    "Tenant header override ignored: no authenticated user. Header tenant was {RequestedTenant}.",
                    headerTenant);
            }
            else if (!userContext.CanAccessTenant(headerTenant))
            {
                _logger.LogWarning(
                    "Tenant header override denied: user {UserId} requested tenant {RequestedTenant} but JWT tenant is {JwtTenant}. Using JWT tenant.",
                    userContext.UserId, headerTenant, userContext.TenantId);
                return userContext.TenantId;
            }
            else
            {
                _logger.LogDebug("Tenant from header: {TenantId}", headerTenant);
                return headerTenant;
            }
        }

        // 2. Query param override — validate user has access to the requested tenant
        if (context.Request.Query.TryGetValue("tenantId", out var queryValue)
            && Guid.TryParse(queryValue.FirstOrDefault(), out var queryTenant))
        {
            // If no authenticated user, ignore the query param entirely.
            if (userContext == null)
            {
                _logger.LogWarning(
                    "Tenant query override ignored: no authenticated user. Query tenant was {RequestedTenant}.",
                    queryTenant);
            }
            else if (!userContext.CanAccessTenant(queryTenant))
            {
                _logger.LogWarning(
                    "Tenant query override denied: user {UserId} requested tenant {RequestedTenant} but JWT tenant is {JwtTenant}. Using JWT tenant.",
                    userContext.UserId, queryTenant, userContext.TenantId);
                return userContext.TenantId;
            }
            else
            {
                _logger.LogDebug("Tenant from query: {TenantId}", queryTenant);
                return queryTenant;
            }
        }

        // 3. JWT claim default (from authenticated user)
        if (userContext != null)
        {
            _logger.LogDebug("Tenant from JWT: {TenantId}", userContext.TenantId);
            return userContext.TenantId;
        }

        return null;
    }
}

/// <summary>
/// Extension methods for HttpContext to access tenant context.
/// </summary>
public static class TenantContextExtensions
{
    /// <summary>
    /// Get the current tenant ID from the request context.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <returns>Tenant ID if present, null otherwise.</returns>
    public static Guid? GetTenantId(this HttpContext context)
    {
        if (context.Items.TryGetValue(TenantContextMiddleware.TenantIdKey, out var value) && value is Guid tenantId)
        {
            return tenantId;
        }
        return null;
    }

    /// <summary>
    /// Get the current tenant ID or throw if not present.
    /// </summary>
    public static Guid GetRequiredTenantId(this HttpContext context)
    {
        return context.GetTenantId() 
            ?? throw new InvalidOperationException("Tenant ID not found in request context");
    }

    /// <summary>
    /// Get the current module name from the request context.
    /// </summary>
    public static string? GetModule(this HttpContext context)
    {
        return context.Items.TryGetValue(TenantContextMiddleware.ModuleKey, out var value) 
            ? value as string 
            : null;
    }

    /// <summary>
    /// Get the current entity name from the request context.
    /// </summary>
    public static string? GetEntity(this HttpContext context)
    {
        return context.Items.TryGetValue(TenantContextMiddleware.EntityKey, out var value) 
            ? value as string 
            : null;
    }
}

/// <summary>
/// Extension methods for registering tenant context middleware.
/// </summary>
public static class TenantContextMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
