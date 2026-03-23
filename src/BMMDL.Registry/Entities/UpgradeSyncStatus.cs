namespace BMMDL.Registry.Entities;

/// <summary>
/// Tracks sync progress for individual entities during upgrade.
/// </summary>
public class UpgradeSyncStatus
{
    public Guid Id { get; set; }
    
    public Guid WindowId { get; set; }
    public UpgradeWindow? Window { get; set; }
    
    /// <summary>
    /// Entity being synced (e.g., "warehouse.Product").
    /// </summary>
    public string EntityName { get; set; } = "";
    
    // Sync progress
    public long TotalRecords { get; set; }
    public long MigratedRecords { get; set; }
    public long SyncErrors { get; set; }
    
    // Timing
    public DateTime? MigrationStartedAt { get; set; }
    public DateTime? MigrationCompletedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    
    // Delta sync for ongoing changes
    public long PendingDeltas { get; set; }
    public int? DeltaLagSeconds { get; set; }
    
    /// <summary>
    /// Current sync status.
    /// </summary>
    public SyncPhase Phase { get; set; } = SyncPhase.Pending;
    
    /// <summary>
    /// Last error message if any.
    /// </summary>
    public string? LastError { get; set; }
    
    /// <summary>
    /// SQL for v2→v1 sync trigger.
    /// </summary>
    public string? SyncTriggerSql { get; set; }
    
    /// <summary>
    /// Whether sync trigger is active.
    /// </summary>
    public bool IsSyncTriggerActive { get; set; }

    /// <summary>
    /// Progress percentage.
    /// </summary>
    public double ProgressPercent => TotalRecords > 0 ? (double)MigratedRecords / TotalRecords * 100 : 0;
}

public enum SyncPhase
{
    /// <summary>
    /// Not started yet.
    /// </summary>
    Pending,
    
    /// <summary>
    /// Initial bulk data migration.
    /// </summary>
    BulkMigration,
    
    /// <summary>
    /// Sync trigger active, processing deltas.
    /// </summary>
    DeltaSync,
    
    /// <summary>
    /// Sync completed.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Rolled back.
    /// </summary>
    RolledBack,
    
    /// <summary>
    /// Failed.
    /// </summary>
    Failed
}
