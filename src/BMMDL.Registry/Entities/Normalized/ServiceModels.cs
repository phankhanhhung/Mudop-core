namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// SERVICE MODELS (4 tables)
// ============================================================

/// <summary>
/// Service definition (API endpoint).
/// </summary>
public class ServiceRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string? ForEntityName { get; set; }  // Entity this service is bound to (FOR clause)
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<ServiceExposedEntity> ExposedEntities { get; } = new List<ServiceExposedEntity>();
    public ICollection<ServiceOperation> Operations { get; } = new List<ServiceOperation>();
    public ICollection<ServiceEventHandler> EventHandlers { get; } = new List<ServiceEventHandler>();
}

/// <summary>
/// Entity exposed by a service.
/// </summary>
public class ServiceExposedEntity
{
    public Guid ServiceId { get; set; }
    public string EntityName { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? IncludeFieldsJson { get; set; }  // JSON array of included field names
    public string? ExcludeFieldsJson { get; set; }  // JSON array of excluded field names

    // Navigation
    public ServiceRecord Service { get; set; } = null!;
    public EntityRecord? Entity { get; set; }
}

/// <summary>
/// Service operation (function or action).
/// </summary>
public class ServiceOperation
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public string OperationType { get; set; } = ""; // 'function', 'action'
    public string? ReturnType { get; set; }
    
    // Body storage (for unbound actions/functions with implementation)
    public string? BodyDefinitionHash { get; set; }  // SHA256 for change detection
    public Guid? BodyRootStatementId { get; set; }   // FK to statement_nodes
    
    // Action contract storage (preconditions, postconditions, modifies)
    // JSON arrays serialized as TEXT - expression ASTs stored in expression_nodes with owner_field prefixes
    public string? PreconditionExprIds { get; set; }   // JSON array of expression root IDs
    public string? PostconditionExprIds { get; set; }  // JSON array of expression root IDs
    public string? ModifiesJson { get; set; }          // JSON array of {FieldName, ExprRootId}
    
    public int Position { get; set; }
    
    // Navigation
    public ServiceRecord Service { get; set; } = null!;
    public ICollection<OperationParameter> Parameters { get; } = new List<OperationParameter>();
    public ICollection<ServiceOperationEmit> Emits { get; } = new List<ServiceOperationEmit>();
}

/// <summary>
/// Events emitted by a service operation (action).
/// </summary>
public class ServiceOperationEmit
{
    public Guid OperationId { get; set; }
    public string EventName { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public ServiceOperation Operation { get; set; } = null!;
}

/// <summary>
/// Parameter for a service operation.
/// </summary>
public class OperationParameter
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public string? DefaultValue { get; set; }          // Raw default expression text
    public Guid? DefaultValueExprRootId { get; set; }  // FK to expression_nodes for querying
    public int Position { get; set; }
    
    // Navigation
    public ServiceOperation Operation { get; set; } = null!;
}

/// <summary>
/// Event handler in a service - subscribes to domain events.
/// </summary>
public class ServiceEventHandler
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string EventName { get; set; } = "";
    
    /// <summary>
    /// SHA256 hash of the body definition for change detection.
    /// </summary>
    public string? BodyDefinitionHash { get; set; }
    
    /// <summary>
    /// FK to the root statement node of the body.
    /// </summary>
    public Guid? BodyRootStatementId { get; set; }
    
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public ServiceRecord Service { get; set; } = null!;
    public StatementNode? BodyRootStatement { get; set; }
}
