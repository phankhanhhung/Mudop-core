namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// EXPRESSION AST NODE (1 table for all expression types)
// ============================================================

/// <summary>
/// Normalized AST node for expressions. 
/// Provides queryable structure alongside TEXT storage.
/// </summary>
public class ExpressionNode
{
    public Guid Id { get; set; }
    
    // Owner reference (polymorphic)
    public string OwnerType { get; set; } = ""; // 'entity_field', 'constraint', 'rule_statement', 'view'
    public Guid OwnerId { get; set; }
    public string OwnerField { get; set; } = ""; // 'computed_expr', 'default_value', 'condition', 'where_condition'
    
    // Node type discriminator
    public string NodeType { get; set; } = ""; // 'literal', 'identifier', 'binary', 'unary', 'function_call', etc.
    
    // Tree structure
    public Guid? ParentId { get; set; }
    public string? ParentRole { get; set; } // 'left', 'right', 'operand', 'condition', 'then', 'else', etc.
    public int Position { get; set; } // Order for list items (arguments, when clauses, etc.)
    
    // ============================================================
    // Type-specific fields (nullable, used based on NodeType)
    // ============================================================
    
    // Literal
    public string? LiteralKind { get; set; } // 'string', 'integer', 'decimal', 'boolean', 'null', 'enum_value'
    public string? LiteralValue { get; set; }
    
    // Identifier / Context Variable / Parameter
    public List<string> IdentifierPath { get; set; } = new(); // PostgreSQL array: ['entity', 'field'] or ['$now'] or [':param']
    
    // Binary / Unary operators
    public string? Operator { get; set; } // 'Add', 'Equal', 'And', 'Not', etc.
    
    // Function call
    public string? FunctionName { get; set; } // 'UPPER', 'SUBSTRING', 'COALESCE', etc.
    
    // Aggregate
    public string? AggregateFunction { get; set; } // 'Count', 'Sum', 'Avg', 'Min', 'Max'
    public bool IsDistinct { get; set; }
    
    // Modifiers for IN, BETWEEN, LIKE, IS NULL
    public bool IsNot { get; set; }
    
    // Cast
    public string? TargetType { get; set; }
    
    // Navigation
    public ExpressionNode? Parent { get; set; }
    public ICollection<ExpressionNode> Children { get; } = new List<ExpressionNode>();
}
