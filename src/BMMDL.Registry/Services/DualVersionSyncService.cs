using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Services;

/// <summary>
/// Orchestrates the dual-version upgrade lifecycle.
/// </summary>
public class DualVersionSyncService
{
    private readonly RegistryDbContext _db;
    private readonly SyncTriggerGenerator _triggerGen;

    public DualVersionSyncService(RegistryDbContext db, string schemaName = "public")
    {
        _db = db;
        _triggerGen = new SyncTriggerGenerator(schemaName);
    }

    #region Upgrade Window Management

    /// <summary>
    /// Schedule a new upgrade window for a module.
    /// </summary>
    public async Task<UpgradeWindow> ScheduleUpgradeAsync(
        Guid tenantId,
        Guid moduleId,
        string fromVersion,
        string toVersion,
        DateTime scheduledStart,
        DateTime scheduledEnd,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        var window = new UpgradeWindow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ModuleId = moduleId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
            Status = UpgradeStatus.Scheduled,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _db.UpgradeWindows.Add(window);
        await _db.SaveChangesAsync(ct);
        return window;
    }

    /// <summary>
    /// Get active upgrade window for a tenant/module.
    /// </summary>
    public virtual async Task<UpgradeWindow?> GetActiveUpgradeAsync(
        Guid tenantId,
        Guid moduleId,
        CancellationToken ct = default)
    {
        return await _db.UpgradeWindows
            .Include(w => w.SyncStatuses)
            .Where(w => w.TenantId == tenantId && 
                        w.ModuleId == moduleId &&
                        w.Status != UpgradeStatus.Completed &&
                        w.Status != UpgradeStatus.RolledBack &&
                        w.Status != UpgradeStatus.Failed)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Transition upgrade to next phase.
    /// </summary>
    public async Task TransitionStatusAsync(
        Guid windowId,
        UpgradeStatus newStatus,
        CancellationToken ct = default)
    {
        var window = await _db.UpgradeWindows.FindAsync(new object[] { windowId }, ct);
        if (window == null) return;

        window.Status = newStatus;

        switch (newStatus)
        {
            case UpgradeStatus.Preparing:
                window.ActualStart = DateTime.UtcNow;
                break;
            case UpgradeStatus.DualVersion:
                window.V1ReadonlyAfter = DateTime.UtcNow;
                break;
            case UpgradeStatus.Cutover:
                window.V2PrimaryAfter = DateTime.UtcNow;
                break;
            case UpgradeStatus.Completed:
                window.ActualEnd = DateTime.UtcNow;
                window.V1CleanupAfter = DateTime.UtcNow.AddDays(7); // 7-day grace period
                break;
            case UpgradeStatus.RolledBack:
                window.ActualEnd = DateTime.UtcNow;
                break;
        }

        await _db.SaveChangesAsync(ct);
    }

    #endregion

    #region Sync Status Management

    /// <summary>
    /// Initialize sync status for all entities in upgrade.
    /// </summary>
    public async Task InitializeSyncStatusesAsync(
        Guid windowId,
        BmModel v1Model,
        BmModel v2Model,
        CancellationToken ct = default)
    {
        foreach (var entity in v2Model.Entities)
        {
            var v1Entity = v1Model.Entities.FirstOrDefault(e => e.QualifiedName == entity.QualifiedName);
            
            // Generate sync trigger SQL
            var triggerResult = v1Entity != null 
                ? _triggerGen.GenerateV2ToV1SyncTrigger(entity.QualifiedName, v1Entity, entity)
                : null;

            var syncStatus = new UpgradeSyncStatus
            {
                Id = Guid.NewGuid(),
                WindowId = windowId,
                EntityName = entity.QualifiedName,
                TotalRecords = 0, // Will be populated during bulk migration
                MigratedRecords = 0,
                SyncErrors = 0,
                Phase = SyncPhase.Pending,
                SyncTriggerSql = triggerResult?.GetCreateSql(),
                IsSyncTriggerActive = false
            };

            _db.UpgradeSyncStatuses.Add(syncStatus);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Update sync progress for an entity.
    /// </summary>
    public async Task UpdateSyncProgressAsync(
        Guid windowId,
        string entityName,
        long migratedRecords,
        long? totalRecords = null,
        long? syncErrors = null,
        SyncPhase? phase = null,
        CancellationToken ct = default)
    {
        var status = await _db.UpgradeSyncStatuses
            .FirstOrDefaultAsync(s => s.WindowId == windowId && s.EntityName == entityName, ct);
        
        if (status == null) return;

        status.MigratedRecords = migratedRecords;
        status.LastSyncAt = DateTime.UtcNow;

        if (totalRecords.HasValue)
            status.TotalRecords = totalRecords.Value;
        if (syncErrors.HasValue)
            status.SyncErrors = syncErrors.Value;
        if (phase.HasValue)
        {
            status.Phase = phase.Value;
            if (phase == SyncPhase.BulkMigration)
                status.MigrationStartedAt = DateTime.UtcNow;
            else if (phase == SyncPhase.Completed)
                status.MigrationCompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Activate sync trigger for an entity.
    /// </summary>
    public async Task ActivateSyncTriggerAsync(
        Guid windowId,
        string entityName,
        CancellationToken ct = default)
    {
        var status = await _db.UpgradeSyncStatuses
            .FirstOrDefaultAsync(s => s.WindowId == windowId && s.EntityName == entityName, ct);
        
        if (status == null) return;

        status.IsSyncTriggerActive = true;
        status.Phase = SyncPhase.DeltaSync;
        await _db.SaveChangesAsync(ct);
    }

    #endregion

    #region Query Methods

    /// <summary>
    /// Check if tenant is currently in upgrade for a module.
    /// </summary>
    public async Task<bool> IsInUpgradeWindowAsync(
        Guid tenantId,
        Guid moduleId,
        CancellationToken ct = default)
    {
        var window = await GetActiveUpgradeAsync(tenantId, moduleId, ct);
        return window != null && 
               window.Status != UpgradeStatus.Scheduled &&
               window.Status != UpgradeStatus.Completed;
    }

    /// <summary>
    /// Get overall upgrade progress.
    /// </summary>
    public async Task<UpgradeProgress> GetUpgradeProgressAsync(
        Guid windowId,
        CancellationToken ct = default)
    {
        var statuses = await _db.UpgradeSyncStatuses
            .Where(s => s.WindowId == windowId)
            .ToListAsync(ct);

        return new UpgradeProgress
        {
            TotalEntities = statuses.Count,
            CompletedEntities = statuses.Count(s => s.Phase == SyncPhase.Completed),
            InProgressEntities = statuses.Count(s => s.Phase == SyncPhase.BulkMigration || s.Phase == SyncPhase.DeltaSync),
            FailedEntities = statuses.Count(s => s.Phase == SyncPhase.Failed),
            TotalRecords = statuses.Sum(s => s.TotalRecords),
            MigratedRecords = statuses.Sum(s => s.MigratedRecords),
            SyncErrors = statuses.Sum(s => s.SyncErrors),
            OverallPercent = statuses.Sum(s => s.TotalRecords) > 0
                ? (double)statuses.Sum(s => s.MigratedRecords) / statuses.Sum(s => s.TotalRecords) * 100
                : 0
        };
    }

    #endregion
}

#region Progress Types

public class UpgradeProgress
{
    public int TotalEntities { get; set; }
    public int CompletedEntities { get; set; }
    public int InProgressEntities { get; set; }
    public int FailedEntities { get; set; }
    public long TotalRecords { get; set; }
    public long MigratedRecords { get; set; }
    public long SyncErrors { get; set; }
    public double OverallPercent { get; set; }
    
    public bool IsComplete => CompletedEntities == TotalEntities;
    public bool HasErrors => FailedEntities > 0 || SyncErrors > 0;
}

#endregion
