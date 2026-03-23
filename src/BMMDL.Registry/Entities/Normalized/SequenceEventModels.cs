namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// SEQUENCE & EVENT MODELS (3 tables)
// ============================================================

/// <summary>
/// Auto-numbering sequence definition.
/// </summary>
public class SequenceRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string? ForEntityName { get; set; }
    public Guid? ForEntityId { get; set; }
    public string? ForField { get; set; }
    public string? Pattern { get; set; }
    public int StartValue { get; set; } = 1;
    public int Increment { get; set; } = 1;
    public int? Padding { get; set; }
    public int? MaxValue { get; set; }
    public string Scope { get; set; } = "company"; // 'global', 'tenant', 'company'
    public string ResetOn { get; set; } = "never"; // 'never', 'daily', 'monthly', 'yearly', 'fiscal_year'
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public EntityRecord? ForEntity { get; set; }
    public SourceFile? SourceFile { get; set; }
}

/// <summary>
/// Domain event definition.
/// </summary>
public class EventRecord
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
    public ICollection<EventField> Fields { get; } = new List<EventField>();
}

/// <summary>
/// Field in a domain event.
/// </summary>
public class EventField
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public EventRecord Event { get; set; } = null!;
}
