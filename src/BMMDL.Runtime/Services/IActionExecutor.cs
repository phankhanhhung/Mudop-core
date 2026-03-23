namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime.Expressions;

/// <summary>
/// Interface for executing bound actions and functions.
/// Supports both C# interpretation and database execution strategies.
/// </summary>
public interface IActionExecutor
{
    /// <summary>
    /// Execute a bound action on an entity instance.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="action">Action definition.</param>
    /// <param name="entityId">Entity instance ID.</param>
    /// <param name="parameters">Action parameters.</param>
    /// <param name="context">Evaluation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Action result.</returns>
    Task<ActionResult> ExecuteActionAsync(
        BmEntity entity,
        BmAction action,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a bound function on an entity instance.
    /// </summary>
    /// <param name="entity">Entity definition.</param>
    /// <param name="function">Function definition.</param>
    /// <param name="entityId">Entity instance ID.</param>
    /// <param name="parameters">Function parameters.</param>
    /// <param name="context">Evaluation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Function result.</returns>
    Task<FunctionResult> ExecuteFunctionAsync(
        BmEntity entity,
        BmFunction function,
        Guid entityId,
        Dictionary<string, object?> parameters,
        EvaluationContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a function exists in the database (for SQL execution mode).
    /// </summary>
    Task<bool> IsFunctionDeployedAsync(BmEntity entity, string operationName, CancellationToken ct = default);
}

/// <summary>
/// Result of executing an action.
/// </summary>
public class ActionResult
{
    public bool Success { get; set; } = true;
    public object? Value { get; set; }
    public List<string> EmittedEvents { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object?> ComputedValues { get; set; } = new();
    public bool Rejected { get; set; }
    public string? RejectionMessage { get; set; }
    public bool HasReturned { get; set; }
}

/// <summary>
/// Result of executing a function.
/// </summary>
public class FunctionResult
{
    public bool Success { get; set; } = true;
    public object? Value { get; set; }
    public string? ErrorMessage { get; set; }
}
