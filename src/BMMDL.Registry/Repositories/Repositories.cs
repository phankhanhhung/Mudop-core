using Microsoft.EntityFrameworkCore;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;

namespace BMMDL.Registry.Repositories;

/// <summary>
/// EF Core implementation of IModuleRepository.
/// </summary>
public class ModuleRepository : IModuleRepository
{
    private readonly RegistryDbContext _context;

    public ModuleRepository(RegistryDbContext context)
    {
        _context = context;
    }

    public async Task<Module?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .Include(m => m.Dependencies)
            .Include(m => m.Deprecations)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Module?> GetByNameAndVersionAsync(Guid tenantId, string name, string version, CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == name && m.Version == version, ct);
    }

    public async Task<Module?> GetLatestVersionAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Name == name && m.Status == ModuleStatus.Published)
            .OrderByDescending(m => m.VersionMajor)
            .ThenByDescending(m => m.VersionMinor)
            .ThenByDescending(m => m.VersionPatch)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Module>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.Name)
            .ThenByDescending(m => m.VersionMajor)
            .ThenByDescending(m => m.VersionMinor)
            .ThenByDescending(m => m.VersionPatch)
            .ToListAsync(ct);
    }

    public async Task<Module> CreateAsync(Module module, CancellationToken ct = default)
    {
        module.Id = Guid.NewGuid();
        module.CreatedAt = DateTime.UtcNow;
        ParseVersion(module);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync(ct);
        return module;
    }

    public async Task UpdateAsync(Module module, CancellationToken ct = default)
    {
        ParseVersion(module);
        _context.Modules.Update(module);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> PublishAsync(Guid id, string approvedBy, CancellationToken ct = default)
    {
        var module = await _context.Modules.FindAsync(new object[] { id }, ct);
        if (module == null) return false;
        
        module.Status = ModuleStatus.Published;
        module.ApprovedBy = approvedBy;
        module.ApprovedAt = DateTime.UtcNow;
        module.PublishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private static void ParseVersion(Module module)
    {
        var parts = module.Version.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            module.VersionMajor = major;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            module.VersionMinor = minor;
        if (parts.Length >= 3 && int.TryParse(parts[2].Split('-')[0], out var patch))
            module.VersionPatch = patch;
    }
}

/// <summary>
/// EF Core implementation of IMigrationRepository.
/// </summary>
public class MigrationRepository : IMigrationRepository
{
    private readonly RegistryDbContext _context;

    public MigrationRepository(RegistryDbContext context)
    {
        _context = context;
    }

    public async Task<Migration?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Migrations
            .AsNoTracking()
            .Include(m => m.Module)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Migration?> GetByVersionsAsync(Guid moduleId, string fromVersion, string toVersion, CancellationToken ct = default)
    {
        return await _context.Migrations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ModuleId == moduleId && m.FromVersion == fromVersion && m.ToVersion == toVersion, ct);
    }

    public async Task<IReadOnlyList<Migration>> GetByModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        return await _context.Migrations
            .AsNoTracking()
            .Where(m => m.ModuleId == moduleId)
            .OrderByDescending(m => m.GeneratedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Migration>> GetPendingAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Migrations
            .AsNoTracking()
            .Include(m => m.Module)
            .Where(m => m.Module.TenantId == tenantId && m.ApprovedAt != null && m.ExecutedAt == null)
            .OrderBy(m => m.GeneratedAt)
            .ToListAsync(ct);
    }

    public async Task<Migration> CreateAsync(Migration migration, CancellationToken ct = default)
    {
        migration.Id = Guid.NewGuid();
        migration.GeneratedAt = DateTime.UtcNow;
        _context.Migrations.Add(migration);
        await _context.SaveChangesAsync(ct);
        return migration;
    }

    public async Task<bool> ApproveAsync(Guid id, Guid approvedBy, CancellationToken ct = default)
    {
        var migration = await _context.Migrations.FindAsync(new object[] { id }, ct);
        if (migration == null) return false;
        
        migration.ApprovedBy = approvedBy;
        migration.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> MarkExecutedAsync(Guid id, Guid executedBy, CancellationToken ct = default)
    {
        var migration = await _context.Migrations.FindAsync(new object[] { id }, ct);
        if (migration == null) return false;
        
        migration.ExecutedBy = executedBy;
        migration.ExecutedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// EF Core implementation of IModuleInstallationRepository.
/// </summary>
public class ModuleInstallationRepository : IModuleInstallationRepository
{
    private readonly RegistryDbContext _context;

    public ModuleInstallationRepository(RegistryDbContext context)
    {
        _context = context;
    }

    public async Task<ModuleInstallation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.ModuleInstallations
            .AsNoTracking()
            .Include(i => i.Module)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<ModuleInstallation?> GetByModuleAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default)
    {
        return await _context.ModuleInstallations
            .Include(i => i.Module)
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ModuleId == moduleId, ct);
    }

    public async Task<IReadOnlyList<ModuleInstallation>> GetInstalledAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.ModuleInstallations
            .AsNoTracking()
            .Include(i => i.Module)
            .Where(i => i.TenantId == tenantId && i.Status == InstallationStatus.Installed)
            .OrderBy(i => i.InstallOrder)
            .ToListAsync(ct);
    }

    public async Task<int> GetNextInstallOrderAsync(Guid tenantId, CancellationToken ct = default)
    {
        var maxOrder = await _context.ModuleInstallations
            .Where(i => i.TenantId == tenantId && i.Status == InstallationStatus.Installed)
            .MaxAsync(i => (int?)i.InstallOrder, ct);
        
        return (maxOrder ?? 0) + 1;
    }

    public async Task<bool> CanUninstallAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default)
    {
        var dependents = await GetDependentModulesAsync(tenantId, moduleId, ct);
        return dependents.Count == 0;
    }

    public async Task<IReadOnlyList<Module>> GetDependentModulesAsync(Guid tenantId, Guid moduleId, CancellationToken ct = default)
    {
        // Server-side filtering: only load modules that actually depend on the target module
        return await _context.ModuleInstallations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == InstallationStatus.Installed)
            .Where(i => i.Module!.Dependencies.Any(d => d.ResolvedId == moduleId))
            .Select(i => i.Module!)
            .ToListAsync(ct);
    }

    public async Task<ModuleInstallation> CreateAsync(ModuleInstallation installation, CancellationToken ct = default)
    {
        installation.Id = Guid.NewGuid();
        _context.ModuleInstallations.Add(installation);
        await _context.SaveChangesAsync(ct);
        return installation;
    }

    public async Task UpdateAsync(ModuleInstallation installation, CancellationToken ct = default)
    {
        _context.ModuleInstallations.Update(installation);
        await _context.SaveChangesAsync(ct);
    }
}
