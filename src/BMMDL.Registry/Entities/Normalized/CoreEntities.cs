namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// CORE ENTITIES (3 tables)
// ============================================================

/// <summary>
/// Namespace registry for organizing model elements.
/// </summary>
public class Namespace
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<EntityRecord> Entities { get; } = new List<EntityRecord>();
    public ICollection<TypeRecord> Types { get; } = new List<TypeRecord>();
    public ICollection<EnumRecord> Enums { get; } = new List<EnumRecord>();
    public ICollection<AspectRecord> Aspects { get; } = new List<AspectRecord>();
    public ICollection<ServiceRecord> Services { get; } = new List<ServiceRecord>();
    public ICollection<ViewRecord> Views { get; } = new List<ViewRecord>();
    public ICollection<EventRecord> Events { get; } = new List<EventRecord>();
}

/// <summary>
/// Tracking for compiled source files.
/// </summary>
public class SourceFile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public string FilePath { get; set; } = "";
    public DateTime CompiledAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
}

/// <summary>
/// Generic annotation storage for any model element.
/// </summary>
public class NormalizedAnnotation
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = ""; // 'entity', 'field', 'service', etc.
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string? Value { get; set; }
}
