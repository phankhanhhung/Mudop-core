using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Endpoints;

/// <summary>
/// Minimal API endpoint handlers for user management within a tenant.
/// Extracted from UserController for use with PlatformIdentityPlugin.MapEndpoints().
///
/// Each method is a static handler compatible with MapGet/MapPost/MapPut/MapDelete.
/// Dependencies are injected via parameter binding (DI-aware minimal APIs).
/// </summary>
public static class UserEndpoints
{
    private const string LogCategory = "BMMDL.Runtime.Api.Endpoints.UserEndpoints";

    /// <summary>
    /// List users in a tenant.
    /// </summary>
    public static async Task<IResult> ListUsers(
        Guid tenantId,
        IPlatformUserService userService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user has access to this tenant
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        var users = await userService.GetTenantUsersAsync(tenantId, ct);

        // Load roles and permissions for each user
        var usersWithRoles = new List<TenantUserResponse>();
        foreach (var user in users)
        {
            var userId = user.GetValueOrDefault("Id") as Guid? ?? Guid.Empty;
            var roles = await userService.GetUserRolesAsync(userId, ct);
            var permissions = await userService.GetUserDirectPermissionsAsync(userId, tenantId, ct);

            usersWithRoles.Add(new TenantUserResponse
            {
                Id = userId,
                Username = user.GetValueOrDefault("Username")?.ToString() ?? "",
                Email = user.GetValueOrDefault("Email")?.ToString() ?? "",
                FirstName = user.GetValueOrDefault("FirstName")?.ToString(),
                LastName = user.GetValueOrDefault("LastName")?.ToString(),
                IsActive = user.GetValueOrDefault("IsActive") as bool? ?? true,
                Roles = roles,
                Permissions = permissions
            });
        }

        return Results.Ok(usersWithRoles);
    }

    /// <summary>
    /// Create a new user for the tenant.
    /// </summary>
    public static async Task<IResult> CreateUser(
        Guid tenantId,
        [FromBody] CreateUserRequest request,
        IPlatformUserService userService,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        // Check if username/email already exists
        if (await userService.UsernameOrEmailExistsAsync(request.Username, request.Email, ct))
        {
            return Results.Conflict(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Conflict, "Username or email already exists"));
        }

        var userId = Guid.NewGuid();
        var passwordHash = passwordHasher.Hash(request.Password);

        var userData = new Dictionary<string, object?>
        {
            ["Id"] = userId,
            ["Username"] = request.Username,
            ["Email"] = request.Email,
            ["PasswordHash"] = passwordHash,
            ["FirstName"] = request.FirstName,
            ["LastName"] = request.LastName,
            ["TenantId"] = tenantId
        };

        var createdUser = await userService.CreateUserAsync(userData, ct);
        if (createdUser == null)
        {
            return Results.Problem(
                detail: "Failed to create user",
                statusCode: 500);
        }

        logger.LogInformation("User created: {Username} for tenant {TenantId} by {CreatedBy}",
            request.Username, tenantId, userContext.UserId);

        return Results.Created($"/api/tenants/{tenantId}/users/{userId}", new TenantUserResponse
        {
            Id = userId,
            Username = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,
            Roles = new List<string>(),
            Permissions = new List<string>()
        });
    }

    /// <summary>
    /// Get user details.
    /// </summary>
    public static async Task<IResult> GetUser(
        Guid tenantId,
        Guid userId,
        IPlatformUserService userService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Allow if TenantAdmin or requesting own info
        if (userContext.UserId != userId && effectiveTenantId != tenantId &&
            !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        var user = await userService.GetUserByIdAsync(userId, ct);
        if (user == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found"));
        }

        // Verify user belongs to the requested tenant
        var userTenantId = user.GetValueOrDefault("TenantId") as Guid?;
        if (userTenantId != tenantId && !userContext.HasRole("SystemAdmin"))
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found in this tenant"));
        }

        var roles = await userService.GetUserRolesAsync(userId, ct);
        var permissions = await userService.GetUserDirectPermissionsAsync(userId, tenantId, ct);

        return Results.Ok(new TenantUserResponse
        {
            Id = userId,
            Username = user.GetValueOrDefault("Username")?.ToString() ?? "",
            Email = user.GetValueOrDefault("Email")?.ToString() ?? "",
            FirstName = user.GetValueOrDefault("FirstName")?.ToString(),
            LastName = user.GetValueOrDefault("LastName")?.ToString(),
            IsActive = user.GetValueOrDefault("IsActive") as bool? ?? true,
            Roles = roles,
            Permissions = permissions
        });
    }

    /// <summary>
    /// Update user details.
    /// </summary>
    public static async Task<IResult> UpdateUser(
        Guid tenantId,
        Guid userId,
        [FromBody] UpdateUserRequest request,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Allow if TenantAdmin or updating own profile
        if (userContext.UserId != userId && effectiveTenantId != tenantId &&
            !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        var updates = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(request.Username))
            updates["Username"] = request.Username;
        if (!string.IsNullOrEmpty(request.Email))
            updates["Email"] = request.Email;
        if (!string.IsNullOrEmpty(request.FirstName))
            updates["FirstName"] = request.FirstName;
        if (!string.IsNullOrEmpty(request.LastName))
            updates["LastName"] = request.LastName;
        if (request.IsActive.HasValue)
            updates["IsActive"] = request.IsActive.Value;

        var updatedUser = await userService.UpdateUserAsync(userId, updates, ct);
        if (updatedUser == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found"));
        }

        logger.LogInformation("User updated: {UserId} in tenant {TenantId} by {UpdatedBy}",
            userId, tenantId, userContext.UserId);

        var roles = await userService.GetUserRolesAsync(userId, ct);
        var permissions = await userService.GetUserDirectPermissionsAsync(userId, tenantId, ct);

        return Results.Ok(new TenantUserResponse
        {
            Id = userId,
            Username = updatedUser.GetValueOrDefault("Username")?.ToString() ?? "",
            Email = updatedUser.GetValueOrDefault("Email")?.ToString() ?? "",
            FirstName = updatedUser.GetValueOrDefault("FirstName")?.ToString(),
            LastName = updatedUser.GetValueOrDefault("LastName")?.ToString(),
            IsActive = updatedUser.GetValueOrDefault("IsActive") as bool? ?? true,
            Roles = roles,
            Permissions = permissions
        });
    }

    /// <summary>
    /// Delete (deactivate) a user from the tenant.
    /// </summary>
    public static async Task<IResult> DeleteUser(
        Guid tenantId,
        Guid userId,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        // Prevent deleting self
        if (userContext.UserId == userId)
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Cannot delete your own account"));
        }

        var success = await userService.DeactivateUserAsync(userId, ct);
        if (!success)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found"));
        }

        logger.LogInformation("User deactivated: {UserId} from tenant {TenantId} by {DeletedBy}",
            userId, tenantId, userContext.UserId);

        return Results.NoContent();
    }

    /// <summary>
    /// Assign a role to a user.
    /// </summary>
    public static async Task<IResult> AssignRole(
        Guid tenantId,
        Guid userId,
        [FromBody] AssignRoleRequest request,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        try
        {
            await userService.AssignRoleAsync(userId, request.RoleName, userContext.UserId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, ex.Message));
        }

        logger.LogInformation("Role {Role} assigned to user {UserId} in tenant {TenantId} by {AssignedBy}",
            request.RoleName, userId, tenantId, userContext.UserId);

        return Results.Ok(new { message = $"Role '{request.RoleName}' assigned to user" });
    }

    /// <summary>
    /// Remove a role from a user.
    /// </summary>
    public static async Task<IResult> RemoveRole(
        Guid tenantId,
        Guid userId,
        string roleName,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        await userService.RemoveRoleAsync(userId, roleName, ct);

        logger.LogInformation("Role {Role} removed from user {UserId} in tenant {TenantId} by {RemovedBy}",
            roleName, userId, tenantId, userContext.UserId);

        return Results.NoContent();
    }

    /// <summary>
    /// Assign a permission to a user.
    /// </summary>
    public static async Task<IResult> AssignPermission(
        Guid tenantId,
        Guid userId,
        [FromBody] AssignPermissionRequest request,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        try
        {
            await userService.AssignPermissionAsync(userId, request.PermissionName, tenantId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, ex.Message));
        }

        logger.LogInformation("Permission {Permission} assigned to user {UserId} in tenant {TenantId} by {AssignedBy}",
            request.PermissionName, userId, tenantId, userContext.UserId);

        return Results.Ok(new { message = $"Permission '{request.PermissionName}' assigned to user" });
    }

    /// <summary>
    /// Remove a permission from a user.
    /// </summary>
    public static async Task<IResult> RemovePermission(
        Guid tenantId,
        Guid userId,
        string permissionName,
        IPlatformUserService userService,
        IJwtService jwtService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        // Check if user is TenantAdmin
        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        await userService.RemovePermissionAsync(userId, permissionName, tenantId, ct);

        logger.LogInformation("Permission {Permission} removed from user {UserId} in tenant {TenantId} by {RemovedBy}",
            permissionName, userId, tenantId, userContext.UserId);

        return Results.NoContent();
    }

    /// <summary>
    /// List available system permissions for the tenant.
    /// </summary>
    public static async Task<IResult> ListPermissions(
        Guid tenantId,
        IPlatformUserService userService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g ? g : userContext.TenantId;

        if (effectiveTenantId != tenantId && !userContext.HasRole("SystemAdmin") && !userContext.HasRole("TenantAdmin"))
        {
            return Results.Forbid();
        }

        var permissions = await userService.ListSystemPermissionsAsync(tenantId, ct);
        return Results.Ok(permissions);
    }
}
