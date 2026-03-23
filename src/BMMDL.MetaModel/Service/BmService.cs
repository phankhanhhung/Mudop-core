using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;

namespace BMMDL.MetaModel.Service;

public class BmService : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    /// <summary>
    /// The entity this service is bound to (from FOR clause).
    /// Syntax: service MyService for MyEntity { ... }
    /// </summary>
    public string? ForEntity { get; set; }

    public List<BmEntity> Entities { get; } = new(); // Exposed entities
    public List<BmFunction> Functions { get; } = new();
    public List<BmAction> Actions { get; } = new();
    
    /// <summary>
    /// Event handlers this service subscribes to.
    /// </summary>
    public List<BmEventHandler> EventHandlers { get; } = new();
    
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmFunction : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    public string ReturnType { get; set; } = "";
    public List<BmParameter> Parameters { get; } = new();

    /// <summary>
    /// Body statements (for functions/actions with implementation).
    /// </summary>
    public List<BmRuleStatement> Body { get; } = new();

    /// <summary>
    /// OData v4: indicates that additional query options ($filter, $select, etc.)
    /// can be applied to the function result. Default false.
    /// </summary>
    public bool IsComposable { get; set; }

    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmAction : BmFunction
{
    /// <summary>
    /// Names of events this action can emit upon completion.
    /// </summary>
    public List<string> Emits { get; } = new();

    /// <summary>
    /// Precondition expressions (from 'requires' clauses).
    /// Must hold true before the action executes.
    /// </summary>
    public List<BmExpression> Preconditions { get; } = new();

    /// <summary>
    /// Postcondition expressions (from 'ensures' clauses).
    /// Must hold true after the action executes.
    /// </summary>
    public List<BmExpression> Postconditions { get; } = new();

    /// <summary>
    /// Field modification declarations (from 'modifies field = expr' clauses).
    /// Key: field name, Value: expression describing the modification.
    /// </summary>
    public List<(string FieldName, BmExpression Expression)> Modifies { get; } = new();
}

public class BmParameter : INamedElement
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    public string Type { get; set; } = "";
    
    /// <summary>
    /// Default value expression AST (from grammar: parameter ... DEFAULT expression).
    /// When non-null, this parameter is optional.
    /// </summary>
    public BmExpression? DefaultValueAst { get; set; }
    
    /// <summary>
    /// Raw default value expression text (for diagnostics).
    /// </summary>
    public string? DefaultValueString { get; set; }
    
    /// <summary>
    /// True when the parameter has a DEFAULT clause.
    /// </summary>
    public bool IsOptional => DefaultValueAst != null;
    
    /// <summary>
    /// Annotations on this parameter (e.g., @Required, @Description).
    /// </summary>
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

// ============================================================
// Event Handlers for Service-level Event Subscription
// ============================================================

/// <summary>
/// Event handler definition within a service.
/// Allows services to subscribe and react to domain events.
/// </summary>
public class BmEventHandler
{
    /// <summary>
    /// Name of the event to handle.
    /// </summary>
    public string EventName { get; set; } = "";
    
    /// <summary>
    /// Statements to execute when the event is received.
    /// </summary>
    public List<BmRuleStatement> Statements { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}
