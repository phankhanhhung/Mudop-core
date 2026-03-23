namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// TYPE & ENUM MODELS (4 tables)
// ============================================================

/// <summary>
/// Custom type definition.
/// </summary>
public class TypeRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string? BaseType { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<TypeField> Fields { get; } = new List<TypeField>();
}

/// <summary>
/// Field in a struct type.
/// </summary>
public class TypeField
{
    public Guid Id { get; set; }
    public Guid TypeId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public bool IsNullable { get; set; } = true;
    public string? DefaultValue { get; set; }
    public int Position { get; set; }
    
    // Navigation
    public TypeRecord Type { get; set; } = null!;
}

/// <summary>
/// Enumeration type definition.
/// </summary>
public class EnumRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string? BaseType { get; set; } // 'int', 'string'
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<EnumValue> Values { get; } = new List<EnumValue>();
}

/// <summary>
/// Enum value.
/// </summary>
public class EnumValue
{
    public Guid Id { get; set; }
    public Guid EnumId { get; set; }
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public int Position { get; set; }
    
    // Navigation
    public EnumRecord Enum { get; set; } = null!;
}
