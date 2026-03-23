namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// ACCESS CONTROL MODELS (5 tables)
// ============================================================

/// <summary>
/// Access control definition for an entity.
/// </summary>
public class AccessControlRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }  // Multi-tenant support
    public Guid? ModuleId { get; set; }  // Link to owning module
    public string Name { get; set; } = "";
    public string TargetEntityName { get; set; } = "";
    public Guid? TargetEntityId { get; set; }
    public string? ExtendsFrom { get; set; }
    public Guid? SourceFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public EntityRecord? TargetEntity { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<AccessRule> Rules { get; } = new List<AccessRule>();
}

/// <summary>
/// Access rule (grant, deny, restrict).
/// </summary>
public class AccessRule
{
    public Guid Id { get; set; }
    public Guid AccessControlId { get; set; }
    public string RuleType { get; set; } = ""; // 'grant', 'deny', 'restrict_fields'
    public string? Scope { get; set; } // "Global", "Tenant", "Company"
    public string? WhereCondition { get; set; }   // TEXT for eval
    public Guid? WhereConditionExprRootId { get; set; } // FK to expression_nodes for querying
    public int Position { get; set; }
    
    // Navigation
    public AccessControlRecord AccessControl { get; set; } = null!;
    public ExpressionNode? WhereConditionExprRoot { get; set; }
    public ICollection<AccessRuleOperation> Operations { get; } = new List<AccessRuleOperation>();
    public ICollection<AccessRulePrincipal> Principals { get; } = new List<AccessRulePrincipal>();
    public ICollection<AccessFieldRestriction> FieldRestrictions { get; } = new List<AccessFieldRestriction>();
}

/// <summary>
/// Operation allowed/denied by an access rule.
/// </summary>
public class AccessRuleOperation
{
    public Guid RuleId { get; set; }
    public string Operation { get; set; } = ""; // 'read', 'create', 'update', 'delete'
    
    // Navigation
    public AccessRule Rule { get; set; } = null!;
}

/// <summary>
/// Principal (role/user) for an access rule.
/// </summary>
public class AccessRulePrincipal
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public string PrincipalType { get; set; } = ""; // 'role', 'user', 'authenticated', 'anonymous'
    public string? PrincipalValue { get; set; }
    
    // Navigation
    public AccessRule Rule { get; set; } = null!;
}

/// <summary>
/// Field-level access restriction.
/// </summary>
public class AccessFieldRestriction
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public string FieldName { get; set; } = "";
    public string AccessType { get; set; } = ""; // 'visible', 'masked', 'readonly', 'hidden'
    public string? Condition { get; set; }
    public string? MaskType { get; set; }
    public Guid? ConditionExprRootId { get; set; } // FK to expression_nodes for condition AST

    // Navigation
    public AccessRule Rule { get; set; } = null!;
    public ExpressionNode? ConditionExprRoot { get; set; }
}
