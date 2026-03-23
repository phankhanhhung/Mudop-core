namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Client for communicating with the BMMDL Registry API.
/// Used by the plugin system to compile and install BMMDL modules.
/// </summary>
public interface IRegistryClient
{
    /// <summary>
    /// Compile and install a BMMDL module into the Registry.
    /// </summary>
    /// <param name="request">Compilation request with source and options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the compilation.</returns>
    Task<ModuleInstallResult> CompileAndInstallModuleAsync(
        ModuleInstallRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get the list of installed modules from the Registry.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of installed module status records.</returns>
    Task<TenantModuleListResult> GetInstalledModulesAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Install a module for a specific tenant by compiling and initializing its schema.
    /// </summary>
    /// <param name="tenantId">Tenant to install the module for.</param>
    /// <param name="moduleName">Name of the module to install.</param>
    /// <param name="installedBy">Identity of the user performing the installation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the installation.</returns>
    Task<TenantModuleInstallResult> InstallModuleForTenantAsync(
        Guid tenantId,
        string moduleName,
        string installedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Uninstall a module from a specific tenant.
    /// </summary>
    /// <param name="tenantId">Tenant to uninstall the module from.</param>
    /// <param name="moduleName">Name of the module to uninstall.</param>
    /// <param name="uninstalledBy">Identity of the user performing the uninstallation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the uninstallation.</returns>
    Task<TenantModuleUninstallResult> UninstallModuleForTenantAsync(
        Guid tenantId,
        string moduleName,
        string uninstalledBy,
        CancellationToken ct = default);
}

/// <summary>
/// Request to compile and install a BMMDL module via the Registry API.
/// </summary>
public record ModuleInstallRequest
{
    /// <summary>Raw BMMDL source code.</summary>
    public required string BmmdlSource { get; init; }

    /// <summary>Module name for identification.</summary>
    public required string ModuleName { get; init; }

    /// <summary>Initialize database tables after compilation.</summary>
    public bool InitSchema { get; init; } = true;

    /// <summary>Force schema recreation (drops existing tables).</summary>
    public bool Force { get; init; } = false;

    /// <summary>Tenant ID for module ownership.</summary>
    public Guid? TenantId { get; init; }
}

/// <summary>
/// Result from a BMMDL module compilation/installation.
/// </summary>
public record ModuleInstallResult
{
    public bool Success { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? SchemaResult { get; init; }
}

/// <summary>
/// Result from listing installed modules for a tenant.
/// </summary>
public record TenantModuleListResult
{
    public bool Success { get; init; }
    public List<TenantModuleInfo> Modules { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Information about a module installed for a tenant.
/// </summary>
public record TenantModuleInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public DateTime InstalledAt { get; init; }
    public bool SchemaInitialized { get; init; }
    public string? SchemaName { get; init; }
}

/// <summary>
/// Result from installing a module for a tenant.
/// </summary>
public record TenantModuleInstallResult
{
    public bool Success { get; init; }
    public string? ModuleName { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? SchemaResult { get; init; }
}

/// <summary>
/// Result from uninstalling a module from a tenant.
/// </summary>
public record TenantModuleUninstallResult
{
    public bool Success { get; init; }
    public List<string> Messages { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    /// <summary>Modules that depend on this one (if uninstall is blocked).</summary>
    public List<string>? DependentModules { get; init; }
}
