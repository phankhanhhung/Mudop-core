using System.Text.Json.Serialization;

namespace BMMDL.Runtime.Api.Models;

/// <summary>
/// Module metadata grouped by namespace.
/// </summary>
public record ModuleMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("services")]
    public required IReadOnlyList<ServiceMetadataDto> Services { get; init; }
}

/// <summary>
/// Service metadata with exposed entity sets, actions, and functions.
/// </summary>
public record ServiceMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("entities")]
    public required IReadOnlyList<EntitySetMetadataDto> Entities { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<ActionMetadataDto> Actions { get; init; }

    [JsonPropertyName("functions")]
    public required IReadOnlyList<FunctionMetadataDto> Functions { get; init; }
}

/// <summary>
/// Entity set reference within a service.
/// </summary>
public record EntitySetMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }
}

/// <summary>
/// Detailed entity metadata with fields, keys, associations.
/// </summary>
public record EntityMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldMetadataDto> Fields { get; init; }

    [JsonPropertyName("keys")]
    public required IReadOnlyList<string> Keys { get; init; }

    [JsonPropertyName("associations")]
    public required IReadOnlyList<AssociationMetadataDto> Associations { get; init; }

    [JsonPropertyName("annotations")]
    public required IReadOnlyDictionary<string, object?> Annotations { get; init; }

    [JsonPropertyName("hasStream")]
    public bool HasStream { get; init; }

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; init; }

    [JsonPropertyName("isSingleton")]
    public bool IsSingleton { get; init; }

    [JsonPropertyName("parentEntityName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEntityName { get; init; }

    [JsonPropertyName("boundActions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ActionMetadataDto>? BoundActions { get; init; }

    [JsonPropertyName("boundFunctions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FunctionMetadataDto>? BoundFunctions { get; init; }
}

/// <summary>
/// Field metadata for form rendering.
/// </summary>
public record FieldMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; init; }

    [JsonPropertyName("isReadOnly")]
    public bool IsReadOnly { get; init; }

    [JsonPropertyName("isComputed")]
    public bool IsComputed { get; init; }

    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; init; }

    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; init; }

    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; init; }

    [JsonPropertyName("defaultValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DefaultValue { get; init; }

    [JsonPropertyName("enumValues")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EnumValueDto>? EnumValues { get; init; }

    [JsonPropertyName("annotations")]
    public required IReadOnlyDictionary<string, object?> Annotations { get; init; }
}

public record EnumValueDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required object Value { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }
}

public record AssociationMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("targetEntity")]
    public required string TargetEntity { get; init; }

    [JsonPropertyName("cardinality")]
    public required string Cardinality { get; init; }

    [JsonPropertyName("foreignKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForeignKey { get; init; }

    [JsonPropertyName("isComposition")]
    public bool IsComposition { get; init; }
}

public record ActionMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<ParameterMetadataDto> Parameters { get; init; }

    [JsonPropertyName("returnType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnType { get; init; }

    [JsonPropertyName("isBound")]
    public bool IsBound { get; init; }

    [JsonPropertyName("bindingParameter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingParameter { get; init; }
}

public record FunctionMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<ParameterMetadataDto> Parameters { get; init; }

    [JsonPropertyName("returnType")]
    public required string ReturnType { get; init; }

    [JsonPropertyName("isBound")]
    public bool IsBound { get; init; }

    [JsonPropertyName("bindingParameter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingParameter { get; init; }
}

public record ParameterMetadataDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; init; } = true;
}
