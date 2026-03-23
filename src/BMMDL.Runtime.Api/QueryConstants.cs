namespace BMMDL.Runtime.Api;

/// <summary>
/// Constants for query and pagination limits.
/// These can be overridden via configuration (appsettings.json).
/// </summary>
public static class QueryConstants
{
    /// <summary>
    /// Default number of items to return in a page.
    /// </summary>
    public const int DefaultPageSize = 50;
    
    /// <summary>
    /// Minimum page size allowed.
    /// </summary>
    public const int MinPageSize = 1;
    
    /// <summary>
    /// Maximum page size allowed.
    /// </summary>
    public const int MaxPageSize = 1000;
    
    /// <summary>
    /// Default page size for batch operations.
    /// </summary>
    public const int DefaultBatchPageSize = 100;
    
    /// <summary>
    /// Maximum number of operations allowed in a single batch request.
    /// </summary>
    public const int MaxBatchOperations = 100;
    
    /// <summary>
    /// Maximum depth for $expand operations to prevent infinite recursion.
    /// </summary>
    public const int MaxExpandDepth = 5;
    
    /// <summary>
    /// Default levels for recursive $expand.
    /// </summary>
    public const int DefaultExpandLevels = 2;
}
