using System.Text.Json.Serialization;

namespace BMMDL.Registry.Api.Models;

public record AiAssistRequest
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }  // "complete" | "generate" | "review" | "explain-error"

    [JsonPropertyName("context")]
    public required string Context { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("cursorLine")]
    public int? CursorLine { get; init; }

    [JsonPropertyName("cursorColumn")]
    public int? CursorColumn { get; init; }
}

public record AiAssistResponse
{
    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("suggestions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Suggestions { get; init; }
}

public record AiStatusResponse
{
    [JsonPropertyName("configured")]
    public required bool Configured { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}

public record NlMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }  // "user" | "assistant"

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

public record NlQueryRequest
{
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    [JsonPropertyName("moduleName")]
    public required string ModuleName { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("schemaContext")]
    public required string SchemaContext { get; init; }

    [JsonPropertyName("history")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NlMessageDto>? History { get; init; }
}

public record NlQueryResponse
{
    [JsonPropertyName("filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filter { get; init; }

    [JsonPropertyName("expand")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expand { get; init; }

    [JsonPropertyName("select")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Select { get; init; }

    [JsonPropertyName("orderby")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Orderby { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}
