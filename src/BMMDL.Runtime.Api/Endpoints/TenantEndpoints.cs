using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Endpoints;

/// <summary>
/// Minimal API endpoint handlers for tenant operations.
/// Extracted from TenantController for use with MultiTenancyPlugin.MapEndpoints().
///
/// Each method is a static handler compatible with MapGet/MapPost/MapPut.
/// Dependencies are injected via parameter binding (DI-aware minimal APIs).
/// </summary>
public static class TenantEndpoints
{
    /// <summary>
    /// List tenants the current user has access to.
    /// </summary>
    public static async Task<IResult> ListTenants(
        IPlatformTenantService tenantService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        var tenants = await tenantService.GetUserTenantsAsync(userContext.UserId, ct);

        var response = tenants.Select(t => new TenantResponse
        {
            Id = t.GetValueOrDefault("Id") as Guid? ?? Guid.Empty,
            Code = t.GetValueOrDefault("Code")?.ToString() ?? "",
            Name = t.GetValueOrDefault("Name")?.ToString() ?? "",
            IsActive = t.GetValueOrDefault("IsActive") as bool? ?? true,
            SubscriptionTier = t.GetValueOrDefault("SubscriptionTier")?.ToString() ?? "free",
            OwnerId = userContext.UserId
        });

        return Results.Ok(response);
    }

    /// <summary>
    /// Create a new tenant. The current user becomes the tenant owner/admin.
    /// </summary>
    public static async Task<IResult> CreateTenant(
        IPlatformTenantService tenantService,
        IPlatformUserService userService,
        IJwtService jwtService,
        HttpContext httpContext,
        [FromBody] CreateTenantRequest request,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Validate input
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 50)
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Code is required and must be 50 characters or less"));

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Name is required and must be 200 characters or less"));

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.Code, @"^[a-zA-Z0-9_-]+$"))
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Code must contain only alphanumeric characters, hyphens, and underscores"));

        // Check if tenant code already exists
        if (await tenantService.TenantCodeExistsAsync(request.Code, ct))
        {
            return Results.Conflict(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Conflict, "Tenant code already exists"));
        }

        var tenantId = Guid.NewGuid();

        // Create tenant in database
        var tenantData = new Dictionary<string, object?>
        {
            ["Id"] = tenantId,
            ["Code"] = request.Code,
            ["Name"] = request.Name,
            ["Description"] = request.Description,
            ["OwnerIdentityId"] = userContext.UserId,
            ["SubscriptionTier"] = "free",
            ["MaxUsers"] = 10,
            ["IsActive"] = true
        };

        var createdTenant = await tenantService.CreateTenantAsync(tenantData, ct);
        if (createdTenant == null)
        {
            return Results.Problem(
                detail: "Failed to create tenant",
                statusCode: 500);
        }

        // Create core.user record to link this identity to the new tenant
        await userService.CreateTenantUserAsync(userContext.UserId, tenantId, "Admin", ct);

        // Create TenantAdmin role for this tenant
        await userService.CreateRoleAsync(
            "TenantAdmin",
            $"Administrator for tenant {request.Code}",
            tenantId,
            isSystemRole: false,
            ct);

        // Assign TenantAdmin role to user
        await userService.AssignRoleAsync(userContext.UserId, "TenantAdmin", userContext.UserId, ct);

        return Results.Created($"/api/tenants/{tenantId}", new TenantResponse
        {
            Id = tenantId,
            Code = request.Code,
            Name = request.Name,
            IsActive = true,
            SubscriptionTier = "free",
            OwnerId = userContext.UserId
        });
    }

    /// <summary>
    /// Get tenant details. Only accessible by tenant members.
    /// </summary>
    public static async Task<IResult> GetTenant(
        Guid id,
        IPlatformTenantService tenantService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware)
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "Tenant not found"));
        }

        // Check if user has access (belongs to tenant or is system admin)
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        return Results.Ok(new TenantResponse
        {
            Id = id,
            Code = tenant.GetValueOrDefault("Code")?.ToString() ?? "",
            Name = tenant.GetValueOrDefault("Name")?.ToString() ?? "",
            IsActive = tenant.GetValueOrDefault("IsActive") as bool? ?? true,
            SubscriptionTier = tenant.GetValueOrDefault("SubscriptionTier")?.ToString() ?? "free",
            OwnerId = tenant.GetValueOrDefault("OwnerIdentityId") as Guid? ?? Guid.Empty
        });
    }

    /// <summary>
    /// Update tenant settings. Only accessible by tenant admin.
    /// </summary>
    public static async Task<IResult> UpdateTenant(
        Guid id,
        IPlatformTenantService tenantService,
        IJwtService jwtService,
        HttpContext httpContext,
        [FromBody] UpdateTenantRequest request,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        // Check if user has admin access to this tenant
        // Cross-tenant access requires SystemAdmin (TenantAdmin is only valid for own tenant)
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }
        if (!userContext.HasRole("TenantAdmin") && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Validate updated fields
        if (!string.IsNullOrEmpty(request.Code))
        {
            if (request.Code.Length > 50)
                return Results.BadRequest(
                    ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Code must be 50 characters or less"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Code, @"^[a-zA-Z0-9_-]+$"))
                return Results.BadRequest(
                    ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Code must contain only alphanumeric characters, hyphens, and underscores"));
            if (await tenantService.TenantCodeExistsAsync(request.Code, ct))
                return Results.Conflict(
                    ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Conflict, "Tenant code already exists"));
        }
        if (!string.IsNullOrEmpty(request.Name) && request.Name.Length > 200)
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Name must be 200 characters or less"));

        // Build update data
        var updates = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(request.Name))
            updates["Name"] = request.Name;
        if (!string.IsNullOrEmpty(request.Code))
            updates["Code"] = request.Code;
        if (request.IsActive.HasValue)
            updates["IsActive"] = request.IsActive.Value;
        if (!string.IsNullOrEmpty(request.Description))
            updates["Description"] = request.Description;

        var updatedTenant = await tenantService.UpdateTenantAsync(id, updates, ct);
        if (updatedTenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "Tenant not found"));
        }

        return Results.Ok(new TenantResponse
        {
            Id = id,
            Code = updatedTenant.GetValueOrDefault("Code")?.ToString() ?? "",
            Name = updatedTenant.GetValueOrDefault("Name")?.ToString() ?? "",
            IsActive = updatedTenant.GetValueOrDefault("IsActive") as bool? ?? true,
            SubscriptionTier = updatedTenant.GetValueOrDefault("SubscriptionTier")?.ToString() ?? "free",
            OwnerId = updatedTenant.GetValueOrDefault("OwnerIdentityId") as Guid? ?? Guid.Empty
        });
    }

    /// <summary>
    /// Switch the current user's active tenant.
    /// </summary>
    public static async Task<IResult> SwitchTenant(
        Guid id,
        IPlatformTenantService tenantService,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Verify tenant exists and user has access
        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "Tenant not found"));
        }

        // Check user has access to this tenant
        if (!userContext.CanAccessTenant(id))
        {
            return Results.Forbid();
        }

        // Return the tenant info — the frontend will update X-Tenant-Id header
        return Results.Ok(new TenantResponse
        {
            Id = id,
            Code = tenant.GetValueOrDefault("Code")?.ToString() ?? "",
            Name = tenant.GetValueOrDefault("Name")?.ToString() ?? "",
            IsActive = tenant.GetValueOrDefault("IsActive") as bool? ?? true,
            SubscriptionTier = tenant.GetValueOrDefault("SubscriptionTier")?.ToString() ?? "free",
            OwnerId = tenant.GetValueOrDefault("OwnerIdentityId") as Guid? ?? Guid.Empty
        });
    }

    /// <summary>
    /// Invite a user to the tenant.
    /// </summary>
    public static async Task<IResult> InviteUser(
        Guid id,
        IPlatformTenantService tenantService,
        IPlatformUserService userService,
        IJwtService jwtService,
        HttpContext httpContext,
        [FromBody] InviteUserRequest request,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        // Check admin access — cross-tenant requires SystemAdmin
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }
        if (!userContext.HasRole("TenantAdmin") && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Verify tenant exists
        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "Tenant not found"));
        }

        // Look up the user by email
        var invitedUser = await userService.GetUserByUsernameOrEmailAsync(request.Email, ct);
        if (invitedUser == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found with the provided email"));
        }

        var invitedUserId = invitedUser.GetValueOrDefault("Id") as Guid? ?? Guid.Empty;

        // Create tenant user link
        await userService.CreateTenantUserAsync(invitedUserId, id, request.DisplayName ?? "Member", ct);

        // Assign role if specified
        if (!string.IsNullOrEmpty(request.Role))
        {
            await userService.AssignRoleAsync(invitedUserId, request.Role, userContext.UserId, ct);
        }

        return Results.Ok(new { UserId = invitedUserId, TenantId = id, Role = request.Role ?? "Member" });
    }

    /// <summary>
    /// List installed modules for a tenant.
    /// Accessible by TenantAdmin, SystemAdmin, or tenant members.
    /// </summary>
    public static async Task<IResult> GetInstalledModules(
        Guid id,
        IPlatformTenantService tenantService,
        IRegistryClient registryClient,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        // Check if user has access (belongs to tenant or is system admin)
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Verify tenant exists
        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantNotFound, "Tenant not found"));
        }

        var result = await registryClient.GetInstalledModulesAsync(id, ct);

        if (!result.Success)
        {
            return Results.Problem(
                detail: string.Join("; ", result.Errors),
                statusCode: 502,
                title: "Failed to retrieve modules from Registry");
        }

        var modules = result.Modules.Select(m => new ModuleInfo
        {
            Name = m.Name,
            Version = m.Version,
            Author = m.Author,
            EntityCount = m.EntityCount,
            ServiceCount = m.ServiceCount,
            InstalledAt = m.InstalledAt,
            SchemaInitialized = m.SchemaInitialized,
            SchemaName = m.SchemaName
        }).ToList();

        return Results.Ok(modules);
    }

    /// <summary>
    /// Install a module for a tenant. Requires TenantAdmin or SystemAdmin role.
    /// </summary>
    public static async Task<IResult> InstallModule(
        Guid id,
        IPlatformTenantService tenantService,
        IRegistryClient registryClient,
        IJwtService jwtService,
        HttpContext httpContext,
        [FromBody] InstallModuleRequest request,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        // Check admin access — only TenantAdmin or SystemAdmin can install modules
        if (!userContext.HasRole("TenantAdmin") && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Cross-tenant access requires SystemAdmin
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.ModuleName))
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "ModuleName is required"));
        }

        // Verify tenant exists
        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantNotFound, "Tenant not found"));
        }

        // Verify tenant is active
        var isActive = tenant.GetValueOrDefault("IsActive") as bool? ?? false;
        if (!isActive)
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.ValidationError, "Cannot install modules on an inactive tenant"));
        }

        var result = await registryClient.InstallModuleForTenantAsync(
            id, request.ModuleName, userContext.Username, ct);

        if (!result.Success)
        {
            return Results.UnprocessableEntity(new ModuleInstallResponse
            {
                Success = false,
                ModuleName = result.ModuleName,
                EntityCount = result.EntityCount,
                ServiceCount = result.ServiceCount,
                Errors = result.Errors,
                Warnings = result.Warnings,
                SchemaResult = result.SchemaResult
            });
        }

        return Results.Ok(new ModuleInstallResponse
        {
            Success = true,
            ModuleName = result.ModuleName,
            EntityCount = result.EntityCount,
            ServiceCount = result.ServiceCount,
            Errors = result.Errors,
            Warnings = result.Warnings,
            SchemaResult = result.SchemaResult
        });
    }

    /// <summary>
    /// Uninstall a module from a tenant. Requires TenantAdmin or SystemAdmin role.
    /// </summary>
    public static async Task<IResult> UninstallModule(
        Guid id,
        string moduleName,
        IPlatformTenantService tenantService,
        IRegistryClient registryClient,
        IJwtService jwtService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userContext = jwtService.GetUserContext(httpContext.User);
        if (userContext == null)
            return Results.Unauthorized();

        // Resolve effective tenant from X-Tenant-Id header
        var effectiveTenantId = httpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;

        // Check admin access — only TenantAdmin or SystemAdmin can uninstall modules
        if (!userContext.HasRole("TenantAdmin") && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Cross-tenant access requires SystemAdmin
        if (effectiveTenantId != id && !userContext.HasRole("SystemAdmin"))
        {
            return Results.Forbid();
        }

        // Validate module name
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return Results.BadRequest(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidRequest, "Module name is required"));
        }

        // Verify tenant exists
        var tenant = await tenantService.GetTenantByIdAsync(id, ct);
        if (tenant == null)
        {
            return Results.NotFound(
                ODataErrorResponse.FromException(ODataConstants.ErrorCodes.TenantNotFound, "Tenant not found"));
        }

        var result = await registryClient.UninstallModuleForTenantAsync(
            id, moduleName, userContext.Username, ct);

        if (!result.Success)
        {
            // If blocked by dependent modules, return 409 Conflict
            if (result.DependentModules is { Count: > 0 })
            {
                return Results.Conflict(new ModuleUninstallResponse
                {
                    Success = false,
                    Messages = result.Messages,
                    Errors = result.Errors,
                    DependentModules = result.DependentModules
                });
            }

            return Results.UnprocessableEntity(new ModuleUninstallResponse
            {
                Success = false,
                Messages = result.Messages,
                Errors = result.Errors
            });
        }

        return Results.Ok(new ModuleUninstallResponse
        {
            Success = true,
            Messages = result.Messages
        });
    }
}
