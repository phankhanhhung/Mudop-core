namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// STATEMENT AST NODE (1 table for all statement types)
// ============================================================

/// <summary>
/// Normalized AST node for action/function body statements.
/// Similar pattern to ExpressionNode for expressions.
/// </summary>
public class StatementNode
{
    public Guid Id { get; set; }
    
    // Owner reference (polymorphic)
    public string OwnerType { get; set; } = "";  // 'operation', 'when_then', 'when_else', 'foreach'
    public Guid OwnerId { get; set; }
    
    // Node type discriminator
    public string NodeType { get; set; } = "";  // 'validate', 'compute', 'let', 'emit', 'return', 'raise', 'when', 'foreach', 'call'
    
    // Tree structure
    public Guid? ParentId { get; set; }
    public string? ParentRole { get; set; }  // 'body', 'then', 'else'
    public int Position { get; set; }
    
    // ============================================================
    // Type-specific fields (nullable, used based on NodeType)
    // ============================================================
    
    // validate/compute: target field name
    public string? TargetField { get; set; }
    
    // validate: error message
    public string? Message { get; set; }
    
    // Expression references (FK to expression_nodes root)
    public Guid? ConditionExprRootId { get; set; }  // validate condition, when condition
    public Guid? ValueExprRootId { get; set; }      // compute value, let value, return value
    
    // let: variable name
    public string? VariableName { get; set; }
    
    // emit: event name
    public string? EventName { get; set; }
    
    // raise: message and severity
    public string? Severity { get; set; }  // 'error', 'warning'
    
    // foreach: iterator variable and collection expression
    public string? IteratorVariable { get; set; }
    public Guid? CollectionExprRootId { get; set; }
    
    // call: target action/function
    public string? CallTarget { get; set; }
    
    // Navigation
    public StatementNode? Parent { get; set; }
    public ExpressionNode? ConditionExprRoot { get; set; }
    public ExpressionNode? ValueExprRoot { get; set; }
    public ExpressionNode? CollectionExprRoot { get; set; }
    public ICollection<StatementNode> Children { get; } = new List<StatementNode>();
}
