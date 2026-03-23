namespace BMMDL.Registry.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// OData v4 compliant error response format.
/// Mirrors the structure in BMMDL.Runtime.Api.Models for API consistency.
/// </summary>
public record ODataErrorResponse
{
    /// <summary>
    /// The error object.
    /// </summary>
    [JsonPropertyName("error")]
    public required ODataError Error { get; init; }

    /// <summary>
    /// Create an error response with code and message.
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
}

/// <summary>
/// OData v4 error object.
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
}
