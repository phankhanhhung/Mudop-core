namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// ASPECT MODELS (3 tables)
// ============================================================

/// <summary>
/// Aspect (mixin) definition.
/// </summary>
public class AspectRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<AspectInclude> Includes { get; } = new List<AspectInclude>();
    public ICollection<AspectField> Fields { get; } = new List<AspectField>();
}

/// <summary>
/// Reference to another aspect included in this aspect.
/// </summary>
public class AspectInclude
{
    public Guid AspectId { get; set; }
    public string IncludedAspectName { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public AspectRecord Aspect { get; set; } = null!;
}

/// <summary>
/// Field in an aspect.
/// </summary>
public class AspectField
{
    public Guid Id { get; set; }
    public Guid AspectId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public bool IsKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public int Position { get; set; }
    
    // Navigation
    public AspectRecord Aspect { get; set; } = null!;
}
