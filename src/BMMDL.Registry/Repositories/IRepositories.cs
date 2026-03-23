using BMMDL.Registry.Entities;

namespace BMMDL.Registry.Repositories;

/// <summary>
/// Repository for Module operations.
/// </summary>
public interface IModuleRepository
{
    Task<Module?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Module?> GetByNameAndVersionAsync(Guid tenantId, string name, string version, CancellationToken ct = default);
    Task<Module?> GetLatestVersionAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<Module>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<Module> CreateAsync(Module module, CancellationToken ct = default);
    Task UpdateAsync(Module module, CancellationToken ct = default);
    Task<bool> PublishAsync(Guid id, string approvedBy, CancellationToken ct = default);
}

/// <summary>
/// Repository for Migration operations.
/// </summary>
public interface IMigrationRepository
{
    Task<Migration?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Migration?> GetByVersionsAsync(Guid moduleId, string fromVersion, string toVersion, CancellationToken ct = default);
    Task<IReadOnlyList<Migration>> GetByModuleAsync(Guid moduleId, CancellationToken ct = default);
    Task<IReadOnlyList<Migration>> GetPendingAsync(Guid tenantId, CancellationToken ct = default);
    Task<Migration> CreateAsync(Migration migration, CancellationToken ct = default);
    Task<bool> ApproveAsync(Guid id, Guid approvedBy, CancellationToken ct = default);
    Task<bool> MarkExecutedAsync(Guid id, Guid executedBy, CancellationToken ct = default);
}

/// <summary>
/// Repository for ModuleInstallation operations.
/// Tracks installation order for safe uninstall.
/// </summary>
public interface IModuleInstallationRepository
{
    Task<ModuleInstallation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Get installation record for a specific module in a tenant.
    /// </summary>
    Task<ModuleInstallation?> GetByModuleAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default);
    
    /// <summary>
    /// Get all installed modules for a tenant (ordered by InstallOrder).
    /// </summary>
    Task<IReadOnlyList<ModuleInstallation>> GetInstalledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Get the next install order number for a tenant.
    /// </summary>
    Task<int> GetNextInstallOrderAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Check if a module can be safely uninstalled (no dependents).
    /// </summary>
    Task<bool> CanUninstallAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default);
    
    /// <summary>
    /// Get list of modules that depend on the specified module.
    /// </summary>
    Task<IReadOnlyList<Module>> GetDependentModulesAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default);
    
    Task<ModuleInstallation> CreateAsync(ModuleInstallation installation, CancellationToken ct = default);
    Task UpdateAsync(ModuleInstallation installation, CancellationToken ct = default);
}

