namespace BMMDL.Runtime.Authorization;

/// <summary>
/// Result of an access control check.
/// </summary>
public record AccessDecision
{
    /// <summary>
    /// Whether the operation is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }
    
    /// <summary>
    /// Reason for denial (if not allowed).
    /// </summary>
    public string? DeniedReason { get; init; }
    
    /// <summary>
    /// Names of rules that granted access.
    /// </summary>
    public IReadOnlyList<string> AllowedByRules { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Names of rules that denied access.
    /// </summary>
    public IReadOnlyList<string> DeniedByRules { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Create an allowed decision.
    /// </summary>
    public static AccessDecision Allowed(params string[] rules) => new()
    {
        IsAllowed = true,
        AllowedByRules = rules
    };
    
    /// <summary>
    /// Create a denied decision.
    /// </summary>
    public static AccessDecision Denied(string reason, params string[] rules) => new()
    {
        IsAllowed = false,
        DeniedReason = reason,
        DeniedByRules = rules
    };
    
    /// <summary>
    /// Default allow when no rules match (fail-open for now).
    /// </summary>
    public static AccessDecision DefaultAllow() => new()
    {
        IsAllowed = true,
        AllowedByRules = new[] { "(default)" }
    };
}

/// <summary>
/// CRUD operation types for access control.
/// </summary>
public enum CrudOperation
{
    Read,
    Create,
    Update,
    Delete,
    Execute
}

/// <summary>
/// Extensions for parsing operations.
/// </summary>
public static class CrudOperationExtensions
{
    public static CrudOperation FromHttpMethod(string method) => method.ToUpperInvariant() switch
    {
        "GET" => CrudOperation.Read,
        "POST" => CrudOperation.Create,
        "PUT" or "PATCH" => CrudOperation.Update,
        "DELETE" => CrudOperation.Delete,
        _ => CrudOperation.Read
    };
    
    public static string ToOperationString(this CrudOperation op) => op switch
    {
        CrudOperation.Read => "read",
        CrudOperation.Create => "create",
        CrudOperation.Update => "update",
        CrudOperation.Delete => "delete",
        CrudOperation.Execute => "execute",
        _ => "read"
    };
}
