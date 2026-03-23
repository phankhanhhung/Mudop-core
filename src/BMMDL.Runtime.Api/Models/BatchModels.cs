namespace BMMDL.Runtime.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// OData v4 $batch request in JSON format.
/// </summary>
public record BatchRequest
{
    /// <summary>
    /// Individual requests to execute.
    /// </summary>
    [JsonPropertyName("requests")]
    public List<BatchRequestItem> Requests { get; init; } = [];
}

/// <summary>
/// Single request item within a batch.
/// </summary>
public record BatchRequestItem
{
    /// <summary>
    /// Unique identifier for this request (used for DependsOn references).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    
    /// <summary>
    /// HTTP method: GET, POST, PATCH, PUT, DELETE.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = "GET";
    
    /// <summary>
    /// Relative URL (e.g., "/Platform/Order" or "/Platform/Order(123)").
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
    
    /// <summary>
    /// Optional headers for this request.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
    
    /// <summary>
    /// Request body (for POST, PATCH, PUT).
    /// </summary>
    [JsonPropertyName("body")]
    public object? Body { get; init; }
    
    /// <summary>
    /// IDs of requests that must complete before this one.
    /// Allows chaining dependent operations.
    /// </summary>
    [JsonPropertyName("dependsOn")]
    public string[]? DependsOn { get; init; }
    
    /// <summary>
    /// OData v4 atomicity group identifier.
    /// Requests in the same group are executed within a single transaction.
    /// If any request in the group fails, all are rolled back.
    /// </summary>
    [JsonPropertyName("atomicityGroup")]
    public string? AtomicityGroup { get; init; }
}

/// <summary>
/// OData v4 $batch response in JSON format.
/// </summary>
public record BatchResponse
{
    /// <summary>
    /// Responses for each request in the batch.
    /// </summary>
    [JsonPropertyName("responses")]
    public List<BatchResponseItem> Responses { get; init; } = [];
}

/// <summary>
/// Single response item within a batch.
/// </summary>
public record BatchResponseItem
{
    /// <summary>
    /// ID matching the corresponding request.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    
    /// <summary>
    /// HTTP status code.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }
    
    /// <summary>
    /// Response headers.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
    
    /// <summary>
    /// Response body (null for 204 No Content).
    /// </summary>
    [JsonPropertyName("body")]
    public object? Body { get; init; }
}
