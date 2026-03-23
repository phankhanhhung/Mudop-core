using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories;

/// <summary>
/// Repository for ObjectVersion and BreakingChange entities.
/// </summary>
public class ObjectVersionRepository
{
    private readonly RegistryDbContext _db;

    public ObjectVersionRepository(RegistryDbContext db)
    {
        _db = db;
    }

    #region ObjectVersion Operations

    /// <summary>
    /// Get latest version for an object.
    /// </summary>
    public async Task<ObjectVersion?> GetLatestVersionAsync(
        Guid tenantId, 
        string objectType, 
        string objectName, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Where(v => v.TenantId == tenantId && v.ObjectType == objectType && v.ObjectName == objectName)
            .OrderByDescending(v => v.VersionMajor)
            .ThenByDescending(v => v.VersionMinor)
            .ThenByDescending(v => v.VersionPatch)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get version by hash for duplicate detection.
    /// </summary>
    public async Task<ObjectVersion?> GetVersionByHashAsync(
        Guid tenantId, 
        string definitionHash, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.DefinitionHash == definitionHash, ct);
    }

    /// <summary>
    /// Get all versions of an object (history).
    /// </summary>
    public async Task<IReadOnlyList<ObjectVersion>> GetVersionHistoryAsync(
        Guid tenantId, 
        string objectName, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Where(v => v.TenantId == tenantId && v.ObjectName == objectName)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all pending approval versions for a module.
    /// </summary>
    public async Task<IReadOnlyList<ObjectVersion>> GetPendingApprovalsAsync(
        Guid moduleId, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Include(v => v.BreakingChanges)
            .Where(v => v.ModuleId == moduleId && v.Status == ObjectVersionStatus.PendingApproval)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Create new object version.
    /// </summary>
    public async Task<ObjectVersion> CreateVersionAsync(ObjectVersion version, CancellationToken ct = default)
    {
        version.Id = Guid.NewGuid();
        version.CreatedAt = DateTime.UtcNow;
        _db.ObjectVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        return version;
    }

    /// <summary>
    /// Update version status.
    /// </summary>
    public async Task UpdateStatusAsync(
        Guid versionId, 
        ObjectVersionStatus status, 
        string? approvedBy = null, 
        CancellationToken ct = default)
    {
        var version = await _db.ObjectVersions.FindAsync(new object[] { versionId }, ct);
        if (version != null)
        {
            version.Status = status;
            
            if (status == ObjectVersionStatus.Approved && !string.IsNullOrEmpty(approvedBy))
            {
                version.ApprovedBy = approvedBy;
                version.ApprovedAt = DateTime.UtcNow;
            }
            else if (status == ObjectVersionStatus.Applied)
            {
                version.AppliedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    #endregion

    #region BreakingChange Operations

    /// <summary>
    /// Add breaking changes to a version.
    /// </summary>
    public async Task AddBreakingChangesAsync(
        Guid versionId, 
        IEnumerable<BreakingChange> changes, 
        CancellationToken ct = default)
    {
        foreach (var change in changes)
        {
            change.Id = Guid.NewGuid();
            change.ObjectVersionId = versionId;
            change.CreatedAt = DateTime.UtcNow;
            _db.BreakingChanges.Add(change);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get breaking changes for a version.
    /// </summary>
    public async Task<IReadOnlyList<BreakingChange>> GetBreakingChangesAsync(
        Guid versionId, 
        CancellationToken ct = default)
    {
        return await _db.BreakingChanges
            .Where(c => c.ObjectVersionId == versionId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Review breaking change (approve/reject).
    /// </summary>
    public async Task ReviewBreakingChangeAsync(
        Guid changeId, 
        BreakingChangeStatus status, 
        string reviewedBy, 
        string? notes = null, 
        CancellationToken ct = default)
    {
        var change = await _db.BreakingChanges.FindAsync(new object[] { changeId }, ct);
        if (change != null)
        {
            change.Status = status;
            change.ReviewedBy = reviewedBy;
            change.ReviewedAt = DateTime.UtcNow;
            change.ReviewerNotes = notes;
            await _db.SaveChangesAsync(ct);

            // Check if all breaking changes for this version are approved
            await CheckAndUpdateVersionStatusAsync(change.ObjectVersionId, ct);
        }
    }

    /// <summary>
    /// Check if all breaking changes are approved and update version status.
    /// </summary>
    private async Task CheckAndUpdateVersionStatusAsync(Guid versionId, CancellationToken ct)
    {
        var allChanges = await _db.BreakingChanges
            .Where(c => c.ObjectVersionId == versionId)
            .ToListAsync(ct);

        if (allChanges.Count == 0) return;

        var allApproved = allChanges.All(c => c.Status == BreakingChangeStatus.Approved);
        var anyRejected = allChanges.Any(c => c.Status == BreakingChangeStatus.Rejected);

        if (anyRejected)
        {
            await UpdateStatusAsync(versionId, ObjectVersionStatus.Rejected, ct: ct);
        }
        else if (allApproved)
        {
            await UpdateStatusAsync(versionId, ObjectVersionStatus.Approved, ct: ct);
        }
    }

    #endregion

    #region Query Operations

    /// <summary>
    /// Get versions with breaking changes for a module.
    /// </summary>
    public async Task<IReadOnlyList<ObjectVersion>> GetVersionsWithBreakingChangesAsync(
        Guid moduleId, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Include(v => v.BreakingChanges)
            .Where(v => v.ModuleId == moduleId && v.IsBreaking)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Check if object has any version (for first install vs update).
    /// </summary>
    public async Task<bool> HasVersionAsync(
        Guid tenantId, 
        string objectName, 
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .AnyAsync(v => v.TenantId == tenantId && v.ObjectName == objectName, ct);
    }

    /// <summary>
    /// Get the latest applied version for a module.
    /// </summary>
    public async Task<ObjectVersion?> GetLatestAppliedVersionForModuleAsync(
        Guid moduleId,
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Where(v => v.ModuleId == moduleId && v.Status == ObjectVersionStatus.Applied)
            .OrderByDescending(v => v.VersionMajor)
            .ThenByDescending(v => v.VersionMinor)
            .ThenByDescending(v => v.VersionPatch)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get count of entity objects for a module.
    /// </summary>
    public async Task<int> GetEntityCountForModuleAsync(
        Guid moduleId,
        CancellationToken ct = default)
    {
        return await _db.ObjectVersions
            .Where(v => v.ModuleId == moduleId && v.ObjectType == "entity")
            .Select(v => v.ObjectName)
            .Distinct()
            .CountAsync(ct);
    }

    #endregion
}
