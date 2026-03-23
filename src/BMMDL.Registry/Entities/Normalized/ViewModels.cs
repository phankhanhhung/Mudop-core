namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// VIEW MODELS (2 tables)
// ============================================================

/// <summary>
/// Query view definition.
/// </summary>
public class ViewRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string SelectStatement { get; set; } = "";
    public bool IsProjection { get; set; }
    public string? ProjectionEntityName { get; set; }
    public string? ProjectionFieldsJson { get; set; }   // JSON array of {FieldName, Alias} objects
    public string? ExcludedFieldsJson { get; set; }      // JSON array of excluded field names
    public string? ParsedSelectJson { get; set; }          // L16: Serialized BmSelectStatement AST (structural parts only)
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<ViewParameter> Parameters { get; } = new List<ViewParameter>();
}

/// <summary>
/// View parameter.
/// </summary>
public class ViewParameter
{
    public Guid Id { get; set; }
    public Guid ViewId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public string? DefaultValue { get; set; }
    public int Position { get; set; }
    
    // Navigation
    public ViewRecord View { get; set; } = null!;
}
