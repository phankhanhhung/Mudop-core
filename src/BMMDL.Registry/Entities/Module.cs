namespace BMMDL.Registry.Entities;

/// <summary>
/// Module declaration with versioning and human actor tracking.
/// </summary>
public class Module
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    
    // Parsed semantic version
    public int VersionMajor { get; set; }
    public int VersionMinor { get; set; }
    public int VersionPatch { get; set; }
    
    // Human actors
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string[] Reviewers { get; set; } = Array.Empty<string>();
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectedBy { get; set; }
    public string? RejectedReason { get; set; }
    public DateTime? RejectedAt { get; set; }
    
    // Extension info
    public Guid? ExtendsModuleId { get; set; }
    public Module? ExtendsModule { get; set; }
    
    // Multi-tenant
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    
    public ModuleStatus Status { get; set; } = ModuleStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    // L20: Module publishes/imports/tenant-aware
    public string? PublishesJson { get; set; }
    public string? ImportsJson { get; set; }
    public bool TenantAware { get; set; }

    // Navigation
    public ICollection<ModuleDependency> Dependencies { get; set; } = new List<ModuleDependency>();
    public ICollection<ModuleDeprecation> Deprecations { get; set; } = new List<ModuleDeprecation>();
}

public enum ModuleStatus
{
    Draft,
    PendingApproval,
    Rejected,
    Published,
    Deprecated
}
