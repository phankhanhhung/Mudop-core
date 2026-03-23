namespace BMMDL.MetaModel.Features;

/// <summary>
/// Declares the schema for a plugin-contributed annotation.
/// Plugins implement <see cref="BMMDL.Runtime.Plugins.IAnnotationSchemaProvider"/>
/// to declare what annotations they understand, enabling compile-time validation.
/// </summary>
public class PluginAnnotationSchema
{
    /// <summary>
    /// The annotation name or dotted prefix.
    /// For structured annotations: "Workflow" matches @Workflow or @Workflow { ... }.
    /// For dotted annotations: "Sequence" matches @Sequence.Name, @Sequence.Pattern, etc.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this annotation uses dotted sub-annotations (e.g., @Sequence.Name, @Sequence.Pattern)
    /// or a single structured annotation (e.g., @Workflow { ... }).
    /// </summary>
    public AnnotationStyle Style { get; init; } = AnnotationStyle.Structured;

    /// <summary>
    /// What model elements this annotation can be attached to.
    /// </summary>
    public AnnotationTarget Target { get; init; } = AnnotationTarget.Entity;

    /// <summary>
    /// Human-readable description of this annotation's purpose.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the annotation can appear with no value or properties (bare marker).
    /// E.g., @CDC with no arguments.
    /// </summary>
    public bool AllowBareMarker { get; init; }

    /// <summary>
    /// Schema for the single value form: @Name(value) or @Name: value.
    /// Null means the single-value form is not supported.
    /// </summary>
    public AnnotationValueSchema? ValueSchema { get; init; }

    /// <summary>
    /// Property schemas for the structured form: @Name { key: value } or dotted @Name.Key: value.
    /// </summary>
    public List<AnnotationPropertySchema> Properties { get; init; } = [];
}

/// <summary>
/// Whether an annotation uses dotted style (@X.Y: value) or structured style (@X { y: value }).
/// </summary>
public enum AnnotationStyle
{
    /// <summary>Single annotation with properties: @Workflow { state: 'draft', ... }</summary>
    Structured,

    /// <summary>Multiple dotted annotations: @Sequence.Name: 'Seq1', @Sequence.Pattern: '{seq}'</summary>
    Dotted,

    /// <summary>Accept both styles (properties merged from either form).</summary>
    Both
}

/// <summary>
/// What model elements an annotation can be attached to.
/// </summary>
[Flags]
public enum AnnotationTarget
{
    Entity = 1,
    Field = 2,
    Association = 4,
    Type = 8,
    Enum = 16,
    Service = 32,
    Rule = 64,
    Event = 128,
    All = Entity | Field | Association | Type | Enum | Service | Rule | Event
}

/// <summary>
/// Schema for a single annotation property (key-value pair).
/// </summary>
public class AnnotationPropertySchema
{
    /// <summary>
    /// Property name. For dotted style, this is the suffix after the dot (e.g., "Name" for @Sequence.Name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Expected value type.
    /// </summary>
    public AnnotationValueType Type { get; init; } = AnnotationValueType.String;

    /// <summary>
    /// Whether this property must be specified.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Default value when not specified.
    /// </summary>
    public object? Default { get; init; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// If set, the value must be one of these options.
    /// </summary>
    public List<object>? AllowedValues { get; init; }
}

/// <summary>
/// Schema for an annotation's single value (@Name(value) or @Name: value).
/// </summary>
public class AnnotationValueSchema
{
    public AnnotationValueType Type { get; init; } = AnnotationValueType.String;
    public string? Description { get; init; }
    public List<object>? AllowedValues { get; init; }
}

/// <summary>
/// Supported annotation value types for schema validation.
/// </summary>
public enum AnnotationValueType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Enum,
    Array,
    Object,
    Any
}
