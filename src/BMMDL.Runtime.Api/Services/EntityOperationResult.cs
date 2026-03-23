namespace BMMDL.Runtime.Api.Services;

/// <summary>
/// Result object for entity CRUD operations performed by services.
/// Controllers map this to the appropriate HTTP response format.
/// </summary>
public class EntityOperationResult
{
    public bool IsSuccess { get; init; }
    public Dictionary<string, object?>? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; } = 200;

    /// <summary>
    /// Non-blocking warnings from rule execution (severity = Warning).
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    /// Informational messages from rule execution (severity = Info).
    /// </summary>
    public List<string> Infos { get; init; } = new();

    public static EntityOperationResult Success(Dictionary<string, object?> data, int statusCode = 200)
        => new() { IsSuccess = true, Data = data, StatusCode = statusCode };

    public static EntityOperationResult Deleted()
        => new() { IsSuccess = true, StatusCode = 204 };

    public static EntityOperationResult Error(string code, string message, int statusCode = 400)
        => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, StatusCode = statusCode };

    public static EntityOperationResult NotFound(string entityName, Guid id)
        => Error("NotFound", $"Entity {entityName} with ID {id} not found", 404);

    public static EntityOperationResult PreconditionFailed(string etag)
        => new() { IsSuccess = false, ErrorCode = "PreconditionFailed", ErrorMessage = "ETag mismatch — the entity was modified by another request.", StatusCode = 412, Data = new Dictionary<string, object?> { ["ETag"] = etag } };

    public static EntityOperationResult Conflict(string message)
        => Error("ReferentialConstraintViolation", message, 409);
}
