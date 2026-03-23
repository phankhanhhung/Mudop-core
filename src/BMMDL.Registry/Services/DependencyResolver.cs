using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;

namespace BMMDL.Registry.Services;

/// <summary>
/// Resolves module dependencies using semver matching.
/// </summary>
public class DependencyResolver
{
    private readonly IModuleRepository _moduleRepository;

    public DependencyResolver(IModuleRepository moduleRepository)
    {
        _moduleRepository = moduleRepository;
    }

    /// <summary>
    /// Resolve all dependencies for a module.
    /// Returns list of unresolved dependencies if any.
    /// </summary>
    public async Task<DependencyResult> ResolveAsync(Module module, CancellationToken ct = default)
    {
        var result = new DependencyResult();

        foreach (var dep in module.Dependencies)
        {
            var resolved = await ResolveDependencyAsync(module.TenantId, dep, ct);
            
            if (resolved != null)
            {
                dep.ResolvedId = resolved.Id;
                dep.IsCompatible = true;
                result.Resolved.Add((dep, resolved));
            }
            else
            {
                dep.IsCompatible = false;
                result.Unresolved.Add(dep);
            }
        }

        result.IsFullyResolved = result.Unresolved.Count == 0;
        return result;
    }

    private async Task<Module?> ResolveDependencyAsync(Guid tenantId, ModuleDependency dep, CancellationToken ct)
    {
        // Get all versions of the dependency
        var candidates = await GetModuleVersionsAsync(tenantId, dep.DependsOnName, ct);

        // Find the best matching version
        foreach (var candidate in candidates.OrderByDescending(m => m.VersionMajor)
                                            .ThenByDescending(m => m.VersionMinor)
                                            .ThenByDescending(m => m.VersionPatch))
        {
            if (VersionMatcher.Satisfies(candidate, dep.VersionRange))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<Module>> GetModuleVersionsAsync(Guid tenantId, string name, CancellationToken ct)
    {
        var modules = await _moduleRepository.GetByTenantAsync(tenantId, ct);
        return modules.Where(m => m.Name == name && m.Status == ModuleStatus.Published).ToList();
    }
}

public class DependencyResult
{
    public bool IsFullyResolved { get; set; }
    public List<(ModuleDependency Dependency, Module Resolved)> Resolved { get; } = new();
    public List<ModuleDependency> Unresolved { get; } = new();
}
