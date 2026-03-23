namespace BMMDL.Registry.Entities;

/// <summary>
/// Tracks maintenance window for version upgrades with dual-version support.
/// </summary>
public class UpgradeWindow
{
    public Guid Id { get; set; }
    
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    
    public Guid ModuleId { get; set; }
    public Module? Module { get; set; }
    
    /// <summary>
    /// Version transitioning from.
    /// </summary>
    public string FromVersion { get; set; } = "";
    
    /// <summary>
    /// Version transitioning to.
    /// </summary>
    public string ToVersion { get; set; } = "";
    
    // Window timing
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }  // Max duration
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    
    /// <summary>
    /// Status of the upgrade window.
    /// </summary>
    public UpgradeStatus Status { get; set; } = UpgradeStatus.Scheduled;
    
    // Phase timestamps
    /// <summary>
    /// When v1 becomes read-only.
    /// </summary>
    public DateTime? V1ReadonlyAfter { get; set; }
    
    /// <summary>
    /// When v2 becomes primary.
    /// </summary>
    public DateTime? V2PrimaryAfter { get; set; }
    
    /// <summary>
    /// When v1 can be dropped (after grace period).
    /// </summary>
    public DateTime? V1CleanupAfter { get; set; }
    
    // Rollback info
    public DateTime? RollbackAvailableUntil { get; set; }
    public string? RollbackReason { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    
    /// <summary>
    /// Sync status for each entity in this upgrade.
    /// </summary>
    public ICollection<UpgradeSyncStatus> SyncStatuses { get; } = new List<UpgradeSyncStatus>();
}

public enum UpgradeStatus
{
    /// <summary>
    /// Upgrade scheduled but not started.
    /// </summary>
    Scheduled,
    
    /// <summary>
    /// Preparing v2 schema and initial data load.
    /// </summary>
    Preparing,
    
    /// <summary>
    /// Both versions running with sync triggers.
    /// </summary>
    DualVersion,
    
    /// <summary>
    /// Switching primary from v1 to v2.
    /// </summary>
    Cutover,
    
    /// <summary>
    /// Validating v2 is working correctly.
    /// </summary>
    Validating,
    
    /// <summary>
    /// Upgrade completed successfully.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Rolled back to v1.
    /// </summary>
    RolledBack,
    
    /// <summary>
    /// Failed during upgrade.
    /// </summary>
    Failed
}
