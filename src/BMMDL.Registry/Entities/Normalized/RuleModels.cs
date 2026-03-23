namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// RULE MODELS (4 tables)
// ============================================================

/// <summary>
/// Business rule definition.
/// </summary>
public class RuleRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string TargetEntityName { get; set; } = "";
    public Guid? TargetEntityId { get; set; }
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public EntityRecord? TargetEntity { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<RuleTrigger> Triggers { get; } = new List<RuleTrigger>();
    public ICollection<RuleStatement> Statements { get; } = new List<RuleStatement>();
}

/// <summary>
/// Trigger event for a rule.
/// </summary>
public class RuleTrigger
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public string Timing { get; set; } = ""; // 'before', 'after', 'on_change'
    public string Operation { get; set; } = ""; // 'create', 'update', 'delete', 'read'
    public int Position { get; set; }
    
    // Navigation
    public RuleRecord Rule { get; set; } = null!;
    public ICollection<RuleTriggerField> ChangeFields { get; } = new List<RuleTriggerField>();
}

/// <summary>
/// Field monitored for changes in a trigger.
/// </summary>
public class RuleTriggerField
{
    public Guid TriggerId { get; set; }
    public string FieldName { get; set; } = "";
    
    // Navigation
    public RuleTrigger Trigger { get; set; } = null!;
}

/// <summary>
/// Statement in a rule (supports nesting for when/else).
/// </summary>
public class RuleStatement
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public Guid? ParentStatementId { get; set; } // For nested when/else
    public string StatementType { get; set; } = ""; // 'validate', 'compute', 'when', 'call'
    public string? Target { get; set; } // Field name (compute) or service name (call)
    public string? Expression { get; set; }   // TEXT for eval
    public Guid? ExpressionRootId { get; set; } // FK to expression_nodes for querying
    public string? Message { get; set; } // For validate
    public string? Severity { get; set; } // 'error', 'warning', 'info'
    public bool IsElseBranch { get; set; }
    public int Position { get; set; }
    
    // Navigation
    public RuleRecord Rule { get; set; } = null!;
    public RuleStatement? ParentStatement { get; set; }
    public ExpressionNode? ExpressionRoot { get; set; }
    public ICollection<RuleStatement> ChildStatements { get; } = new List<RuleStatement>();
}
