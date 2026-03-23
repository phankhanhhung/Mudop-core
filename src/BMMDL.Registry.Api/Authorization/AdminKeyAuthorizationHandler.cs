using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;

namespace BMMDL.Registry.Api.Authorization;

/// <summary>
/// Requirement for admin key authorization.
/// </summary>
public class AdminKeyRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Authorization handler that validates the X-Admin-Key header against configured admin key.
/// </summary>
public class AdminKeyAuthorizationHandler : AuthorizationHandler<AdminKeyRequirement>
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AdminKeyAuthorizationHandler> _logger;

    public AdminKeyAuthorizationHandler(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AdminKeyAuthorizationHandler> logger)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminKeyRequirement requirement)
    {
        // Check if admin key auth is disabled via config
        var adminEnabled = _configuration.GetValue("Admin:Enabled", true);
        if (!adminEnabled)
        {
            _logger.LogWarning("Admin key authorization is DISABLED via Admin:Enabled=false. All admin endpoints are open.");
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("No HTTP context available for admin key authorization");
            context.Fail();
            return Task.CompletedTask;
        }

        // Get the admin key from header
        var providedKey = httpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
        {
            _logger.LogWarning("Admin key authorization failed: No X-Admin-Key header provided");
            context.Fail();
            return Task.CompletedTask;
        }

        // Get the configured admin key
        var configuredKey = _configuration["Admin:ApiKey"];
        if (string.IsNullOrEmpty(configuredKey))
        {
            _logger.LogError("Admin:ApiKey is not configured in appsettings");
            context.Fail();
            return Task.CompletedTask;
        }

        // Constant-time comparison to prevent timing attacks
        if (CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedKey),
            System.Text.Encoding.UTF8.GetBytes(configuredKey)))
        {
            context.Succeed(requirement);
            _logger.LogInformation("Admin key authorization succeeded");
        }
        else
        {
            _logger.LogWarning("Admin key authorization failed: Invalid key provided");
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
