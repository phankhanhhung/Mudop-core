namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Controller for async operation status monitoring.
/// OData v4: Supports Prefer: respond-async pattern.
/// </summary>
[ApiController]
[Authorize]
[Route("api/odata/$operations")]
[Tags("Async Operations")]
public class AsyncOperationController : ControllerBase
{
    private readonly IAsyncOperationService _operationService;
    private readonly ILogger<AsyncOperationController> _logger;

    public AsyncOperationController(
        IAsyncOperationService operationService,
        ILogger<AsyncOperationController> logger)
    {
        _operationService = operationService;
        _logger = logger;
    }

    /// <summary>
    /// Get status of an async operation.
    /// OData v4 status monitor endpoint.
    /// </summary>
    /// <param name="operationId">The operation ID returned in Location header.</param>
    /// <returns>
    /// 200 OK with result if completed.
    /// 202 Accepted if still running.
    /// 404 Not Found if operation doesn't exist.
    /// </returns>
    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetOperationStatus([FromRoute] Guid operationId)
    {
        var operation = _operationService.GetOperation(operationId);

        if (operation == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "OPERATION_NOT_FOUND",
                $"Async operation '{operationId}' not found or has expired",
                operationId.ToString()));
        }

        // Tenant ownership check: prevent cross-tenant operation access
        var tenantId = HttpContext.GetTenantId();
        if (operation.TenantId != Guid.Empty && operation.TenantId != tenantId)
        {
            return NotFound(ODataErrorResponse.FromException(
                "OPERATION_NOT_FOUND",
                $"Async operation '{operationId}' not found or has expired",
                operationId.ToString()));
        }

        // Build response based on operation state
        var response = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{Request.Scheme}://{Request.Host}/api/odata/$metadata#AsyncOperation",
            ["id"] = operation.OperationId,
            ["status"] = operation.Status.ToString().ToLowerInvariant(),
            ["operationType"] = operation.OperationType,
            ["createdAt"] = operation.CreatedAt,
            ["percentComplete"] = operation.PercentComplete
        };

        if (operation.EntityId.HasValue)
        {
            response["entityId"] = operation.EntityId;
        }

        switch (operation.Status)
        {
            case OperationState.Running:
                // Still processing - return 202 with Retry-After
                Response.Headers["Retry-After"] = "1"; // Check again in 1 second
                return AcceptedAtAction(
                    nameof(GetOperationStatus),
                    new { operationId },
                    response);

            case OperationState.Succeeded:
                response["completedAt"] = operation.CompletedAt;
                if (operation.Result != null)
                {
                    response["result"] = operation.Result;
                }
                return Ok(response);

            case OperationState.Failed:
                response["completedAt"] = operation.CompletedAt;
                response["error"] = operation.Error;
                return Ok(response); // Return 200 with error details in body

            default:
                return Ok(response);
        }
    }

    /// <summary>
    /// Delete/cancel an async operation.
    /// </summary>
    [HttpDelete("{operationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult CancelOperation([FromRoute] Guid operationId)
    {
        var operation = _operationService.GetOperation(operationId);

        if (operation == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                "OPERATION_NOT_FOUND",
                $"Async operation '{operationId}' not found",
                operationId.ToString()));
        }

        // Tenant ownership check: prevent cross-tenant operation cancellation
        var tenantId = HttpContext.GetTenantId();
        if (operation.TenantId != Guid.Empty && operation.TenantId != tenantId)
        {
            return NotFound(ODataErrorResponse.FromException(
                "OPERATION_NOT_FOUND",
                $"Async operation '{operationId}' not found",
                operationId.ToString()));
        }

        if (operation.Status == OperationState.Running)
        {
            _operationService.FailOperation(operationId, "Cancelled by user");
        }

        return NoContent();
    }
}
