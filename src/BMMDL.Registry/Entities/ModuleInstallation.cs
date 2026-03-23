namespace BMMDL.Registry.Entities;

/// <summary>
/// Tracks module installations per tenant with ordering.
/// Used for safe install/uninstall operations.
/// </summary>
public class ModuleInstallation
{
    public Guid Id { get; set; }
    
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    
    public Guid ModuleId { get; set; }
    public Module? Module { get; set; }
    
    /// <summary>
    /// Installation order (1-based, per tenant).
    /// Higher number = installed later.
    /// Modules must be uninstalled in reverse order.
    /// </summary>
    public int InstallOrder { get; set; }
    
    public InstallationStatus Status { get; set; }
    public DateTime InstalledAt { get; set; }
    public string? InstalledBy { get; set; }
    
    public DateTime? UninstalledAt { get; set; }
    public string? UninstalledBy { get; set; }
    
    /// <summary>
    /// Snapshot of element counts at install time for audit.
    /// </summary>
    public int EntityCount { get; set; }
    public int TypeCount { get; set; }
    public int EnumCount { get; set; }
    public int ServiceCount { get; set; }
    public int RuleCount { get; set; }
    
    /// <summary>
    /// Hash of source files at install time for change detection.
    /// </summary>
    public string? SourceHash { get; set; }
    
    /// <summary>
    /// Error message if installation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

public enum InstallationStatus
{
    /// <summary>
    /// Installation is pending (not yet started).
    /// </summary>
    Pending,
    
    /// <summary>
    /// Installation is in progress.
    /// </summary>
    Installing,
    
    /// <summary>
    /// Module is successfully installed and active.
    /// </summary>
    Installed,
    
    /// <summary>
    /// Installation failed (see ErrorMessage for details).
    /// </summary>
    Failed,
    
    /// <summary>
    /// Uninstallation is in progress.
    /// </summary>
    Uninstalling,
    
    /// <summary>
    /// Module has been uninstalled.
    /// </summary>
    Uninstalled
}
