namespace BMMDL.Runtime.Api.Models;

/// <summary>
/// Standard API response wrapper.
/// </summary>
/// <typeparam name="T">Type of data returned.</typeparam>
public record ApiResponse<T>
{
    /// <summary>
    /// Response data (null if error).
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Error message (null if success).
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Error code for machine-readable error handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// True if the request was successful.
    /// </summary>
    public bool Success => Error == null;

    /// <summary>
    /// Create a successful response.
    /// </summary>
    public static ApiResponse<T> Ok(T data) => new() { Data = data };

    /// <summary>
    /// Create an error response.
    /// </summary>
    public static ApiResponse<T> Fail(string error, string? errorCode = null) => 
        new() { Error = error, ErrorCode = errorCode };
}

/// <summary>
/// API response for paginated list queries.
/// </summary>
/// <typeparam name="T">Type of items in the list.</typeparam>
public record ApiListResponse<T>
{
    /// <summary>
    /// List of items.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Total count of items matching the query (before pagination).
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// Current page number (0-indexed).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size (items per page).
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Whether there are more items beyond the current page.
    /// </summary>
    public bool HasMore => TotalCount.HasValue && (Page + 1) * PageSize < TotalCount.Value;

    /// <summary>
    /// Create an empty list response.
    /// </summary>
    public static ApiListResponse<T> Empty(int pageSize = QueryConstants.DefaultPageSize) => new()
    {
        Items = Array.Empty<T>(),
        TotalCount = 0,
        Page = 0,
        PageSize = pageSize
    };
}

/// <summary>
/// API error response body.
/// </summary>
public record ApiErrorResponse
{
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    /// Machine-readable error code.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Correlation ID for tracking.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Additional details (validation errors, etc.).
    /// </summary>
    public IDictionary<string, string[]>? Details { get; init; }
}
