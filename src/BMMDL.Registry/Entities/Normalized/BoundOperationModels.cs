namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// BOUND OPERATION MODELS (Operations on entities)
// ============================================================

/// <summary>
/// Bound action or function on an entity.
/// </summary>
public class EntityBoundOperation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EntityId { get; set; }
    public Guid? ModuleId { get; set; }
    
    public string Name { get; set; } = "";
    public string OperationType { get; set; } = "";  // 'action' | 'function'
    public string? ReturnType { get; set; }
    
    /// <summary>
    /// SHA256 hash of the body definition for change detection.
    /// </summary>
    public string? BodyDefinitionHash { get; set; }
    
    /// <summary>
    /// FK to the root statement node of the body.
    /// </summary>
    public Guid? BodyRootStatementId { get; set; }

    // Action contract storage (preconditions, postconditions, modifies)
    // JSON arrays serialized as TEXT - expression ASTs stored in expression_nodes with owner_field prefixes
    public string? PreconditionExprIds { get; set; }   // JSON array of expression root IDs
    public string? PostconditionExprIds { get; set; }  // JSON array of expression root IDs
    public string? ModifiesJson { get; set; }          // JSON array of {FieldName, ExprRootId}

    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public EntityRecord Entity { get; set; } = null!;
    public Module? Module { get; set; }
    public StatementNode? BodyRootStatement { get; set; }
    public ICollection<BoundOperationParameter> Parameters { get; } = new List<BoundOperationParameter>();
    public ICollection<BoundOperationEmit> Emits { get; } = new List<BoundOperationEmit>();
}

/// <summary>
/// Parameter for a bound operation.
/// </summary>
public class BoundOperationParameter
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public EntityBoundOperation Operation { get; set; } = null!;
}

/// <summary>
/// Event emitted by a bound action (signature-level emits clause).
/// </summary>
public class BoundOperationEmit
{
    public Guid OperationId { get; set; }
    public string EventName { get; set; } = "";
    public int Position { get; set; }
    
    // Navigation
    public EntityBoundOperation Operation { get; set; } = null!;
}
