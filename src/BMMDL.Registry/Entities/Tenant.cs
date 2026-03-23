namespace BMMDL.Registry.Entities;

/// <summary>
/// Tenant for multi-tenancy isolation.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Subdomain { get; set; }
    public string Settings { get; set; } = "{}"; // JSONB
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public ICollection<Module> Modules { get; set; } = new List<Module>();
}
