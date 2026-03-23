namespace BMMDL.Registry.Entities;

/// <summary>
/// Tracks version history for individual meta model objects (entities, fields, enums, etc.)
/// </summary>
public class ObjectVersion
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant this version belongs to.
    /// </summary>
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    
    /// <summary>
    /// Module this object belongs to.
    /// </summary>
    public Guid ModuleId { get; set; }
    public Module? Module { get; set; }
    
    /// <summary>
    /// Type of object: entity, field, enum, type, rule, etc.
    /// </summary>
    public string ObjectType { get; set; } = "";
    
    /// <summary>
    /// Qualified name of the object (e.g., "warehouse.Product", "warehouse.Product.sku")
    /// </summary>
    public string ObjectName { get; set; } = "";
    
    /// <summary>
    /// Version string (semantic version format).
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    public int VersionMajor { get; set; } = 1;
    public int VersionMinor { get; set; } = 0;
    public int VersionPatch { get; set; } = 0;
    
    /// <summary>
    /// SHA256 hash of the object definition for change detection.
    /// </summary>
    public string DefinitionHash { get; set; } = "";
    
    /// <summary>
    /// JSON snapshot of the object definition at this version.
    /// </summary>
    public string? DefinitionSnapshot { get; set; }
    
    /// <summary>
    /// Change category from previous version (Patch, Minor, Major).
    /// </summary>
    public string? ChangeCategory { get; set; }
    
    /// <summary>
    /// Whether this version contains breaking changes.
    /// </summary>
    public bool IsBreaking { get; set; }
    
    /// <summary>
    /// Description of changes in this version.
    /// </summary>
    public string? ChangeDescription { get; set; }
    
    /// <summary>
    /// UP migration SQL for this version change.
    /// </summary>
    public string? MigrationUpSql { get; set; }
    
    /// <summary>
    /// DOWN migration SQL to rollback this version.
    /// </summary>
    public string? MigrationDownSql { get; set; }
    
    /// <summary>
    /// Status: Draft, PendingApproval, Approved, Applied, Rolledback
    /// </summary>
    public ObjectVersionStatus Status { get; set; } = ObjectVersionStatus.Draft;
    
    /// <summary>
    /// Who created this version.
    /// </summary>
    public string? CreatedBy { get; set; }
    
    /// <summary>
    /// When this version was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who approved this version (if breaking change required approval).
    /// </summary>
    public string? ApprovedBy { get; set; }
    
    /// <summary>
    /// When this version was approved.
    /// </summary>
    public DateTime? ApprovedAt { get; set; }
    
    /// <summary>
    /// When this version was applied to the database.
    /// </summary>
    public DateTime? AppliedAt { get; set; }
    
    /// <summary>
    /// Reference to breaking changes requiring approval.
    /// </summary>
    public ICollection<BreakingChange> BreakingChanges { get; } = new List<BreakingChange>();
}

public enum ObjectVersionStatus
{
    /// <summary>
    /// Version is being prepared, not yet submitted.
    /// </summary>
    Draft,
    
    /// <summary>
    /// Breaking changes require approval before applying.
    /// </summary>
    PendingApproval,
    
    /// <summary>
    /// Approved but not yet applied.
    /// </summary>
    Approved,
    
    /// <summary>
    /// Applied to database successfully.
    /// </summary>
    Applied,
    
    /// <summary>
    /// Rolled back to previous version.
    /// </summary>
    Rolledback,
    
    /// <summary>
    /// Rejected, will not be applied.
    /// </summary>
    Rejected
}
