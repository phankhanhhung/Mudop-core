namespace BMMDL.Registry.Entities;

/// <summary>
/// Deprecation notice for a module version.
/// </summary>
public class ModuleDeprecation
{
    public Guid Id { get; set; }
    
    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;
    
    public string DeprecatedVersion { get; set; } = "";
    public string? Message { get; set; }
    public string? MigrateTo { get; set; }
    public DateTime? Deadline { get; set; }
}
