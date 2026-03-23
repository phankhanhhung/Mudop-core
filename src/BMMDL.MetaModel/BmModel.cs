using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Expressions;


namespace BMMDL.MetaModel;

/// <summary>
/// The complete BMMDL model built from parsing.
/// </summary>

public class BmAnnotateDirective
{
    public string TargetName { get; set; } = "";
    public string? TargetField { get; set; }  // null = entity-level, non-null = field-level
    public List<BmAnnotation> Annotations { get; } = new();
    public string? Namespace { get; set; }
    public string? SourceFile { get; set; }
    public int Line { get; set; }
}

public class BmModel
{
    /// <summary>
    /// Module declaration (optional, for modular DSL).
    /// When multiple files are merged, this is the first/primary module.
    /// </summary>
    public BmModuleDeclaration? Module { get; set; }
    
    /// <summary>
    /// All module declarations from merged files.
    /// Used when --resolve-deps compiles multiple modules together.
    /// </summary>
    public List<BmModuleDeclaration> AllModules { get; } = new();
    
    public string? Namespace { get; set; }
    
    public List<BmEntity> Entities { get; } = new();
    public List<BmType> Types { get; } = new();
    public List<BmEnum> Enums { get; } = new();
    public List<BmAspect> Aspects { get; } = new();
    public List<BmService> Services { get; } = new();
    public List<BmView> Views { get; } = new();
    public List<BmAccessControl> AccessControls { get; } = new();
    public List<BmRule> Rules { get; } = new();
    public List<BmSequence> Sequences { get; } = new();
    
    // Phase 7: Domain Events
    public List<BmEvent> Events { get; } = new();
    
    // Entity System Overhaul
    public List<BmExtension> Extensions { get; } = new();
    public List<BmModification> Modifications { get; } = new();

    // Annotate directives (annotate Entity with { ... })
    public List<BmAnnotateDirective> AnnotateDirectives { get; } = new();

    // Migration definitions (explicit schema migrations)
    public List<BmMigrationDef> Migrations { get; } = new();

    // Seed data definitions (initial data for entities)
    public List<BmSeedDef> Seeds { get; } = new();

    // File-level import statements (using directives)
    public List<BmImport> Imports { get; } = new();

    /// <summary>
    /// Merge another model into this one.
    /// </summary>
    public void Merge(BmModel other)
    {
        // Collect all module declarations
        if (other.Module != null)
        {
            AllModules.Add(other.Module);
        }
        AllModules.AddRange(other.AllModules);
        
        // Set primary module if not set
        if (Module == null && other.Module != null)
        {
            Module = other.Module;
        }
        
        Entities.AddRange(other.Entities);
        Types.AddRange(other.Types);
        Enums.AddRange(other.Enums);
        Aspects.AddRange(other.Aspects);
        Services.AddRange(other.Services);
        Views.AddRange(other.Views);
        AccessControls.AddRange(other.AccessControls);
        Rules.AddRange(other.Rules);
        Sequences.AddRange(other.Sequences);
        Events.AddRange(other.Events);
        Extensions.AddRange(other.Extensions);
        Modifications.AddRange(other.Modifications);
        AnnotateDirectives.AddRange(other.AnnotateDirectives);
        Migrations.AddRange(other.Migrations);
        Seeds.AddRange(other.Seeds);
        Imports.AddRange(other.Imports);
    }

    /// <summary>
    /// Find entity by name.
    /// </summary>
    public BmEntity? FindEntity(string name) =>
        Entities.FirstOrDefault(e => e.Name == name || e.QualifiedName == name);

    /// <summary>
    /// Find type by name.
    /// </summary>
    public BmType? FindType(string name) =>
        Types.FirstOrDefault(t => t.Name == name || t.QualifiedName == name);
}

// ============================================================
// Import System
// ============================================================

/// <summary>
/// Represents a file-level import statement (using directive).
/// Syntax: using [alias:] namespace.path [from 'source.bmmdl'];
/// </summary>
public class BmImport
{
    /// <summary>
    /// The namespace or symbol path being imported (e.g., "business.crm").
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Optional alias for the import (e.g., "crm" in "using crm: business.crm").
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Optional source file or module (e.g., "'common.bmmdl'" in "using ns from 'common.bmmdl'").
    /// </summary>
    public string? Source { get; set; }
}

// ============================================================
// Module System
// ============================================================

/// <summary>
/// Module declaration for modular DSL packaging and versioning.
/// </summary>
public class BmModuleDeclaration
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Dependencies on other modules with version ranges.
    /// </summary>
    public List<BmModuleDependency> Dependencies { get; } = new();
    
    /// <summary>
    /// Namespaces that this module publishes (makes available for other modules).
    /// </summary>
    public List<string> Publishes { get; } = new();
    
    /// <summary>
    /// Namespaces that this module imports from dependencies.
    /// </summary>
    public List<string> Imports { get; } = new();

    /// <summary>
    /// Indicates if this module enables tenant-awareness by default.
    /// When true, all entities in this module are automatically tenant-scoped
    /// unless explicitly marked otherwise.
    /// </summary>
    public bool TenantAware { get; set; } = false;

    // Source tracking
    public string? SourceFile { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
}

/// <summary>
/// A dependency on another module with version constraint.
/// </summary>
public class BmModuleDependency
{
    public string ModuleName { get; set; } = "";
    
    /// <summary>
    /// Version range (e.g., ">=1.0.0", "^2.0.0", "~1.2.0").
    /// </summary>
    public string VersionRange { get; set; } = "";
}

/// <summary>
/// Type definition.
/// </summary>
public class BmType : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    
    public string BaseType { get; set; } = ""; // Built-in or reference
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    
    // For struct types
    public List<BmField> Fields { get; } = new();
    
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

/// <summary>
/// Enum definition.
/// </summary>
public class BmEnum : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    
    public string? BaseType { get; set; }
    public List<BmEnumValue> Values { get; } = new();
    
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmEnumValue : INamedElement
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    public object? Value { get; set; }
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

/// <summary>
/// Aspect (mixin) definition.
/// </summary>
public class BmAspect : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    
    public List<string> Includes { get; } = new(); // Other aspects
    public List<BmField> Fields { get; } = new();
    public List<BmAssociation> Associations { get; } = new();
    public List<BmComposition> Compositions { get; } = new();

    // Bound actions and functions (inlined into entity BoundActions/BoundFunctions)
    public List<BmAction> Actions { get; } = new();
    public List<BmFunction> Functions { get; } = new();

    // Behavioral AOP
    public List<BmRule> Rules { get; } = new();
    public List<BmAccessControl> AccessControls { get; } = new();

    // Indexes and Constraints (inlined into entity during optimization)
    public List<BmIndex> Indexes { get; } = new();
    public List<BmConstraint> Constraints { get; } = new();

    public List<BmAnnotation> Annotations { get; } = new();

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

/// <summary>
/// View definition.
/// </summary>
public class BmView : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    public List<BmViewParameter> Parameters { get; } = new();
    public string SelectStatement { get; set; } = ""; // Raw SQL-like statement (kept for backward compat)

    /// <summary>
    /// Parsed AST of the SELECT statement. Null if parsing failed (raw string is used as fallback).
    /// </summary>
    public BmSelectStatement? ParsedSelect { get; set; }

    /// <summary>Whether this view was defined using projection syntax (projection on Entity).</summary>
    public bool IsProjection { get; set; }

    /// <summary>The entity name referenced by the projection (e.g., "Customer").</summary>
    public string? ProjectionEntityName { get; set; }

    /// <summary>Explicit projection fields with optional aliases.</summary>
    public List<BmProjectionField> ProjectionFields { get; } = new();

    /// <summary>Fields excluded via * excluding (field1, field2).</summary>
    public List<string> ExcludedFields { get; } = new();

    /// <summary>Whether the projection uses wildcard (*).</summary>
    public bool ProjectionIncludesAll { get; set; }

    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmViewParameter : INamedElement
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    public string Type { get; set; } = "";
    public object? DefaultValue { get; set; }
    public BmExpression? DefaultExpr { get; set; }

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

/// <summary>
/// A field in a projection definition.
/// </summary>
public class BmProjectionField
{
    /// <summary>The field name (from the source entity).</summary>
    public string FieldName { get; set; } = "";

    /// <summary>Optional alias (AS alias).</summary>
    public string? Alias { get; set; }
}

/// <summary>
/// Access Control definition.
/// </summary>
public class BmAccessControl : INamedElement
{
    public string Name { get; set; } = ""; // Target entity name
    public string QualifiedName => Name;
    public string TargetEntity { get; set; } = "";
    public string? ExtendsFrom { get; set; }
    
    public List<BmAccessRule> Rules { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public class BmAccessRule
{
    public BmAccessRuleType RuleType { get; set; }
    public List<string> Operations { get; } = new();
    public BmPrincipal? Principal { get; set; }

    /// <summary>
    /// The scope at which this access rule applies (Global, Tenant, Company).
    /// Defaults to null (not specified, inherits from module or entity).
    /// </summary>
    public BmAccessScope? Scope { get; set; }

    public string? WhereCondition { get; set; }
    public BmExpression? WhereConditionExpr { get; set; }  // AST
    public List<BmFieldRestriction> FieldRestrictions { get; } = new();
}

public enum BmAccessRuleType { Grant, Deny, RestrictFields }

/// <summary>
/// Access scope levels for multi-tenant isolation.
/// </summary>
public enum BmAccessScope { Global, Tenant, Company }

public class BmPrincipal
{
    public BmPrincipalType Type { get; set; }
    public List<string> Values { get; } = new();
}

public enum BmPrincipalType { Role, User, Authenticated, Anonymous }

public class BmFieldRestriction
{
    public string FieldName { get; set; } = "";
    public BmFieldAccessType AccessType { get; set; }
    public string? Condition { get; set; }
    public BmExpression? ConditionExpr { get; set; }  // AST
    public string? MaskType { get; set; }
}

public enum BmFieldAccessType { Visible, Masked, Readonly, Hidden }

/// <summary>
/// Business Rule definition.
/// </summary>
public class BmRule : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    public string TargetEntity { get; set; } = "";
    
    public List<BmTriggerEvent> Triggers { get; } = new();
    public List<BmRuleStatement> Statements { get; } = new();
    
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmTriggerEvent
{
    public BmTriggerTiming Timing { get; set; }
    public BmTriggerOperation Operation { get; set; }
    public List<string> ChangeFields { get; } = new(); // For ON CHANGE OF
}

public enum BmTriggerTiming { Before, After, OnChange }
public enum BmTriggerOperation { Create, Update, Delete, Read }

public abstract class BmRuleStatement { }

public class BmValidateStatement : BmRuleStatement
{
    public string Expression { get; set; } = "";
    public BmExpression? ExpressionAst { get; set; }  // AST
    public string? Message { get; set; }
    public BmSeverity Severity { get; set; } = BmSeverity.Error;
}

public class BmComputeStatement : BmRuleStatement
{
    public string Target { get; set; } = "";
    public string Expression { get; set; } = "";
    public BmExpression? ExpressionAst { get; set; }  // AST
}

public class BmWhenStatement : BmRuleStatement
{
    public string Condition { get; set; } = "";
    public BmExpression? ConditionAst { get; set; }  // AST
    public List<BmRuleStatement> ThenStatements { get; } = new();
    public List<BmRuleStatement> ElseStatements { get; } = new();
}

public class BmCallStatement : BmRuleStatement
{
    public string Target { get; set; } = "";
    public List<BmExpression> Arguments { get; } = new();
    public List<string?> ArgumentLabels { get; } = new();
}

// ============================================================
// Action/Function Body Statements (Phase: Action Body Language)
// ============================================================

/// <summary>
/// Emit a domain event from within an action.
/// </summary>
public class BmEmitStatement : BmRuleStatement
{
    public string EventName { get; set; } = "";
    
    /// <summary>
    /// Event field assignments (name -> expression)
    /// </summary>
    public Dictionary<string, BmExpression> FieldAssignments { get; } = new();
}

/// <summary>
/// Iterate over a collection within an action.
/// </summary>
public class BmForeachStatement : BmRuleStatement
{
    public string VariableName { get; set; } = "";
    public string Collection { get; set; } = "";
    public BmExpression? CollectionAst { get; set; }
    public List<BmRuleStatement> Body { get; } = new();
}

/// <summary>
/// Return a value from an action or function.
/// </summary>
public class BmReturnStatement : BmRuleStatement
{
    public string Expression { get; set; } = "";
    public BmExpression? ExpressionAst { get; set; }
}

/// <summary>
/// Declare a local variable within an action or function.
/// </summary>
public class BmLetStatement : BmRuleStatement
{
    public string VariableName { get; set; } = "";
    public string Expression { get; set; } = "";
    public BmExpression? ExpressionAst { get; set; }
}

/// <summary>
/// Raise an exception with message and severity.
/// </summary>
public class BmRaiseStatement : BmRuleStatement
{
    public string Message { get; set; } = "";
    public BmSeverity Severity { get; set; } = BmSeverity.Error;
}

/// <summary>
/// Reject the current operation with an optional message. Short-circuits rule execution.
/// </summary>
public class BmRejectStatement : BmRuleStatement
{
    public BmExpression? Message { get; set; }
}

public enum BmSeverity { Error, Warning, Info }

/// <summary>
/// Sequence definition.
/// </summary>
public class BmSequence : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string QualifiedName => Name;
    
    public string? ForEntity { get; set; }
    public string? ForField { get; set; }
    
    public string? Pattern { get; set; }
    public int StartValue { get; set; } = 1;
    public int Increment { get; set; } = 1;
    public int? Padding { get; set; }
    public int? MaxValue { get; set; }
    public BmSequenceScope Scope { get; set; } = BmSequenceScope.Company;
    public BmResetTrigger ResetOn { get; set; } = BmResetTrigger.Never;
    
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public enum BmSequenceScope { Global, Tenant, Company }
public enum BmResetTrigger { Never, Daily, Monthly, Yearly, FiscalYear }

// ============================================================
// Phase 7: Event Definitions (Domain Events)
// ============================================================

/// <summary>
/// Domain event definition.
/// </summary>
public class BmEvent : INamedElement, IAnnotatable
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    
    public List<BmEventField> Fields { get; } = new();
    public List<BmAnnotation> Annotations { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    /// <summary>
    /// Whether this event is an integration event (has @Integration annotation).
    /// Integration events are routed through the durable outbox for guaranteed delivery.
    /// </summary>
    public bool IsIntegration => HasAnnotation("Integration");

    public BmAnnotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Name == name);
    public bool HasAnnotation(string name) => Annotations.Any(a => a.Name == name);
}

public class BmEventField
{
    public string Name { get; set; } = "";
    public string TypeString { get; set; } = "";
    public BmTypeReference? TypeRef { get; set; }
    public List<BmAnnotation> Annotations { get; } = new();
}

// ============================================================
// Entity Extension (extend entity/type/aspect/service)
// ============================================================

public class BmExtension : INamedElement
{
    public string Name { get; set; } = ""; // Target name
    public string QualifiedName => Name;
    
    /// <summary>
    /// Target kind: "entity", "type", "aspect", or "service".
    /// </summary>
    public string TargetKind { get; set; } = "entity";
    
    /// <summary>
    /// Target entity/type/aspect/service name.
    /// </summary>
    public string TargetName { get; set; } = "";
    
    /// <summary>
    /// Aspects to mix in via WITH clause.
    /// </summary>
    public List<string> WithAspects { get; } = new();
    
    public List<BmField> Fields { get; } = new();
    public List<BmAssociation> Associations { get; } = new();
    public List<BmComposition> Compositions { get; } = new();

    // Bound actions and functions
    public List<BmAction> Actions { get; } = new();
    public List<BmFunction> Functions { get; } = new();

    // Indexes and constraints
    public List<BmIndex> Indexes { get; } = new();
    public List<BmConstraint> Constraints { get; } = new();

    public List<BmAnnotation> Annotations { get; } = new();

    /// <summary>
    /// Service entity exposures to merge (for "extend service" extensions).
    /// </summary>
    public List<BmEntity> ServiceEntities { get; } = new();

    /// <summary>
    /// Enum values to merge (for "extend enum" extensions).
    /// </summary>
    public List<BmEnumValue> EnumValues { get; } = new();

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

// ============================================================
// Entity Modification (modify entity/type)
// ============================================================

public class BmModification : INamedElement
{
    public string Name { get; set; } = ""; // Target name
    public string QualifiedName => Name;
    
    /// <summary>
    /// Target kind: "entity", "type", or "aspect".
    /// </summary>
    public string TargetKind { get; set; } = "entity";
    
    /// <summary>
    /// Target entity/type/aspect name.
    /// </summary>
    public string TargetName { get; set; } = "";
    
    public List<BmModifyAction> Actions { get; } = new();
    
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public abstract class BmModifyAction
{
    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
}

public class BmRemoveFieldAction : BmModifyAction
{
    public string FieldName { get; set; } = "";
}

public class BmRenameFieldAction : BmModifyAction
{
    public string OldName { get; set; } = "";
    public string NewName { get; set; } = "";
}

public class BmChangeTypeAction : BmModifyAction
{
    public string FieldName { get; set; } = "";
    public string NewTypeString { get; set; } = "";
}

public class BmAddFieldAction : BmModifyAction
{
    public BmField Field { get; set; } = new();
}

public class BmModifyFieldAction : BmModifyAction
{
    public string FieldName { get; set; } = "";
    public string? NewTypeString { get; set; }
    public string? NewDefaultValueString { get; set; }
    public List<BmAnnotation> Annotations { get; } = new();
}

/// <summary>
/// Add a new member to an enum (for modify enum).
/// </summary>
public class BmAddEnumMemberAction : BmModifyAction
{
    public BmEnumValue Member { get; set; } = new();
}

/// <summary>
/// Remove a member from an enum (for modify enum).
/// </summary>
public class BmRemoveEnumMemberAction : BmModifyAction
{
    public string MemberName { get; set; } = "";
}
