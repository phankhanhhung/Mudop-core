using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;

namespace BMMDL.Registry.Services;

/// <summary>
/// Service for managing meta model versioning during installation.
/// Integrates ChangeDetector, MigrationGenerator, and ObjectVersionRepository.
/// </summary>
public class VersioningService
{
    private readonly ObjectVersionRepository _versionRepo;
    private readonly ChangeDetector _changeDetector;
    private readonly DefinitionHasher _hasher;
    private readonly MigrationGenerator _migrationGen;

    public VersioningService(
        ObjectVersionRepository versionRepo,
        string schemaName = "public")
    {
        _versionRepo = versionRepo;
        _changeDetector = new ChangeDetector();
        _hasher = new DefinitionHasher();
        _migrationGen = new MigrationGenerator(schemaName);
    }

    /// <summary>
    /// Process model for versioning: detect changes, generate migrations, create version records.
    /// Returns VersioningResult with breaking changes requiring approval if any.
    /// </summary>
    public async Task<VersioningResult> ProcessModelVersionAsync(
        Guid tenantId,
        Guid moduleId,
        BmModel? existingModel,
        BmModel incomingModel,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        var result = new VersioningResult();

        // 1. Detect changes
        var changes = _changeDetector.DetectChanges(existingModel, incomingModel);
        result.DetectionResult = changes;

        if (changes.TotalChanges == 0)
        {
            result.Status = VersioningStatus.NoChanges;
            return result;
        }

        // 2. Calculate new version
        var currentVersion = await GetCurrentVersionAsync(tenantId, moduleId, ct);
        var newVersion = VersionParser.Bump(currentVersion, changes.OverallCategory);
        result.NewVersion = newVersion.ToString();
        result.ChangeCategory = changes.OverallCategory;

        // 3. Generate migration if needed
        if (changes.TotalChanges > 0)
        {
            var migrationPlan = _migrationGen.GenerateMigration(changes);
            result.MigrationPlan = migrationPlan;
        }

        // 4. Create version records for changed entities
        var versionRecords = await CreateVersionRecordsAsync(
            tenantId, moduleId, incomingModel, changes, newVersion, createdBy, ct);
        result.VersionRecords = versionRecords;

        // 5. Check if approval is needed
        if (changes.HasBreakingChanges)
        {
            result.Status = VersioningStatus.PendingApproval;
            result.BreakingChanges = changes.GetBreakingChanges();
        }
        else
        {
            result.Status = VersioningStatus.ReadyToApply;
        }

        return result;
    }

    /// <summary>
    /// Get current module version (from latest entity version).
    /// </summary>
    public async Task<string> GetCurrentVersionAsync(Guid tenantId, Guid moduleId, CancellationToken ct)
    {
        // Get the latest applied version for any entity in this module
        var latestVersion = await _versionRepo.GetLatestAppliedVersionForModuleAsync(moduleId, ct);
        
        // Default to 0.0.0 for first installation, so bump results in 1.0.0
        return latestVersion?.Version ?? "0.0.0";
    }

    /// <summary>
    /// Create version records for all changed objects.
    /// </summary>
    private async Task<List<ObjectVersion>> CreateVersionRecordsAsync(
        Guid tenantId,
        Guid moduleId,
        BmModel model,
        ChangeDetectionResult changes,
        SemanticVersion newVersion,
        string? createdBy,
        CancellationToken ct)
    {
        var records = new List<ObjectVersion>();

        // Create version records for entities with changes
        foreach (var entityChange in changes.EntityChanges)
        {
            var entity = model.Entities.FirstOrDefault(e => e.QualifiedName == entityChange.EntityName);
            if (entity == null && entityChange.ChangeType != ObjectChangeType.Remove)
                continue;

            var version = new ObjectVersion
            {
                TenantId = tenantId,
                ModuleId = moduleId,
                ObjectType = "entity",
                ObjectName = entityChange.EntityName,
                Version = newVersion.ToString(),
                VersionMajor = newVersion.Major,
                VersionMinor = newVersion.Minor,
                VersionPatch = newVersion.Patch,
                DefinitionHash = entity != null ? _hasher.HashEntity(entity) : "",
                DefinitionSnapshot = entity != null ? _hasher.SerializeToJson(entity) : null,
                ChangeCategory = entityChange.Category.ToString(),
                IsBreaking = entityChange.IsBreaking,
                ChangeDescription = entityChange.Description,
                Status = entityChange.IsBreaking ? ObjectVersionStatus.PendingApproval : ObjectVersionStatus.Draft,
                CreatedBy = createdBy
            };

            var created = await _versionRepo.CreateVersionAsync(version, ct);
            records.Add(created);

            // Add breaking change records
            if (entityChange.IsBreaking)
            {
                await _versionRepo.AddBreakingChangesAsync(created.Id, new[]
                {
                    new BreakingChange
                    {
                        ChangeType = entityChange.ChangeType.ToString(),
                        TargetName = entityChange.EntityName,
                        Description = entityChange.Description,
                        OldValue = entityChange.OldHash,
                        NewValue = entityChange.NewHash
                    }
                }, ct);
            }
        }

        // Create version records for field changes
        foreach (var fieldChange in changes.FieldChanges.Where(f => f.IsBreaking))
        {
            // Field-level breaking changes are tracked under entity
            var entityVersion = records.FirstOrDefault(r => 
                r.ObjectType == "entity" && r.ObjectName == fieldChange.EntityName);
            
            if (entityVersion != null)
            {
                await _versionRepo.AddBreakingChangesAsync(entityVersion.Id, new[]
                {
                    new BreakingChange
                    {
                        ChangeType = $"Field{fieldChange.ChangeType}",
                        TargetName = $"{fieldChange.EntityName}.{fieldChange.FieldName}",
                        Description = fieldChange.Description,
                        OldValue = fieldChange.OldValue,
                        NewValue = fieldChange.NewValue
                    }
                }, ct);
            }
        }

        return records;
    }

    /// <summary>
    /// Approve all breaking changes for a module version.
    /// </summary>
    public async Task<bool> ApproveBreakingChangesAsync(
        Guid moduleId,
        string approvedBy,
        CancellationToken ct = default)
    {
        var pending = await _versionRepo.GetPendingApprovalsAsync(moduleId, ct);
        
        foreach (var version in pending)
        {
            var changes = await _versionRepo.GetBreakingChangesAsync(version.Id, ct);
            foreach (var change in changes)
            {
                await _versionRepo.ReviewBreakingChangeAsync(
                    change.Id, 
                    BreakingChangeStatus.Approved, 
                    approvedBy, 
                    ct: ct);
            }
        }

        return pending.Count > 0;
    }

    /// <summary>
    /// Apply approved versions (mark as applied).
    /// </summary>
    public async Task ApplyApprovedVersionsAsync(
        Guid moduleId, 
        CancellationToken ct = default)
    {
        var approved = await _versionRepo.GetPendingApprovalsAsync(moduleId, ct);
        foreach (var version in approved.Where(v => v.Status == ObjectVersionStatus.Approved))
        {
            await _versionRepo.UpdateStatusAsync(version.Id, ObjectVersionStatus.Applied, ct: ct);
        }
    }
}

#region Result Types

/// <summary>
/// Result of versioning process.
/// </summary>
public class VersioningResult
{
    public VersioningStatus Status { get; set; } = VersioningStatus.NoChanges;
    public string NewVersion { get; set; } = "";
    public ChangeCategory ChangeCategory { get; set; }
    public ChangeDetectionResult? DetectionResult { get; set; }
    public MigrationPlan? MigrationPlan { get; set; }
    public List<ObjectVersion> VersionRecords { get; set; } = new();
    public List<IObjectChange> BreakingChanges { get; set; } = new();

    public bool HasBreakingChanges => BreakingChanges.Count > 0;
    public bool RequiresApproval => Status == VersioningStatus.PendingApproval;
}

public enum VersioningStatus
{
    NoChanges,
    ReadyToApply,
    PendingApproval,
    Applied,
    Failed
}

#endregion
