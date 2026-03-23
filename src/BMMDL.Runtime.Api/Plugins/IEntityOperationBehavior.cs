using BMMDL.Runtime.Plugins;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Delegate representing the next behavior (or core handler) in the pipeline.
/// </summary>
public delegate Task<EntityOperationResult> EntityOperationDelegate();

/// <summary>
/// Wraps entity CRUD operations at the API level.
/// Russian doll pattern — each behavior wraps the next via next() delegation.
///
/// Inspired by: MediatR IPipelineBehavior&lt;TRequest, TResponse&gt;.
///
/// Behaviors can:
/// - Pre-process (validate, strip fields, check permissions)
/// - Post-process (set headers, transform result)
/// - Short-circuit (return early without calling next)
/// - Catch and handle errors from inner behaviors
/// </summary>
public interface IEntityOperationBehavior : IPlatformFeature
{
    /// <summary>
    /// Handle the operation by optionally pre-processing, calling next(), and post-processing.
    /// </summary>
    /// <param name="context">The operation context with entity, data, and request details.</param>
    /// <param name="next">The next behavior or core handler in the pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<EntityOperationResult> HandleAsync(
        EntityOperationContext context,
        EntityOperationDelegate next,
        CancellationToken ct);
}
