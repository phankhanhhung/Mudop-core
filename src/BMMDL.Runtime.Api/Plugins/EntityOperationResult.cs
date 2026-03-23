namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Result from the entity operation behavior pipeline.
/// Contains the operation outcome, response data, HTTP status code, and optional headers/warnings.
/// </summary>
public class EntityOperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The result data (entity record) to include in the response body.
    /// </summary>
    public Dictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// The HTTP status code to return.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional HTTP response headers to set (e.g., ETag, Location).
    /// </summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    /// <summary>
    /// Non-fatal warnings to include in the response (e.g., deprecated field usage).
    /// </summary>
    public List<string> Warnings { get; } = [];
}
