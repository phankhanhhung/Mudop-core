namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Helpers;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// OData v4 $batch endpoint controller.
/// Handles multiple operations in a single HTTP request.
/// Delegates write operations to EntityWriteService.
/// </summary>
/// <example>
/// POST /api/odata/$batch
/// {
///   "requests": [
///     { "id": "1", "method": "POST", "url": "/Platform/Order", "body": {...} },
///     { "id": "2", "method": "GET", "url": "/Platform/Product?$top=5" }
///   ]
/// }
/// </example>
[ApiController]
[Authorize]
[Route("api/odata/$batch")]
[Tags("Batch")]
public class BatchController : ControllerBase
{
    private const int MaxBatchRequests = 1000;

    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<BatchController> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IEntityWriteService _writeService;
    private readonly IEntityResolver _entityResolver;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public BatchController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<BatchController> logger,
        IUnitOfWork unitOfWork,
        IPermissionChecker permissionChecker,
        IEntityWriteService writeService,
        IEntityResolver entityResolver)
    {
        _cacheManager = cacheManager;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _logger = logger;
        _unitOfWork = unitOfWork;
        _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        _writeService = writeService;
        _entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
    }

    /// <summary>
    /// Execute multiple OData operations in a single request.
    /// Supports atomicity groups (M7): requests in the same group execute within a transaction.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteBatch(
        [FromBody] BatchRequest batchRequest,
        CancellationToken ct = default)
    {
        if (batchRequest?.Requests == null || batchRequest.Requests.Count == 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "BATCH_EMPTY",
                "Batch request must contain at least one request"));
        }

        if (batchRequest.Requests.Count > MaxBatchRequests)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "BATCH_TOO_LARGE",
                $"Batch must not exceed {MaxBatchRequests} requests"));
        }

        // Validate that all DependsOn references point to existing request IDs in the batch
        var allIds = new HashSet<string>(batchRequest.Requests.Select(r => r.Id));
        foreach (var req in batchRequest.Requests)
        {
            if (req.DependsOn?.Any(depId => !allIds.Contains(depId)) == true)
            {
                var invalidDeps = req.DependsOn.Where(depId => !allIds.Contains(depId));
                return BadRequest(ODataErrorResponse.FromException(
                    "BATCH_INVALID_DEPENDENCY",
                    $"Request '{req.Id}' depends on non-existent request ID(s): {string.Join(", ", invalidDeps)}"));
            }
        }

        // M31: Detect circular dependencies before processing
        var cycleError = DependencyGraphValidator.DetectCycles(
            batchRequest.Requests.Select(r => (r.Id, r.DependsOn)).ToList());
        if (cycleError != null)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "BATCH_CIRCULAR_DEPENDENCY",
                cycleError));
        }

        // Limit dependency chain depth to prevent excessively deep execution graphs
        const int maxDependencyDepth = 20;
        var depthError = DependencyGraphValidator.CheckMaxDepth(
            batchRequest.Requests.Select(r => (r.Id, r.DependsOn)).ToList(),
            maxDependencyDepth);
        if (depthError != null)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "BATCH_DEPENDENCY_TOO_DEEP",
                depthError));
        }

        _logger.LogInformation("Processing $batch with {Count} requests", batchRequest.Requests.Count);

        var responses = new List<BatchResponseItem>();
        var completedRequests = new Dictionary<string, BatchResponseItem>();

        // Start UoW transaction if batch contains any write operations
        var hasWriteOperations = batchRequest.Requests.Any(r =>
            !string.Equals(r.Method, "GET", StringComparison.OrdinalIgnoreCase));

        if (hasWriteOperations)
        {
            await _unitOfWork.BeginAsync(ct);
        }

        try
        {
            // Group requests by atomicity group
            var groups = batchRequest.Requests
                .Select((r, i) => (Request: r, Index: i))
                .GroupBy(x => x.Request.AtomicityGroup ?? $"__independent_{x.Index}")
                .ToList();

            foreach (var group in groups)
            {
                var isAtomicGroup = !group.Key.StartsWith("__independent_");
                var groupRequests = group.Select(x => x.Request).ToList();

                if (isAtomicGroup)
                {
                    // Execute group within the shared transaction — all succeed or all fail
                    var groupResponses = await ExecuteAtomicGroupAsync(groupRequests, completedRequests, ct);
                    responses.AddRange(groupResponses);

                    // If atomic group failed (424 responses), the PostgreSQL transaction is
                    // in an aborted state — rollback and stop processing further requests
                    if (groupResponses.Any(r => r.Status >= 400))
                    {
                        if (_unitOfWork.IsStarted)
                        {
                            await _unitOfWork.RollbackAsync(ct);
                        }
                        _logger.LogWarning("$batch aborted: atomic group failure caused transaction rollback");
                        return Ok(new BatchResponse { Responses = responses });
                    }

                    foreach (var r in groupResponses)
                    {
                        completedRequests[r.Id] = r;
                    }
                }
                else
                {
                    // Independent request
                    var request = groupRequests[0];

                    // Check dependencies
                    if (request.DependsOn != null && request.DependsOn.Length > 0)
                    {
                        var missingDeps = request.DependsOn.Where(d => !completedRequests.ContainsKey(d)).ToList();
                        if (missingDeps.Count > 0)
                        {
                            var failResponse = new BatchResponseItem
                            {
                                Id = request.Id,
                                Status = 424,
                                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchMissingDependency, $"Missing dependencies: {string.Join(", ", missingDeps)}")
                            };
                            responses.Add(failResponse);
                            completedRequests[request.Id] = failResponse;
                            continue;
                        }
                    }

                    // Use savepoints for independent writes to prevent a single failure
                    // from aborting the PostgreSQL transaction (25P02 error cascade)
                    var isWrite = !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase);
                    var savepointName = $"batch_ind_{request.Id}";

                    if (isWrite && _unitOfWork.IsStarted)
                    {
                        await _unitOfWork.SavepointAsync(savepointName, ct);
                    }

                    BatchResponseItem response;
                    try
                    {
                        response = await ExecuteSingleRequestAsync(request, ct);

                        // If the write returned an error status, rollback to savepoint
                        // to keep the transaction clean for subsequent requests
                        if (isWrite && _unitOfWork.IsStarted && response.Status >= 400)
                        {
                            await _unitOfWork.RollbackToSavepointAsync(savepointName, ct);
                        }
                        else if (isWrite && _unitOfWork.IsStarted)
                        {
                            await _unitOfWork.ReleaseSavepointAsync(savepointName, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (isWrite && _unitOfWork.IsStarted)
                        {
                            await _unitOfWork.RollbackToSavepointAsync(savepointName, ct);
                        }
                        _logger.LogError(ex, "Batch independent request {Id} failed, rolled back savepoint", request.Id);
                        response = new BatchResponseItem
                        {
                            Id = request.Id,
                            Status = 500,
                            Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InternalError, "An internal error occurred while processing the request")
                        };
                    }

                    responses.Add(response);
                    completedRequests[request.Id] = response;
                }
            }

            // Commit all write operations atomically
            if (_unitOfWork.IsStarted)
            {
                await _unitOfWork.CommitAsync(ct);
            }

            _logger.LogInformation("$batch completed: {Success} success, {Failed} failed",
                responses.Count(r => r.Status >= 200 && r.Status < 300),
                responses.Count(r => r.Status >= 400));

            return Ok(new BatchResponse { Responses = responses });
        }
        catch
        {
            if (_unitOfWork.IsStarted)
            {
                await _unitOfWork.RollbackAsync(ct);
            }
            throw;
        }
    }

    /// <summary>
    /// Execute a group of requests within a database transaction.
    /// If any request fails, all are rolled back and return status 424.
    /// </summary>
    private async Task<List<BatchResponseItem>> ExecuteAtomicGroupAsync(
        List<BatchRequestItem> requests,
        Dictionary<string, BatchResponseItem> completedRequests,
        CancellationToken ct)
    {
        var groupResponses = new List<BatchResponseItem>();

        try
        {
            // Execute each request, collecting responses
            foreach (var request in requests)
            {
                // Check dependencies
                if (request.DependsOn != null && request.DependsOn.Length > 0)
                {
                    var allCompleted = completedRequests.Keys
                        .Concat(groupResponses.Select(r => r.Id));
                    var missingDeps = request.DependsOn
                        .Where(d => !allCompleted.Contains(d))
                        .ToList();
                    if (missingDeps.Count > 0)
                    {
                        // Fail the entire group
                        throw new InvalidOperationException(
                            $"Request '{request.Id}' has unmet dependencies: {string.Join(", ", missingDeps)}");
                    }
                }

                var response = await ExecuteSingleRequestAsync(request, ct);

                // If any request fails, rollback the entire group
                if (response.Status >= 400)
                {
                    throw new InvalidOperationException(
                        $"Request '{request.Id}' failed with status {response.Status}");
                }

                groupResponses.Add(response);
            }

            return groupResponses;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Atomicity group failed, rolling back all {Count} requests", requests.Count);

            // Return 424 Failed Dependency for all requests in the group
            return requests.Select(r => new BatchResponseItem
            {
                Id = r.Id,
                Status = 424,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchAtomicityRollback, "Rolled back due to atomicity group failure")
            }).ToList();
        }
    }

    /// <summary>
    /// Execute a single request within the batch.
    /// </summary>
    private async Task<BatchResponseItem> ExecuteSingleRequestAsync(
        BatchRequestItem request,
        CancellationToken ct)
    {
        try
        {
            // Parse URL: /Module/Entity or /Module/Entity(id) or /Module/Entity?query
            var (module, entity, id, queryString) = ParseUrl(request.Url);

            if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(entity))
            {
                return new BatchResponseItem
                {
                    Id = request.Id,
                    Status = 400,
                    Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchInvalidUrl, $"Invalid URL format: {request.Url}")
                };
            }

            var entityDef = await GetEntityDefinitionAsync(module, entity);
            if (entityDef == null)
            {
                return new BatchResponseItem
                {
                    Id = request.Id,
                    Status = 404,
                    Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.EntityNotFound, $"Entity not found: {module}.{entity}")
                };
            }

            return request.Method.ToUpperInvariant() switch
            {
                "GET" => await ExecuteGetAsync(request.Id, entityDef, id, queryString, ct),
                "POST" => await ExecutePostAsync(request.Id, module, entity, entityDef, request.Body, ct, request.Headers),
                "PATCH" or "PUT" => await ExecutePatchAsync(request.Id, module, entity, entityDef, id, request.Body, ct, request.Headers),
                "DELETE" => await ExecuteDeleteAsync(request.Id, module, entity, entityDef, id, ct, request.Headers),
                _ => new BatchResponseItem
                {
                    Id = request.Id,
                    Status = 405,
                    Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchUnsupportedMethod, $"Unsupported method: {request.Method}")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch request {Id} failed: {Url}", request.Id, request.Url);

            // Avoid leaking database internals to the client
            var clientMessage = ex switch
            {
                Npgsql.PostgresException => "A database error occurred while processing the request.",
                Npgsql.NpgsqlException => "A database error occurred while processing the request.",
                _ => "An internal error occurred while processing the request"
            };

            return new BatchResponseItem
            {
                Id = request.Id,
                Status = 500,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InternalError, clientMessage)
            };
        }
    }

    private async Task<BatchResponseItem> ExecuteGetAsync(
        string requestId, BmEntity entityDef, Guid? id, string? queryString,
        CancellationToken ct)
    {
        // Access control check
        var denied = await CheckBatchPermissionAsync(requestId, entityDef, CrudOperation.Read);
        if (denied != null) return denied;

        var tenantId = entityDef.TenantScoped ? HttpContext.GetTenantId() : null;
        var options = ParseQueryOptions(queryString, tenantId);

        if (id.HasValue)
        {
            var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options, id.Value);
            var result = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);

            return new BatchResponseItem
            {
                Id = requestId,
                Status = result != null ? 200 : 404,
                Body = result
            };
        }
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options);
            var results = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);

            return new BatchResponseItem
            {
                Id = requestId,
                Status = 200,
                Body = new { value = results }
            };
        }
    }

    private async Task<BatchResponseItem> ExecutePostAsync(
        string requestId, string module, string entity, BmEntity entityDef, object? body,
        CancellationToken ct, Dictionary<string, string>? requestHeaders = null)
    {
        var data = ConvertBody(body);
        if (data == null || data.Count == 0)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 400,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchEmptyBody, "Request body cannot be empty")
            };
        }

        // Access control check (stays in controller — needs HttpContext)
        var denied = await CheckBatchPermissionAsync(requestId, entityDef, CrudOperation.Create, data);
        if (denied != null) return denied;

        // Delegate to EntityWriteService
        var context = BuildRequestContext();
        var result = await _writeService.CreateAsync(entityDef, module, entity, data, context, ct);

        return MapResultToResponseItem(requestId, result, requestHeaders);
    }

    private async Task<BatchResponseItem> ExecutePatchAsync(
        string requestId, string module, string entity, BmEntity entityDef, Guid? id, object? body,
        CancellationToken ct, Dictionary<string, string>? requestHeaders = null)
    {
        if (!id.HasValue)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 400,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchMissingId, "PATCH requires entity ID in URL")
            };
        }

        var data = ConvertBody(body);
        if (data == null || data.Count == 0)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 400,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchEmptyBody, "Request body cannot be empty")
            };
        }

        // Access control check (stays in controller — needs HttpContext)
        var denied = await CheckBatchPermissionAsync(requestId, entityDef, CrudOperation.Update, data);
        if (denied != null) return denied;

        // Extract If-Match ETag from per-request headers
        var ifMatch = requestHeaders != null && requestHeaders.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal : null;

        // Delegate to EntityWriteService
        var context = BuildRequestContext();
        var result = await _writeService.UpdateAsync(entityDef, module, entity, id.Value, data, context, ifMatch, ct);

        return MapResultToResponseItem(requestId, result, requestHeaders);
    }

    private async Task<BatchResponseItem> ExecuteDeleteAsync(
        string requestId, string module, string entity, BmEntity entityDef, Guid? id,
        CancellationToken ct, Dictionary<string, string>? requestHeaders = null)
    {
        if (!id.HasValue)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 400,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.BatchMissingId, "DELETE requires entity ID in URL")
            };
        }

        // Access control check (stays in controller — needs HttpContext)
        var denied = await CheckBatchPermissionAsync(requestId, entityDef, CrudOperation.Delete);
        if (denied != null) return denied;

        // Extract If-Match ETag from per-request headers
        var ifMatch = requestHeaders != null && requestHeaders.TryGetValue("If-Match", out var ifMatchVal) ? ifMatchVal : null;

        // Delegate to EntityWriteService
        var context = BuildRequestContext();
        var result = await _writeService.DeleteAsync(entityDef, module, entity, id.Value, context, soft: false, ifMatch, ct);

        return MapResultToResponseItem(requestId, result, requestHeaders);
    }

    #region Helpers

    /// <summary>
    /// Map EntityOperationResult to BatchResponseItem, honoring Prefer headers and ETag.
    /// </summary>
    private static BatchResponseItem MapResultToResponseItem(
        string requestId, EntityOperationResult result, Dictionary<string, string>? requestHeaders)
    {
        if (!result.IsSuccess)
        {
            var errorHeaders = new Dictionary<string, string>();
            // Include ETag on 412 PreconditionFailed
            if (result.StatusCode == 412 && result.Data?.TryGetValue("ETag", out var etagVal) == true)
                errorHeaders["ETag"] = etagVal?.ToString() ?? "";

            return new BatchResponseItem
            {
                Id = requestId,
                Status = result.StatusCode,
                Body = ODataErrorResponse.FromException(result.ErrorCode ?? ODataConstants.ErrorCodes.UnknownError, result.ErrorMessage ?? "An error occurred"),
                Headers = errorHeaders.Count > 0 ? errorHeaders : null
            };
        }

        // 204 No Content (delete, or no data)
        if (result.StatusCode == 204 || result.Data == null)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = result.StatusCode,
                Body = null
            };
        }

        // H41: Honor Prefer header on individual batch operations
        var (returnMinimal, _, _) = ParseBatchPreferHeader(requestHeaders);
        var responseHeaders = new Dictionary<string, string>();
        var etag = ETagGenerator.GenerateWeakETag(result.Data);
        responseHeaders["ETag"] = etag;

        if (returnMinimal)
        {
            responseHeaders[ODataConstants.Headers.PreferenceApplied] = ODataConstants.PreferValues.ReturnMinimal;
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 204,
                Body = null,
                Headers = responseHeaders
            };
        }

        return new BatchResponseItem
        {
            Id = requestId,
            Status = result.StatusCode,
            Body = result.Data,
            Headers = responseHeaders
        };
    }

    private RequestContext BuildRequestContext()
    {
        var userContext = HttpContext.GetUserContext();
        return new RequestContext(
            HttpContext.GetTenantId(),
            userContext?.UserId,
            Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            null, // batch doesn't support per-item locale
            userContext
        );
    }

    private (string? module, string? entity, Guid? id, string? queryString) ParseUrl(string url)
    {
        // Remove leading /api/odata/ if present
        var path = url.TrimStart('/');
        if (path.StartsWith("api/odata/", StringComparison.OrdinalIgnoreCase))
            path = path.Substring(10);

        // Split query string
        var queryIndex = path.IndexOf('?');
        string? queryString = null;
        if (queryIndex >= 0)
        {
            queryString = path.Substring(queryIndex + 1);
            path = path.Substring(0, queryIndex);
        }

        // Parse path: Module/Entity, Module/Entity(guid), or Module/Entity/guid
        var match = Regex.Match(path, @"^([^/]+)/([^/(]+)(?:\(([^)]+)\)|/([^/?]+))?$");
        if (!match.Success)
            return (null, null, null, null);

        var module = match.Groups[1].Value;
        var entity = match.Groups[2].Value;
        Guid? id = null;

        // Check parens format (group 3) or segment format (group 4)
        var idGroup = match.Groups[3].Success ? match.Groups[3] : match.Groups[4];
        if (idGroup.Success)
        {
            var idStr = idGroup.Value.Trim('\'', '"');
            if (Guid.TryParse(idStr, out var parsedId))
                id = parsedId;
        }

        return (module, entity, id, queryString);
    }

    private async Task<BmEntity?> GetEntityDefinitionAsync(string module, string entity)
    {
        return await _entityResolver.ResolveEntityAsync(module, entity);
    }

    private QueryOptions ParseQueryOptions(string? queryString, Guid? tenantId)
    {
        var options = new QueryOptions { TenantId = tenantId };

        if (string.IsNullOrEmpty(queryString))
            return options;

        var parts = queryString.Split('&');
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].TrimStart('$').ToLowerInvariant();
            var value = Uri.UnescapeDataString(kv[1]);

            switch (key)
            {
                case "filter": options = options with { Filter = value }; break;
                case "orderby": options = options with { OrderBy = value }; break;
                case "select": options = options with { Select = value }; break;
                case "top": options = options with { Top = int.TryParse(value, out var t) ? t : QueryConstants.DefaultBatchPageSize }; break;
                case "skip": options = options with { Skip = int.TryParse(value, out var s) ? s : 0 }; break;
            }
        }

        return options;
    }

    private Dictionary<string, object?>? ConvertBody(object? body)
    {
        if (body == null) return null;

        if (body is Dictionary<string, object?> dict)
            return dict;

        if (body is JsonElement je && je.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());

        return null;
    }

    /// <summary>
    /// Check access control permission for the given entity and operation.
    /// Returns null if allowed, or a 403 BatchResponseItem if denied.
    /// </summary>
    private async Task<BatchResponseItem?> CheckBatchPermissionAsync(
        string requestId,
        BmEntity entityDef,
        CrudOperation operation,
        Dictionary<string, object?>? data = null)
    {
        var userContext = HttpContext.GetUserContext();
        if (userContext == null)
        {
            return new BatchResponseItem
            {
                Id = requestId,
                Status = 403,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied, "No authenticated user context available.")
            };
        }

        var evalContext = BuildRequestContext().ToEvaluationContext();
        var decision = await _permissionChecker.CheckAccessAsync(
            entityDef, operation, userContext, data, evalContext);

        if (!decision.IsAllowed)
        {
            _logger.LogWarning(
                "Batch access denied: {Operation} on {Entity} for user {User}. Reason: {Reason}",
                operation, entityDef.QualifiedName, userContext.Username, decision.DeniedReason);

            return new BatchResponseItem
            {
                Id = requestId,
                Status = 403,
                Body = ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied, decision.DeniedReason ?? $"Access denied for {operation} on {entityDef.Name}.")
            };
        }

        return null; // Allowed
    }

    /// <summary>
    /// Parse the Prefer header from per-request headers in a batch item.
    /// </summary>
    private static (bool returnMinimal, bool returnRepresentation, int? maxPageSize) ParseBatchPreferHeader(
        Dictionary<string, string>? headers)
    {
        bool returnMinimal = false;
        bool returnRepresentation = false;
        int? maxPageSize = null;

        if (headers == null)
            return (returnMinimal, returnRepresentation, maxPageSize);

        // Check for "Prefer" header (case-insensitive lookup)
        var preferValue = headers.FirstOrDefault(h =>
            h.Key.Equals("Prefer", StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrEmpty(preferValue))
            return (returnMinimal, returnRepresentation, maxPageSize);

        foreach (var prefer in preferValue.Split(',', StringSplitOptions.TrimEntries))
        {
            if (prefer.Equals(ODataConstants.PreferValues.ReturnMinimal, StringComparison.OrdinalIgnoreCase))
            {
                returnMinimal = true;
            }
            else if (prefer.Equals(ODataConstants.PreferValues.ReturnRepresentation, StringComparison.OrdinalIgnoreCase))
            {
                returnRepresentation = true;
            }
            else if (prefer.StartsWith(ODataConstants.PreferValues.MaxPageSize + "=", StringComparison.OrdinalIgnoreCase))
            {
                var val = prefer[(ODataConstants.PreferValues.MaxPageSize + "=").Length..];
                if (int.TryParse(val, out var mps) && mps > 0)
                {
                    maxPageSize = mps;
                }
            }
            else if (prefer.StartsWith("maxpagesize=", StringComparison.OrdinalIgnoreCase))
            {
                var val = prefer["maxpagesize=".Length..];
                if (int.TryParse(val, out var mps) && mps > 0)
                {
                    maxPageSize = mps;
                }
            }
        }

        return (returnMinimal, returnRepresentation, maxPageSize);
    }

    #endregion
}
