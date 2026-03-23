namespace BMMDL.Registry.Entities;

/// <summary>
/// Migration history between module versions.
/// </summary>
public class Migration
{
    public Guid Id { get; set; }
    
    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;
    
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public ChangeType ChangeType { get; set; }
    
    /// <summary>
    /// Model diff as JSON.
    /// </summary>
    public string DiffJson { get; set; } = "{}";
    
    /// <summary>
    /// Generated SQL migration script.
    /// </summary>
    public string? SqlScript { get; set; }
    
    /// <summary>
    /// Generated EF Core migration script.
    /// </summary>
    public string? EfCoreScript { get; set; }
    
    // Approval workflow
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public Guid? GeneratedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public Guid? ExecutedBy { get; set; }
}

public enum ChangeType
{
    Patch,      // Bug fixes, no schema changes
    Compatible, // Non-breaking additions
    Breaking    // Breaking changes
}
