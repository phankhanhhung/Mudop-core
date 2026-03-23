using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Features;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;

namespace BMMDL.MetaModel.Structure;

public class BmEntity : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    
    /// <summary>
    /// If this entity extends another entity (from another module).
    /// Used for cross-module extensions (extend entity X { ... }).
    /// </summary>
    public string? ExtendsFrom { get; set; }
    
    /// <summary>
    /// Indicates this is an extension definition (extend entity X).
    /// </summary>
    public bool IsExtension => ExtendsFrom != null;
    
    /// <summary>
    /// Whether this entity is declared as abstract (cannot be instantiated directly).
    /// </summary>
    public bool IsAbstract { get; set; }
    
    /// <summary>
    /// Name of the parent entity for table-per-type inheritance.
    /// Set when: entity Child extends Parent { ... }
    /// </summary>
    public string? ParentEntityName { get; set; }
    
    /// <summary>
    /// Resolved reference to the parent entity (populated by InheritanceResolutionPass).
    /// </summary>
    public BmEntity? ParentEntity { get; set; }
    
    /// <summary>
    /// Discriminator value for this entity type (defaults to entity name).
    /// </summary>
    public string? DiscriminatorValue { get; set; }
    
    /// <summary>
    /// List of entities that inherit from this entity.
    /// </summary>
    public List<BmEntity> DerivedEntities { get; } = new();
    
    public List<BmField> Fields { get; } = new();
    public List<BmAssociation> Associations { get; } = new();
    public List<BmComposition> Compositions { get; } = new();
    public List<string> Aspects { get; } = new(); // Names of aspects mixed in

    // Phase 7: Indexes and Constraints
    public List<BmIndex> Indexes { get; } = new();
    public List<BmConstraint> Constraints { get; } = new();
    
    // Phase 8: Bound Actions/Functions (OData v4)
    /// <summary>
    /// Actions bound to this entity (invoked on specific entity instance).
    /// Route: POST /api/odata/Module/Entity(id)/ActionName
    /// </summary>
    public List<BMMDL.MetaModel.Service.BmAction> BoundActions { get; } = new();
    
    /// <summary>
    /// Functions bound to this entity (invoked on specific entity instance).
    /// Route: GET /api/odata/Module/Entity(id)/FunctionName()
    /// </summary>
    public List<BMMDL.MetaModel.Service.BmFunction> BoundFunctions { get; } = new();

    /// <summary>
    /// Indicates if this entity is tenant-scoped.
    /// When true, the entity automatically enforces tenant isolation.
    /// Can be set explicitly via @TenantScoped annotation or inherited from module.
    /// </summary>
    public bool TenantScoped { get; set; } = false;

    // ============================================================
    // Phase 8: Temporal/Bitemporal Support
    // ============================================================
    
    /// <summary>
    /// Indicates if this entity has @Temporal annotation (transaction-time versioning).
    /// </summary>
    public bool IsTemporal => HasAnnotation("Temporal");
    
    /// <summary>
    /// Indicates if this entity has @Temporal.ValidTime annotation (valid-time tracking).
    /// </summary>
    public bool HasValidTime => HasAnnotation("Temporal.ValidTime");
    
    /// <summary>
    /// Indicates if this entity is fully bitemporal (both transaction and valid time).
    /// </summary>
    public bool IsBitemporal => IsTemporal && HasValidTime;
    
    /// <summary>
    /// Table organization strategy for temporal data.
    /// Default is InlineHistory unless overridden with @Temporal(strategy: 'separate')
    /// </summary>
    public TemporalStrategy TemporalStrategy
    {
        get
        {
            var annotation = GetAnnotation("Temporal");
            var strategy = annotation?.GetValue("strategy")?.ToString();
            return strategy?.ToLowerInvariant() == "separate" 
                ? TemporalStrategy.SeparateTables 
                : TemporalStrategy.InlineHistory;
        }
    }
    
    /// <summary>
    /// Get the valid-time 'from' column name from @Temporal.ValidTime annotation.
    /// </summary>
    public string? ValidTimeFromColumn => GetAnnotation("Temporal.ValidTime")?.GetValue("from")?.ToString();
    
    /// <summary>
    /// Get the valid-time 'to' column name from @Temporal.ValidTime annotation.
    /// </summary>
    public string? ValidTimeToColumn => GetAnnotation("Temporal.ValidTime")?.GetValue("to")?.ToString();

    // ============================================================
    // HasStream: Entity-level Media Resource (OData v4)
    // ============================================================

    /// <summary>
    /// Indicates if this entity is a media entity (HasStream).
    /// When true, the entity supports binary media content via GET/PUT/DELETE $value.
    /// </summary>
    public bool HasStream => HasAnnotation("HasStream");

    /// <summary>
    /// Maximum media size in bytes. Default 10MB.
    /// </summary>
    public long MaxMediaSize
    {
        get
        {
            var annotation = GetAnnotation("HasStream");
            if (annotation?.Properties != null)
            {
                foreach (var prop in annotation.Properties)
                {
                    if (prop.Key.Equals("maxSize", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(prop.Value?.ToString(), out var size))
                        return size;
                }
            }
            return 10_485_760; // 10MB default
        }
    }


    // ============================================================
    // Service Projection (A5): Fields exposed through service entity
    // ============================================================

    /// <summary>
    /// Explicit list of fields to include when this entity is exposed through a service projection.
    /// Null means all fields are included (no projection or wildcard *).
    /// </summary>
    public List<string>? IncludeFields { get; set; }

    /// <summary>
    /// List of fields to exclude when this entity is exposed through a service projection with '* excluding { ... }'.
    /// </summary>
    public List<string>? ExcludeFields { get; set; }

    // ============================================================
    // Plugin Architecture: Feature Metadata
    // ============================================================

    /// <summary>
    /// Feature metadata contributed by platform features at compile time.
    /// Keyed by feature name (e.g., "TenantIsolation", "Temporal", "SoftDelete").
    /// Ordering is guaranteed by the feature registry's dependency resolution.
    /// </summary>
    public Dictionary<string, BmFeatureMetadata> Features { get; } = new();

    public List<BmAnnotation> Annotations { get; } = new();

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name)
        => Annotations.FirstOrDefault(a => a.Name == name);

    public bool HasAnnotation(string name)
        => Annotations.Any(a => a.Name == name);
}

/// <summary>
/// Table organization strategy for temporal entities.
/// </summary>
public enum TemporalStrategy
{
    /// <summary>
    /// All versions stored in single table, distinguished by system_end='infinity' for current.
    /// This is the DEFAULT strategy.
    /// </summary>
    InlineHistory,
    
    /// <summary>
    /// Current data in main table, old versions in separate {table}_history table.
    /// </summary>
    SeparateTables
}

public class BmField : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name; 
    /// <summary>
    /// Type reference as string (legacy, for compatibility).
    /// </summary>
    public string TypeString { get; set; } = "";
    
    /// <summary>
    /// Strongly-typed type reference (parsed AST).
    /// </summary>
    public BmTypeReference? TypeRef { get; set; }
    
    public bool IsKey { get; set; }
    public bool IsNullable { get; set; }
    
    // Phase 7: Computed field properties
    public bool IsVirtual { get; set; }
    public bool IsReadonly { get; set; }
    public bool IsImmutable { get; set; }
    public bool IsComputed { get; set; }
    public bool IsStored { get; set; }
    public BMMDL.MetaModel.Enums.ComputedStrategy? ComputedStrategy { get; set; }
    public string? ComputedExprString { get; set; }
    public BmExpression? ComputedExpr { get; set; }
    
    /// <summary>
    /// Default value as string (legacy).
    /// </summary>
    public string? DefaultValueString { get; set; }
    
    /// <summary>
    /// Strongly-typed default expression (parsed AST).
    /// </summary>
    public BmExpression? DefaultExpr { get; set; }

    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmAssociation : INamedElement, IAnnotatable
{
    public string Name { get; set; } = ""; // Alias
    public string QualifiedName => Name;
    public string TargetEntity { get; set; } = ""; // Syntax: Namespace.Entity

    /// <summary>
    /// Cardinality of the association (1:1, 1:N, N:1, N:M).
    /// </summary>
    public BmCardinality Cardinality { get; set; } = BmCardinality.ManyToOne;

    /// <summary>
    /// Minimum cardinality (0 = optional, 1 = required).
    /// </summary>
    public int MinCardinality { get; set; } = 0;

    /// <summary>
    /// Maximum cardinality (-1 = unlimited/many).
    /// </summary>
    public int MaxCardinality { get; set; } = 1;

    /// <summary>
    /// On condition as string (legacy).
    /// </summary>
    public string? OnConditionString { get; set; }

    /// <summary>
    /// Strongly-typed on condition expression (parsed AST).
    /// </summary>
    public BmExpression? OnConditionExpr { get; set; }

    /// <summary>
    /// Referential action on parent delete.
    /// Null means use legacy default: Cascade for compositions, Restrict for associations.
    /// </summary>
    public DeleteAction? OnDelete { get; set; }

    public List<BmAnnotation> Annotations { get; } = new();

    // ... Implementation of Interface properties ...
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

/// <summary>
/// Cardinality of associations and compositions.
/// </summary>
public enum BmCardinality
{
    /// <summary>One-to-One: exactly one related entity.</summary>
    OneToOne,
    
    /// <summary>One-to-Many: one parent has many children.</summary>
    OneToMany,
    
    /// <summary>Many-to-One: many entities reference one target (FK).</summary>
    ManyToOne,
    
    /// <summary>Many-to-Many: requires junction table.</summary>
    ManyToMany
}

/// <summary>
/// Referential action to take when the referenced (parent) entity is deleted.
/// </summary>
public enum DeleteAction
{
    /// <summary>Delete all referencing rows (children).</summary>
    Cascade,
    /// <summary>Prevent deletion if referencing rows exist (default for associations).</summary>
    Restrict,
    /// <summary>Set FK columns to NULL in referencing rows.</summary>
    SetNull,
    /// <summary>Take no action (defer to database behavior).</summary>
    NoAction
}

public class BmComposition : BmAssociation
{
    // Composition is a special kind of association (parent-child)
}

// ============================================================
// Phase 7: Index and Constraint Definitions
// ============================================================

public class BmIndex
{
    public string Name { get; set; } = "";
    public List<string> Fields { get; } = new();
    public bool IsUnique { get; set; }

    /// <summary>
    /// Optional expression for functional indexes (e.g., "LOWER(email)").
    /// When set, the index is created on the expression rather than on column names.
    /// </summary>
    public string? Expression { get; set; }
}

public abstract class BmConstraint
{
    public string Name { get; set; } = "";
}

public class BmCheckConstraint : BmConstraint
{
    public string ConditionString { get; set; } = "";
    public BmExpression? Condition { get; set; }
}

public class BmUniqueConstraint : BmConstraint
{
    public List<string> Fields { get; } = new();
}

public class BmForeignKeyConstraint : BmConstraint
{
    public List<string> Fields { get; } = new();
    public string ReferencedEntity { get; set; } = "";
    public List<string> ReferencedFields { get; } = new();
}
