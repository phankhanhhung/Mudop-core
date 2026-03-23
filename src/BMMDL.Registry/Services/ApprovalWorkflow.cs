using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;

namespace BMMDL.Registry.Services;

/// <summary>
/// Manages the approval workflow for modules and migrations.
/// </summary>
public class ApprovalWorkflow
{
    private readonly IModuleRepository _moduleRepository;
    private readonly IMigrationRepository _migrationRepository;
    private readonly DependencyResolver _dependencyResolver;
    private readonly MigrationExecutor _migrationExecutor;
    private readonly string? _platformConnectionString;

    public ApprovalWorkflow(
        IModuleRepository moduleRepository,
        IMigrationRepository migrationRepository,
        DependencyResolver dependencyResolver,
        MigrationExecutor migrationExecutor,
        string? platformConnectionString = null)
    {
        _moduleRepository = moduleRepository;
        _migrationRepository = migrationRepository;
        _dependencyResolver = dependencyResolver;
        _migrationExecutor = migrationExecutor;
        _platformConnectionString = platformConnectionString;
    }

    /// <summary>
    /// Submit a module for approval. Transitions status from Draft to PendingApproval.
    /// </summary>
    public async Task<ApprovalResult> SubmitModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _moduleRepository.GetByIdAsync(moduleId, ct);
        if (module == null)
        {
            return ApprovalResult.Failed("Module not found");
        }

        if (module.Status != ModuleStatus.Draft && module.Status != ModuleStatus.Rejected)
        {
            return ApprovalResult.Failed($"Module cannot be submitted for approval from '{module.Status}' status. Must be Draft or Rejected.");
        }

        // Check dependencies
        var depResult = await _dependencyResolver.ResolveAsync(module, ct);
        if (!depResult.IsFullyResolved)
        {
            var unresolved = string.Join(", ", depResult.Unresolved.Select(d => $"{d.DependsOnName} {d.VersionRange}"));
            return ApprovalResult.Failed($"Unresolved dependencies: {unresolved}");
        }

        // Transition to PendingApproval
        module.Status = ModuleStatus.PendingApproval;
        // Clear any previous rejection info
        module.RejectedBy = null;
        module.RejectedReason = null;
        module.RejectedAt = null;
        await _moduleRepository.UpdateAsync(module, ct);

        return ApprovalResult.Success("Module submitted for approval. Dependencies resolved.");
    }

    /// <summary>
    /// Approve a module. Transitions status from PendingApproval to Published.
    /// </summary>
    public async Task<ApprovalResult> ApproveModuleAsync(Guid moduleId, string approvedBy, CancellationToken ct = default)
    {
        var module = await _moduleRepository.GetByIdAsync(moduleId, ct);
        if (module == null)
        {
            return ApprovalResult.Failed("Module not found");
        }

        if (module.Status == ModuleStatus.Published)
        {
            return ApprovalResult.Failed("Module is already published");
        }

        if (module.Status != ModuleStatus.PendingApproval)
        {
            return ApprovalResult.Failed($"Module must be in PendingApproval status to approve, but is '{module.Status}'.");
        }

        // Check if approver is in reviewers list (if reviewers are specified)
        if (module.Reviewers.Length > 0 && !module.Reviewers.Contains(approvedBy))
        {
            return ApprovalResult.Failed($"User {approvedBy} is not in the reviewers list");
        }

        // Publish the module
        module.Status = ModuleStatus.Published;
        module.ApprovedBy = approvedBy;
        module.ApprovedAt = DateTime.UtcNow;
        module.PublishedAt = DateTime.UtcNow;
        await _moduleRepository.UpdateAsync(module, ct);

        return ApprovalResult.Success($"Module {module.Name} v{module.Version} approved and published");
    }

    /// <summary>
    /// Reject a module. Transitions status from PendingApproval to Rejected with a reason.
    /// </summary>
    public async Task<ApprovalResult> RejectModuleAsync(Guid moduleId, string rejectedBy, string reason, CancellationToken ct = default)
    {
        var module = await _moduleRepository.GetByIdAsync(moduleId, ct);
        if (module == null)
        {
            return ApprovalResult.Failed("Module not found");
        }

        if (module.Status != ModuleStatus.PendingApproval)
        {
            return ApprovalResult.Failed($"Module must be in PendingApproval status to reject, but is '{module.Status}'.");
        }

        // Transition to Rejected with reason
        module.Status = ModuleStatus.Rejected;
        module.RejectedBy = rejectedBy;
        module.RejectedReason = reason;
        module.RejectedAt = DateTime.UtcNow;
        await _moduleRepository.UpdateAsync(module, ct);

        return ApprovalResult.Success($"Module {module.Name} rejected: {reason}");
    }

    /// <summary>
    /// Submit a migration for approval.
    /// </summary>
    public async Task<ApprovalResult> SubmitMigrationAsync(Guid migrationId, CancellationToken ct = default)
    {
        var migration = await _migrationRepository.GetByIdAsync(migrationId, ct);
        if (migration == null)
        {
            return ApprovalResult.Failed("Migration not found");
        }

        if (migration.ApprovedAt != null)
        {
            return ApprovalResult.Failed("Migration is already approved");
        }

        // Check if source module exists
        var module = migration.Module;
        if (module == null)
        {
            return ApprovalResult.Failed("Associated module not found");
        }

        // Validate change type
        if (migration.ChangeType == ChangeType.Breaking)
        {
            return ApprovalResult.Success("Migration submitted for approval. WARNING: This is a BREAKING change!");
        }

        return ApprovalResult.Success("Migration submitted for approval");
    }

    /// <summary>
    /// Approve a migration.
    /// </summary>
    public async Task<ApprovalResult> ApproveMigrationAsync(Guid migrationId, Guid approvedBy, CancellationToken ct = default)
    {
        var migration = await _migrationRepository.GetByIdAsync(migrationId, ct);
        if (migration == null)
        {
            return ApprovalResult.Failed("Migration not found");
        }

        if (migration.ApprovedAt != null)
        {
            return ApprovalResult.Failed("Migration is already approved");
        }

        await _migrationRepository.ApproveAsync(migrationId, approvedBy, ct);

        return ApprovalResult.Success($"Migration from {migration.FromVersion} to {migration.ToVersion} approved");
    }

    /// <summary>
    /// Execute an approved migration by running its SQL script against the platform database.
    /// </summary>
    public async Task<ApprovalResult> ExecuteMigrationAsync(Guid migrationId, Guid executedBy, CancellationToken ct = default)
    {
        var migration = await _migrationRepository.GetByIdAsync(migrationId, ct);
        if (migration == null)
        {
            return ApprovalResult.Failed("Migration not found");
        }

        if (migration.ApprovedAt == null)
        {
            return ApprovalResult.Failed("Migration must be approved before execution");
        }

        if (migration.ExecutedAt != null)
        {
            return ApprovalResult.Failed("Migration is already executed");
        }

        // Execute the SQL script if present
        if (!string.IsNullOrWhiteSpace(migration.SqlScript))
        {
            if (string.IsNullOrWhiteSpace(_platformConnectionString))
            {
                return ApprovalResult.Failed(
                    "Platform connection string not configured. Cannot execute migration SQL.");
            }

            var execResult = await _migrationExecutor.ExecuteAsync(
                _platformConnectionString,
                migration.SqlScript,
                $"{migration.FromVersion}_to_{migration.ToVersion}",
                ct);

            if (!execResult.Success)
            {
                return ApprovalResult.Failed(
                    $"Migration SQL execution failed: {execResult.Error}");
            }
        }

        // Mark as executed only after successful SQL execution
        await _migrationRepository.MarkExecutedAsync(migrationId, executedBy, ct);

        return ApprovalResult.Success($"Migration executed successfully at {DateTime.UtcNow:O}");
    }
}

public class ApprovalResult
{
    public bool IsSuccess { get; private set; }
    public string Message { get; private set; } = "";
    public List<string> Warnings { get; } = new();

    public static ApprovalResult Success(string message)
    {
        return new ApprovalResult { IsSuccess = true, Message = message };
    }

    public static ApprovalResult Failed(string message)
    {
        return new ApprovalResult { IsSuccess = false, Message = message };
    }
}
