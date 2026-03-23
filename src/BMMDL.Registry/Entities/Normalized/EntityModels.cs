namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// ENTITY MODELS (8 tables)
// ============================================================

/// <summary>
/// Business entity definition.
/// </summary>
public class EntityRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public Guid? SourceFileId { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Multi-tenancy support
    public bool IsTenantScoped { get; set; }  // true = filter by current tenant_id

    // Inheritance support
    public bool IsAbstract { get; set; }
    public string? ParentEntityName { get; set; }  // Qualified name of parent entity (extends)
    public string? DiscriminatorValue { get; set; }  // Value for _discriminator column

    // Cross-module extension support
    public string? ExtendsFrom { get; set; }  // Qualified name of entity being extended (extend entity X)
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<EntityField> Fields { get; } = new List<EntityField>();
    public ICollection<EntityAssociation> Associations { get; } = new List<EntityAssociation>();
    public ICollection<EntityAspectRef> AspectRefs { get; } = new List<EntityAspectRef>();
    public ICollection<EntityIndex> Indexes { get; } = new List<EntityIndex>();
    public ICollection<EntityConstraint> Constraints { get; } = new List<EntityConstraint>();
    public ICollection<NormalizedAnnotation> Annotations { get; } = new List<NormalizedAnnotation>();
    public ICollection<EntityBoundOperation> BoundOperations { get; } = new List<EntityBoundOperation>();
}

/// <summary>
/// Entity field definition.
/// </summary>
public class EntityField
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public bool IsKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsVirtual { get; set; }
    public bool IsReadonly { get; set; }
    public bool IsImmutable { get; set; }
    public bool IsComputed { get; set; }
    public bool IsStored { get; set; }
    public string? ComputedStrategy { get; set; } // "Virtual", "Application", "Stored", "Trigger"
    public string? ComputedExpr { get; set; }   // TEXT for eval
    public Guid? ComputedExprRootId { get; set; } // FK to expression_nodes for querying
    public string? DefaultValue { get; set; }   // TEXT for eval
    public Guid? DefaultValueExprRootId { get; set; } // FK to expression_nodes for querying
    public int Position { get; set; }
    
    // Navigation
    public EntityRecord Entity { get; set; } = null!;
    public ExpressionNode? ComputedExprRoot { get; set; }
    public ExpressionNode? DefaultValueExprRoot { get; set; }
}

/// <summary>
/// Entity association (including compositions).
/// </summary>
public class EntityAssociation
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Name { get; set; } = "";
    public string TargetEntityName { get; set; } = "";
    public Guid? TargetEntityId { get; set; }
    public string? OnCondition { get; set; }   // TEXT for eval
    public Guid? OnConditionExprRootId { get; set; } // FK to expression_nodes for querying
    public bool IsComposition { get; set; }
    public int Cardinality { get; set; } // BmCardinality enum as int
    public int MinCardinality { get; set; }
    public int MaxCardinality { get; set; } = 1;
    public string? OnDeleteAction { get; set; } // DeleteAction enum as string

    // Navigation
    public EntityRecord Entity { get; set; } = null!;
    public EntityRecord? TargetEntity { get; set; }
    public ExpressionNode? OnConditionExprRoot { get; set; }
}

/// <summary>
/// Reference to an aspect included in an entity.
/// </summary>
public class EntityAspectRef
{
    public Guid EntityId { get; set; }
    public string AspectName { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public EntityRecord Entity { get; set; } = null!;
}

/// <summary>
/// Entity index definition.
/// </summary>
public class EntityIndex
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Name { get; set; } = "";
    public bool IsUnique { get; set; }
    public string? Expression { get; set; } // Functional index expression

    // Navigation
    public EntityRecord Entity { get; set; } = null!;
    public ICollection<EntityIndexField> Fields { get; } = new List<EntityIndexField>();
}

/// <summary>
/// Field in an entity index.
/// </summary>
public class EntityIndexField
{
    public Guid IndexId { get; set; }
    public string FieldName { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public EntityIndex Index { get; set; } = null!;
}

/// <summary>
/// Entity constraint (check, unique, foreign key).
/// </summary>
public class EntityConstraint
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Name { get; set; } = "";
    public string ConstraintType { get; set; } = ""; // 'check', 'unique', 'foreign_key'
    public string? ConditionExpr { get; set; }   // TEXT for eval (check constraints)
    public Guid? ConditionExprRootId { get; set; } // FK to expression_nodes for querying
    public string? ReferencedEntity { get; set; } // For FK
    
    // Navigation
    public EntityRecord Entity { get; set; } = null!;
    public ExpressionNode? ConditionExprRoot { get; set; }
    public ICollection<EntityConstraintField> Fields { get; } = new List<EntityConstraintField>();
    public ICollection<EntityConstraintField> ReferencedFields { get; } = new List<EntityConstraintField>();
}

/// <summary>
/// Field in an entity constraint.
/// </summary>
public class EntityConstraintField
{
    public Guid ConstraintId { get; set; }
    public string FieldName { get; set; } = "";
    public int Position { get; set; }
    public bool IsReferenced { get; set; } // true = referenced field, false = source field
    
    // Navigation
    public EntityConstraint Constraint { get; set; } = null!;
}
