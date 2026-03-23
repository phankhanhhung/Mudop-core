namespace BMMDL.Runtime.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// OData v4 collection response format.
/// </summary>
/// <typeparam name="T">Type of items in the collection.</typeparam>
public record ODataCollectionResponse<T>
{
    /// <summary>
    /// OData context URL describing the collection.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? Context { get; init; }

    /// <summary>
    /// Total count of matching items (when $count=true).
    /// </summary>
    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; init; }

    /// <summary>
    /// URL to next page of results (when more items exist).
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; init; }

    /// <summary>
    /// OData v4 Delta link for change tracking.
    /// Present when client requests trackChanges=true.
    /// </summary>
    [JsonPropertyName("@odata.deltaLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeltaLink { get; init; }

    /// <summary>
    /// The collection of items.
    /// </summary>
    [JsonPropertyName("value")]
    public required IReadOnlyList<T> Value { get; init; }
}

/// <summary>
/// OData v4 single entity response format.
/// </summary>
public record ODataEntityResponse<T> where T : class
{
    /// <summary>
    /// OData context URL describing the entity type.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? Context { get; init; }

    /// <summary>
    /// The entity data (flattened into this response).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// OData v4 error response format.
/// </summary>
public record ODataErrorResponse
{
    /// <summary>
    /// The error object.
    /// </summary>
    [JsonPropertyName("error")]
    public required ODataError Error { get; init; }

    /// <summary>
    /// Create an error response from an exception.
    /// </summary>
    public static ODataErrorResponse FromException(string code, string message, string? target = null) => new()
    {
        Error = new ODataError
        {
            Code = code,
            Message = message,
            Target = target
        }
    };

    /// <summary>
    /// Create a 412 Precondition Failed response for ETag mismatch.
    /// </summary>
    public static ODataErrorResponse PreconditionFailed(string? currentETag = null) => new()
    {
        Error = new ODataError
        {
            Code = "PRECONDITION_FAILED",
            Message = "The ETag does not match. The resource has been modified by another client.",
            Target = currentETag != null ? $"Current ETag: {currentETag}" : null
        }
    };
}

/// <summary>
/// OData error details.
/// </summary>
public record ODataError
{
    /// <summary>
    /// Machine-readable error code.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Target of the error (property/field name).
    /// </summary>
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }

    /// <summary>
    /// Additional error details.
    /// </summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ODataErrorDetail>? Details { get; init; }

    /// <summary>
    /// Inner error for debugging (should be hidden in production).
    /// </summary>
    [JsonPropertyName("innererror")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ODataInnerError? InnerError { get; init; }
}

/// <summary>
/// Detailed error information.
/// </summary>
public record ODataErrorDetail
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }
}

/// <summary>
/// Inner error for debugging purposes.
/// </summary>
public record ODataInnerError
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("stacktrace")]
    public string? StackTrace { get; init; }
}

/// <summary>
/// Helper class for building OData responses.
/// </summary>
public static class ODataResponseBuilder
{
    /// <summary>
    /// Build a collection response.
    /// </summary>
    public static ODataCollectionResponse<T> Collection<T>(
        IReadOnlyList<T> items,
        string baseUrl,
        string entitySet,
        int? totalCount = null,
        int? skip = null,
        int? top = null)
    {
        string? nextLink = null;
        
        // Generate nextLink if there are more items
        if (totalCount.HasValue && skip.HasValue && top.HasValue)
        {
            var nextSkip = skip.Value + top.Value;
            if (nextSkip < totalCount.Value)
            {
                nextLink = $"{baseUrl}?$skip={nextSkip}&$top={top.Value}";
            }
        }

        return new ODataCollectionResponse<T>
        {
            Context = $"{baseUrl}/$metadata#{entitySet}",
            Count = totalCount,
            NextLink = nextLink,
            Value = items
        };
    }

    /// <summary>
    /// Build a single entity response.
    /// </summary>
    public static object Entity(
        Dictionary<string, object?> data,
        string baseUrl,
        string entitySet)
    {
        // Add @odata.context to the data dictionary
        var result = new Dictionary<string, object?>(data)
        {
            ["@odata.context"] = $"{baseUrl}/$metadata#{entitySet}/$entity"
        };
        return result;
    }
}
