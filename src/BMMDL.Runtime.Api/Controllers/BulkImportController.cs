namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

/// <summary>
/// Bulk import endpoint for efficient multi-record insertion.
/// POST /api/odata/{module}/{entitySet}/$bulk-import
/// </summary>
[ApiController]
[Authorize]
[Route("api/odata/{module}")]
[Tags("Import")]
public class BulkImportController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ILogger<BulkImportController> _logger;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public BulkImportController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IUnitOfWork unitOfWork,
        IPermissionChecker permissionChecker,
        ILogger<BulkImportController> logger)
    {
        _cacheManager = cacheManager;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _unitOfWork = unitOfWork;
        _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        _logger = logger;
    }

    /// <summary>
    /// Import multiple records for an entity in a single request.
    /// Each record is inserted individually; failures are tracked per row.
    /// </summary>
    [HttpPost("{entitySet}/$bulk-import")]
    [ProducesResponseType(typeof(BulkImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkImport(
        [FromRoute] string module,
        [FromRoute] string entitySet,
        [FromBody] BulkImportRequest request,
        CancellationToken ct = default)
    {
        if (request?.Records == null || request.Records.Count == 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "BULK_IMPORT_EMPTY",
                "Bulk import request must contain at least one record"));
        }

        // Resolve entity definition
        var qualifiedName = $"{module}.{entitySet}";
        var cache = await GetCacheAsync();
        var entityDef = cache.GetEntity(qualifiedName) ?? cache.GetEntity(entitySet);
        if (entityDef == null)
        {
            throw new EntityNotFoundException(qualifiedName);
        }

        // Check create permission before allowing bulk import
        var userContext = HttpContext.GetUserContext();
        if (userContext == null)
        {
            return StatusCode(403, ODataErrorResponse.FromException(
                "AccessDenied", "No authenticated user context available."));
        }

        var decision = await _permissionChecker.CheckAccessAsync(
            entityDef, CrudOperation.Create, userContext, null, null);
        if (!decision.IsAllowed)
        {
            _logger.LogWarning(
                "Bulk import access denied: Create on {Entity} for user {User}. Reason: {Reason}",
                entityDef.QualifiedName, userContext.Username, decision.DeniedReason);

            return StatusCode(403, ODataErrorResponse.FromException(
                "AccessDenied", decision.DeniedReason ?? $"Access denied for Create on {entityDef.Name}."));
        }

        var tenantId = HttpContext.GetTenantId();
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : (Guid?)null;

        _logger.LogInformation(
            "Bulk importing {Count} records into {Module}.{Entity} for tenant {TenantId}",
            request.Records.Count, module, entitySet, tenantId);

        var errors = new List<BulkImportError>();
        var successCount = 0;

        await _unitOfWork.BeginAsync(ct);
        try
        {
            for (var i = 0; i < request.Records.Count; i++)
            {
                var record = ConvertRecord(request.Records[i]);
                if (record == null || record.Count == 0)
                {
                    errors.Add(new BulkImportError
                    {
                        RowIndex = i,
                        Message = "Record is empty or invalid",
                        Data = request.Records[i] ?? new Dictionary<string, object?>()
                    });

                    if (request.StopOnError)
                    {
                        await _unitOfWork.RollbackAsync(ct);
                        return Ok(new BulkImportResult
                        {
                            TotalRecords = request.Records.Count,
                            SuccessCount = successCount,
                            ErrorCount = errors.Count,
                            Errors = errors
                        });
                    }
                    continue;
                }

                // Strip computed/readonly fields
                StripComputedFields(entityDef, record);

                try
                {
                    var (sql, parameters) = _sqlBuilder.BuildInsertQuery(entityDef, record, effectiveTenantId);
                    await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bulk import row {Index} failed for {Entity}", i, entitySet);
                    errors.Add(new BulkImportError
                    {
                        RowIndex = i,
                        Message = "Row import failed. Check server logs for details.",
                        Data = record
                    });

                    if (request.StopOnError)
                    {
                        await _unitOfWork.RollbackAsync(ct);
                        return Ok(new BulkImportResult
                        {
                            TotalRecords = request.Records.Count,
                            SuccessCount = successCount,
                            ErrorCount = errors.Count,
                            Errors = errors
                        });
                    }
                }
            }

            await _unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Bulk import completed for {Module}.{Entity}: {Success} succeeded, {Failed} failed",
            module, entitySet, successCount, errors.Count);

        return Ok(new BulkImportResult
        {
            TotalRecords = request.Records.Count,
            SuccessCount = successCount,
            ErrorCount = errors.Count,
            Errors = errors
        });
    }

    private static void StripComputedFields(BmEntity entityDef, Dictionary<string, object?> data)
    {
        foreach (var field in entityDef.Fields)
        {
            if (field.IsComputed || field.IsVirtual)
            {
                data.Remove(field.Name);
            }
        }
    }

    private static Dictionary<string, object?>? ConvertRecord(Dictionary<string, object?> record)
    {
        // JsonElement values need conversion to proper .NET types
        var result = new Dictionary<string, object?>(record.Count);
        foreach (var (key, value) in record)
        {
            result[key] = value is JsonElement je ? ConvertJsonElement(je) : value;
        }
        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }
}

// ---------------------------------------------------------------------------
// Request / Response Models
// ---------------------------------------------------------------------------

public class BulkImportRequest
{
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public bool StopOnError { get; set; } = false;
}

public class BulkImportResult
{
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<BulkImportError> Errors { get; set; } = new();
}

public class BulkImportError
{
    public int RowIndex { get; set; }
    public string Message { get; set; } = "";
    public Dictionary<string, object?> Data { get; set; } = new();
}
