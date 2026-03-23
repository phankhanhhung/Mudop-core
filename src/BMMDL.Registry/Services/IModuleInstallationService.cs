using BMMDL.Registry.Entities;
using BMMDL.MetaModel;

namespace BMMDL.Registry.Services;

/// <summary>
/// Service for installing and uninstalling modules.
/// Note: Compilation and consistency checking are done by CLI.
/// </summary>
public interface IModuleInstallationService
{
    /// <summary>
    /// Install a pre-compiled, pre-validated module.
    /// Compilation and consistency checking should be done by CLI before calling this.
    /// </summary>
    Task<InstallResult> InstallModuleAsync(
        Guid tenantId,
        BmModel model,
        string sourceHash,
        string installedBy,
        CancellationToken ct = default);
    
    /// <summary>
    /// Install a pre-compiled, published module by ID.
    /// </summary>
    Task<InstallResult> InstallPublishedModuleAsync(
        Guid tenantId,
        Guid moduleId,
        string installedBy,
        CancellationToken ct = default);
    
    /// <summary>
    /// Safely uninstall a module (reverse order check).
    /// </summary>
    Task<UninstallResult> UninstallModuleAsync(
        Guid tenantId,
        Guid moduleId,
        string uninstalledBy,
        CancellationToken ct = default);
    
    /// <summary>
    /// Check if a module can be safely uninstalled.
    /// </summary>
    Task<UninstallCheckResult> CanUninstallAsync(
        Guid tenantId,
        Guid moduleId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get installation history for a tenant (ordered by InstallOrder).
    /// </summary>
    Task<IReadOnlyList<ModuleInstallation>> GetInstallationHistoryAsync(
        Guid tenantId,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a module installation.
/// </summary>
public class InstallResult
{
    public bool Success { get; private set; }
    public string Message { get; private set; } = "";
    public ModuleInstallation? Installation { get; private set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    
    public static InstallResult Succeeded(ModuleInstallation installation, string message = "Module installed successfully")
        => new() { Success = true, Installation = installation, Message = message };
    
    public static InstallResult Failed(string message, IEnumerable<string>? errors = null)
    {
        var result = new InstallResult { Success = false, Message = message };
        if (errors != null) result.Errors.AddRange(errors);
        return result;
    }
}

/// <summary>
/// Result of a module uninstallation.
/// </summary>
public class UninstallResult
{
    public bool Success { get; private set; }
    public string Message { get; private set; } = "";
    public List<string> Errors { get; } = new();
    
    public static UninstallResult Succeeded(string message = "Module uninstalled successfully")
        => new() { Success = true, Message = message };
    
    public static UninstallResult Failed(string message, IEnumerable<string>? errors = null)
    {
        var result = new UninstallResult { Success = false, Message = message };
        if (errors != null) result.Errors.AddRange(errors);
        return result;
    }
}

/// <summary>
/// Result of uninstall safety check.
/// </summary>
public record UninstallCheckResult(
    bool CanUninstall, 
    string Reason, 
    List<string>? BlockingModules = null);
