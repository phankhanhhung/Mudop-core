namespace BMMDL.Runtime.Api.Models;

/// <summary>
/// Request model for creating a new tenant.
/// </summary>
public class CreateTenantRequest
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request model for updating an existing tenant.
/// </summary>
public class UpdateTenantRequest
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Response model for tenant operations.
/// </summary>
public class TenantResponse
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
    public string SubscriptionTier { get; set; } = "free";
    public Guid OwnerId { get; set; }
}

/// <summary>
/// Request model for installing a module into a tenant.
/// </summary>
public class InstallModuleRequest
{
    public required string ModuleName { get; set; }
    public string? Version { get; set; }
}

/// <summary>
/// Response model for module information.
/// </summary>
public class ModuleInfo
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public string? Author { get; set; }
    public int EntityCount { get; set; }
    public int ServiceCount { get; set; }
    public DateTime InstalledAt { get; set; }
    public bool SchemaInitialized { get; set; }
    public string? SchemaName { get; set; }
}

/// <summary>
/// Response model for module installation result.
/// </summary>
public class ModuleInstallResponse
{
    public bool Success { get; set; }
    public string? ModuleName { get; set; }
    public int EntityCount { get; set; }
    public int ServiceCount { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? SchemaResult { get; set; }
}

/// <summary>
/// Response model for module uninstallation result.
/// </summary>
public class ModuleUninstallResponse
{
    public bool Success { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    /// <summary>Modules that depend on this one (if uninstall is blocked).</summary>
    public List<string>? DependentModules { get; set; }
}

/// <summary>
/// Request model for inviting a user to a tenant.
/// </summary>
public class InviteUserRequest
{
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
}
