using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;
using BMMDL.MetaModel;
using Microsoft.Extensions.Logging;

namespace BMMDL.Registry.Services;

/// <summary>
/// Implementation of module installation service.
/// Note: Compilation and consistency checking are done by CLI.
/// This service only handles saving pre-validated models to DB.
/// </summary>
public class ModuleInstallationService : IModuleInstallationService
{
    private readonly RegistryDbContext _db;
    private readonly IModuleRepository _moduleRepo;
    private readonly IModuleInstallationRepository _installRepo;
    private readonly DependencyResolver _depResolver;
    private readonly ILogger<ModuleInstallationService> _logger;

    public ModuleInstallationService(
        RegistryDbContext db,
        IModuleRepository moduleRepo,
        IModuleInstallationRepository installRepo,
        DependencyResolver depResolver,
        ILogger<ModuleInstallationService> logger)
    {
        _db = db;
        _moduleRepo = moduleRepo;
        _installRepo = installRepo;
        _depResolver = depResolver;
        _logger = logger;
    }

    public async Task<InstallResult> InstallModuleAsync(
        Guid tenantId,
        BmModel model,
        string sourceHash,
        string installedBy,
        CancellationToken ct = default)
    {
        // Use transaction to ensure atomicity of module + installation record creation
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _logger.LogInformation("Starting module installation for tenant {TenantId}", tenantId);
            
            // 1. Validate module declaration exists
            if (model.Module == null)
            {
                return InstallResult.Failed(
                    "No module declaration found in compiled model.");
            }
            
            var moduleDecl = model.Module;
            
            // 4. Check if already installed (exact same version)
            var existingInstall = await _installRepo.GetByModuleAsync(tenantId, Guid.Empty, ct);
            // Note: We need to find by name since we don't have module ID yet
            
            var existingModule = await _moduleRepo.GetByNameAndVersionAsync(
                tenantId, moduleDecl.Name, moduleDecl.Version, ct);
            
            if (existingModule != null)
            {
                var installation = await _installRepo.GetByModuleAsync(tenantId, existingModule.Id, ct);
                if (installation?.Status == InstallationStatus.Installed)
                {
                    // Idempotent: update the existing installation record on recompile
                    installation.InstalledAt = DateTime.UtcNow;
                    installation.InstalledBy = installedBy;
                    installation.SourceHash = sourceHash;
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return InstallResult.Succeeded(installation,
                        $"Module '{moduleDecl.Name}' version '{moduleDecl.Version}' re-installed (updated).");
                }
            }
            
            // ╔══════════════════════════════════════════════════════════════════╗
            // ║  🚨 VERSION DOWNGRADE PROTECTION 🚨                              ║
            // ║  Prevents installing older versions over newer ones!             ║
            // ║  This is a SECURITY measure to prevent rollback attacks.         ║
            // ╚══════════════════════════════════════════════════════════════════╝
            var latestModule = await _moduleRepo.GetLatestVersionAsync(tenantId, moduleDecl.Name, ct);
            if (latestModule != null)
            {
                var incomingVersion = VersionParser.Parse(moduleDecl.Version);
                var existingVersion = VersionParser.Parse(latestModule.Version);
                
                if (incomingVersion <= existingVersion)
                {
                    _logger.LogWarning(
                        "⚠️⚠️⚠️ VERSION DOWNGRADE BLOCKED! ⚠️⚠️⚠️ " +
                        "Attempted to install {ModuleName} v{IncomingVersion} but v{ExistingVersion} already exists. " +
                        "TenantId={TenantId}. If you need to downgrade, uninstall the newer version first!",
                        moduleDecl.Name, moduleDecl.Version, latestModule.Version, tenantId);
                    
                    return InstallResult.Failed(
                        $"🚨 VERSION DOWNGRADE NOT ALLOWED! 🚨\n" +
                        $"Module '{moduleDecl.Name}' version '{moduleDecl.Version}' is OLDER OR EQUAL to existing version '{latestModule.Version}'.\n" +
                        $"You must use a version GREATER than '{latestModule.Version}'.\n" +
                        $"If you really need to downgrade, uninstall the current version first.");
                }
                
                // ╔══════════════════════════════════════════════════════════════════╗
                // ║  🚨 EMPTY VERSION BUMP DETECTION 🚨                              ║
                // ║  Blocks version bump without actual content changes!             ║
                // ╚══════════════════════════════════════════════════════════════════╝
                var latestInstall = await _installRepo.GetByModuleAsync(tenantId, latestModule.Id, ct);
                if (latestInstall?.SourceHash != null && latestInstall.SourceHash == sourceHash)
                {
                    _logger.LogWarning(
                        "🚫🚫🚫 EMPTY VERSION BUMP BLOCKED! 🚫🚫🚫 " +
                        "Module {ModuleName} v{NewVersion} has IDENTICAL content to v{OldVersion} (hash={Hash}). " +
                        "TenantId={TenantId}. You cannot bump version without making actual changes!",
                        moduleDecl.Name, moduleDecl.Version, latestModule.Version, sourceHash, tenantId);
                    
                    return InstallResult.Failed(
                        $"🚫 EMPTY VERSION BUMP NOT ALLOWED! 🚫\n" +
                        $"Module '{moduleDecl.Name}' version '{moduleDecl.Version}' has IDENTICAL content to existing version '{latestModule.Version}'.\n" +
                        $"Content hash: {sourceHash}\n" +
                        $"You cannot bump version number without making actual changes to entities/fields/rules.\n" +
                        $"Either make real changes or keep the same version number.");
                }
                
                _logger.LogInformation(
                    "Version upgrade validated: {ModuleName} {OldVersion} → {NewVersion}",
                    moduleDecl.Name, latestModule.Version, moduleDecl.Version);
            }
            
            // 5. Check dependencies are installed
            foreach (var dep in moduleDecl.Dependencies)
            {
                var depModule = await _moduleRepo.GetLatestVersionAsync(tenantId, dep.ModuleName, ct);
                if (depModule == null)
                {
                    return InstallResult.Failed(
                        $"Dependency '{dep.ModuleName}' is not installed. Install it first.");
                }

                var depInstall = await _installRepo.GetByModuleAsync(tenantId, depModule.Id, ct);
                if (depInstall?.Status != InstallationStatus.Installed)
                {
                    return InstallResult.Failed(
                        $"Dependency '{dep.ModuleName}' is not in installed state.");
                }

                // Validate version range
                if (!string.IsNullOrEmpty(dep.VersionRange))
                {
                    if (!VersionMatcher.Satisfies(depModule, dep.VersionRange))
                    {
                        return InstallResult.Failed(
                            $"Dependency '{dep.ModuleName}' version '{depModule.Version}' does not satisfy required range '{dep.VersionRange}'.");
                    }
                }
            }
            
            // Note: Consistency check is done by CLI before calling this method
            // CLI fetches existing model, runs ModuleConsistencyChecker, then calls install
            
            // 7. Create Module record
            var module = new Module
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = moduleDecl.Name,
                Version = moduleDecl.Version,
                Description = moduleDecl.Description,
                Author = moduleDecl.Author ?? installedBy,
                Status = ModuleStatus.Published,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow,
                // L20: Persist publishes/imports/tenant-aware
                TenantAware = moduleDecl.TenantAware,
                PublishesJson = moduleDecl.Publishes.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(moduleDecl.Publishes) : null,
                ImportsJson = moduleDecl.Imports.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(moduleDecl.Imports) : null
            };
            
            await _moduleRepo.CreateAsync(module, ct);
            
            // 8. Create installation record
            var installOrder = await _installRepo.GetNextInstallOrderAsync(tenantId, ct);
            
            var installation2 = new ModuleInstallation
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                InstallOrder = installOrder,
                Status = InstallationStatus.Installing,
                InstalledAt = DateTime.UtcNow,
                InstalledBy = installedBy,
                EntityCount = model.Entities.Count,
                TypeCount = model.Types.Count,
                EnumCount = model.Enums.Count,
                ServiceCount = model.Services.Count,
                SourceHash = sourceHash
            };
            
            await _installRepo.CreateAsync(installation2, ct);
            
            // Model elements are persisted by DbPersistenceService.PublishAsync (called by AdminService
            // before InstallModuleAsync). This service only manages the installation record lifecycle.
            installation2.Status = InstallationStatus.Installed;
            await _installRepo.UpdateAsync(installation2, ct);
            
            _logger.LogInformation(
                "Module {ModuleName} v{Version} installed successfully with order {InstallOrder}",
                module.Name, module.Version, installOrder);

            // Commit transaction after all operations succeed
            await transaction.CommitAsync(ct);

            return InstallResult.Succeeded(installation2,
                $"Module '{moduleDecl.Name}' version '{moduleDecl.Version}' installed successfully as #{installOrder}");
        }
        catch (Exception ex)
        {
            // Rollback transaction on any failure
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Module installation failed for tenant {TenantId}", tenantId);
            return InstallResult.Failed($"Installation failed: {ex.Message}");
        }
    }

    public async Task<InstallResult> InstallPublishedModuleAsync(
        Guid tenantId,
        Guid moduleId,
        string installedBy,
        CancellationToken ct = default)
    {
        var module = await _moduleRepo.GetByIdAsync(moduleId, ct);
        if (module == null)
        {
            return InstallResult.Failed($"Module with ID {moduleId} not found.");
        }
        
        if (module.Status != ModuleStatus.Published)
        {
            return InstallResult.Failed($"Module '{module.Name}' is not published.");
        }
        
        // Check not already installed
        var existingInstall = await _installRepo.GetByModuleAsync(tenantId, moduleId, ct);
        if (existingInstall?.Status == InstallationStatus.Installed)
        {
            return InstallResult.Failed($"Module '{module.Name}' is already installed.");
        }
        
        // Check dependencies
        var depResult = await _depResolver.ResolveAsync(module);
        if (!depResult.IsFullyResolved)
        {
            var missing = string.Join(", ", depResult.Unresolved.Select(d => d.DependsOnName));
            return InstallResult.Failed($"Unresolved dependencies: {missing}");
        }
        
        // Create installation record
        var installOrder = await _installRepo.GetNextInstallOrderAsync(tenantId, ct);
        
        var installation = new ModuleInstallation
        {
            TenantId = tenantId,
            ModuleId = moduleId,
            InstallOrder = installOrder,
            Status = InstallationStatus.Installed,
            InstalledAt = DateTime.UtcNow,
            InstalledBy = installedBy
        };
        
        await _installRepo.CreateAsync(installation, ct);
        
        return InstallResult.Succeeded(installation, 
            $"Module '{module.Name}' version '{module.Version}' installed as #{installOrder}");
    }

    public async Task<UninstallResult> UninstallModuleAsync(
        Guid tenantId,
        Guid moduleId,
        string uninstalledBy,
        CancellationToken ct = default)
    {
        // Check can uninstall
        var canUninstall = await CanUninstallAsync(tenantId, moduleId, ct);
        if (!canUninstall.CanUninstall)
        {
            return UninstallResult.Failed(canUninstall.Reason, canUninstall.BlockingModules);
        }

        var installation = await _installRepo.GetByModuleAsync(tenantId, moduleId, ct);
        if (installation == null)
        {
            return UninstallResult.Failed("Module is not installed.");
        }

        _logger.LogInformation(
            "Uninstalling module {ModuleId} from tenant {TenantId}",
            moduleId, tenantId);

        // Use transaction to ensure atomicity of status updates
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Mark as uninstalling
            installation.Status = InstallationStatus.Uninstalling;
            await _installRepo.UpdateAsync(installation, ct);

            // Elements are cascade-deleted via FK constraint (ModuleId ON DELETE CASCADE)
            // No manual deletion needed
            _logger.LogInformation("Module uninstalled, elements cascade-deleted via FK");

            // Mark as uninstalled
            installation.Status = InstallationStatus.Uninstalled;
            installation.UninstalledAt = DateTime.UtcNow;
            installation.UninstalledBy = uninstalledBy;
            await _installRepo.UpdateAsync(installation, ct);

            await transaction.CommitAsync(ct);

            return UninstallResult.Succeeded(
                $"Module uninstalled successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Uninstall failed for module {ModuleId}", moduleId);

            // Try to mark as failed (outside transaction since main work failed)
            try
            {
                installation.Status = InstallationStatus.Failed;
                installation.ErrorMessage = ex.Message;
                await _installRepo.UpdateAsync(installation, ct);
            }
            catch
            {
                // Ignore failure to update status - main error is more important
            }

            return UninstallResult.Failed($"Uninstall failed: {ex.Message}");
        }
    }

    public async Task<UninstallCheckResult> CanUninstallAsync(
        Guid tenantId,
        Guid moduleId,
        CancellationToken ct = default)
    {
        var installation = await _installRepo.GetByModuleAsync(tenantId, moduleId, ct);
        if (installation == null)
        {
            return new UninstallCheckResult(false, "Module is not installed.", null);
        }
        
        if (installation.Status != InstallationStatus.Installed)
        {
            return new UninstallCheckResult(false, 
                $"Module is in '{installation.Status}' state, cannot uninstall.", null);
        }
        
        // Check for dependent modules
        var dependents = await _installRepo.GetDependentModulesAsync(tenantId, moduleId, ct);
        if (dependents.Count > 0)
        {
            var names = dependents.Select(m => $"{m.Name} v{m.Version}").ToList();
            return new UninstallCheckResult(false, 
                "Cannot uninstall: other modules depend on this module.", names);
        }
        
        return new UninstallCheckResult(true, "Module can be safely uninstalled.", null);
    }

    public async Task<IReadOnlyList<ModuleInstallation>> GetInstallationHistoryAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        return await _installRepo.GetInstalledAsync(tenantId, ct);
    }
}
