namespace BMMDL.Registry.Entities;

/// <summary>
/// Dependency between modules with semver range.
/// </summary>
public class ModuleDependency
{
    public Guid Id { get; set; }
    
    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;
    
    public string DependsOnName { get; set; } = "";
    public string VersionRange { get; set; } = ""; // Semver range: ^1.0.0, >=2.0.0
    
    public Guid? ResolvedId { get; set; }
    public Module? ResolvedModule { get; set; }
    
    public bool? IsCompatible { get; set; }
}
