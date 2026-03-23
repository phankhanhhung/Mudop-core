namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Storage;
using BMMDL.Runtime.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/files/{module}/{entity}/{id:guid}/{field}")]
[Authorize]
public class FileStorageController : ControllerBase
{
    private readonly IFileStorageProvider _storage;
    private readonly IMetaModelCache _cache;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionChecker _permissionChecker;

    public FileStorageController(
        IFileStorageProvider storage,
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        IUnitOfWork unitOfWork,
        IPermissionChecker permissionChecker)
    {
        _storage = storage;
        _cache = cache;
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _unitOfWork = unitOfWork;
        _permissionChecker = permissionChecker;
    }

    [HttpPost]
    [RequestSizeLimit(50_000_000)] // 50MB
    public async Task<IActionResult> Upload(
        string module, string entity, Guid id, string field,
        IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ODataErrorResponse.FromException("VALIDATION_ERROR", "File attachment is required"));

        var entityDef = _cache.GetEntity($"{module}.{entity}") ?? _cache.GetEntity(entity);
        if (entityDef == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.EntityNotFound, $"Entity '{entity}' not found"));

        // Validate the field exists on the entity
        var fieldDef = entityDef.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));
        if (fieldDef == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.FieldNotFound, $"Field '{field}' not found on entity '{entity}'"));

        // Validate the field is a FileReference type
        if (fieldDef.TypeRef is not BMMDL.MetaModel.Types.BmFileReferenceType
            && !(fieldDef.TypeString?.Contains("FileReference", StringComparison.OrdinalIgnoreCase) ?? false))
            return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidFieldType, $"Field '{field}' is not a FileReference type"));

        // ACL permission check (Update operation — uploading modifies the entity record)
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update);
        if (permissionResult != null)
            return permissionResult;

        // Enforce @Storage.MaxSize annotation
        var maxSizeAnnotation = fieldDef.GetAnnotation("Storage.MaxSize");
        if (maxSizeAnnotation?.Value != null && long.TryParse(maxSizeAnnotation.Value.ToString(), out var maxSize))
        {
            if (file.Length > maxSize)
                return StatusCode(StatusCodes.Status413PayloadTooLarge,
                    ODataErrorResponse.FromException(ODataConstants.ErrorCodes.FileTooLarge, $"File size {file.Length} bytes exceeds maximum allowed size of {maxSize} bytes"));
        }

        // Enforce @Storage.AllowedTypes annotation
        var allowedTypesAnnotation = fieldDef.GetAnnotation("Storage.AllowedTypes");
        if (allowedTypesAnnotation?.Value != null)
        {
            var allowedTypes = ParseAllowedTypes(allowedTypesAnnotation.Value);
            if (allowedTypes.Count > 0 && !IsAllowedMimeType(file.ContentType, allowedTypes))
                return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                    ODataErrorResponse.FromException(ODataConstants.ErrorCodes.UnsupportedMediaType, $"File type '{file.ContentType}' is not allowed. Allowed types: {string.Join(", ", allowedTypes)}"));
        }

        // Verify the entity record exists
        var tenantId = HttpContext.GetTenantId();
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (selectSql, selectParams) = _sqlBuilder.BuildSelectQuery(entityDef, options, id);
        var record = await _queryExecutor.ExecuteSingleAsync(selectSql, selectParams, ct);
        if (record == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, $"Record with id '{id}' not found"));

        // H6: Verify record's tenant matches request tenant to prevent cross-tenant file access
        if (entityDef.TenantScoped && record.TryGetValue("TenantId", out var recordTenant))
        {
            var recordTenantId = recordTenant is Guid g ? g : (Guid.TryParse(recordTenant?.ToString(), out var parsed) ? parsed : Guid.Empty);
            if (recordTenantId != tenantId) return NotFound();
        }

        var userContext = HttpContext.GetUserContext();
        var userId = userContext?.UserId;

        // Use @Storage.Bucket annotation if present, otherwise derive from module-entity
        var bucketAnnotation = fieldDef.GetAnnotation("Storage.Bucket");
        var bucket = bucketAnnotation?.Value?.ToString() ?? $"{module}-{entity}".ToLowerInvariant();

        await _unitOfWork.BeginAsync(ct);
        try
        {
            await using var stream = file.OpenReadStream();
            // Sanitize filename to prevent directory traversal attacks
            var safeFileName = Path.GetFileName(file.FileName);
            var result = await _storage.UploadAsync(new FileUploadRequest
            {
                Bucket = bucket,
                FileName = safeFileName,
                ContentType = file.ContentType,
                Content = stream,
                ContentLength = file.Length,
                TenantId = tenantId,
                UserId = userId
            }, ct);

            // Update entity record with file metadata (snake_case column names)
            var snakeField = NamingConvention.ToSnakeCase(field);
            var updates = new Dictionary<string, object?>
            {
                [$"{snakeField}_provider"] = result.Provider,
                [$"{snakeField}_bucket"] = result.Bucket,
                [$"{snakeField}_key"] = result.Key,
                [$"{snakeField}_size"] = result.Size,
                [$"{snakeField}_mime_type"] = result.MimeType,
                [$"{snakeField}_checksum"] = result.Checksum,
                [$"{snakeField}_uploaded_at"] = result.UploadedAt,
                [$"{snakeField}_uploaded_by"] = result.UploadedBy
            };

            var (updateSql, updateParams) = _sqlBuilder.BuildUpdateQuery(entityDef, id, updates, effectiveTenantId);
            await _queryExecutor.ExecuteNonQueryAsync(updateSql, updateParams, ct);

            await _unitOfWork.CommitAsync(ct);

            return Ok(new
            {
                provider = result.Provider,
                bucket = result.Bucket,
                key = result.Key,
                size = result.Size,
                mimeType = result.MimeType,
                checksum = result.Checksum,
                uploadedAt = result.UploadedAt
            });
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Download(
        string module, string entity, Guid id, string field,
        CancellationToken ct)
    {
        var entityDef = _cache.GetEntity($"{module}.{entity}") ?? _cache.GetEntity(entity);
        if (entityDef == null)
            return NotFound();

        // ACL permission check (Read operation)
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Read);
        if (permissionResult != null)
            return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options, id);
        var record = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        if (record == null)
            return NotFound();

        // H6: Verify record's tenant matches request tenant to prevent cross-tenant file access
        if (entityDef.TenantScoped && record.TryGetValue("TenantId", out var dlTenant))
        {
            var dlTenantId = dlTenant is Guid g ? g : (Guid.TryParse(dlTenant?.ToString(), out var parsed) ? parsed : Guid.Empty);
            if (dlTenantId != tenantId) return NotFound();
        }

        // Extract file metadata (try both PascalCase and snake_case keys)
        var snakeField = NamingConvention.ToSnakeCase(field);
        var bucket = GetFieldValue<string>(record, $"{snakeField}_bucket", $"{field}Bucket");
        var key = GetFieldValue<string>(record, $"{snakeField}_key", $"{field}Key");

        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key))
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "No file uploaded for this field"));

        var result = await _storage.DownloadAsync(bucket, key, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        string module, string entity, Guid id, string field,
        CancellationToken ct)
    {
        var entityDef = _cache.GetEntity($"{module}.{entity}") ?? _cache.GetEntity(entity);
        if (entityDef == null)
            return NotFound();

        // ACL permission check (Update operation — deleting a file modifies the entity record)
        var permissionResult = await CheckPermissionAsync(entityDef, CrudOperation.Update);
        if (permissionResult != null)
            return permissionResult;

        var tenantId = HttpContext.GetTenantId();
        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var options = new QueryOptions { TenantId = effectiveTenantId };
        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options, id);
        var record = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        if (record == null)
            return NotFound();

        // H6: Verify record's tenant matches request tenant to prevent cross-tenant file access
        if (entityDef.TenantScoped && record.TryGetValue("TenantId", out var delTenant))
        {
            var delTenantId = delTenant is Guid g ? g : (Guid.TryParse(delTenant?.ToString(), out var parsed) ? parsed : Guid.Empty);
            if (delTenantId != tenantId) return NotFound();
        }

        var snakeField = NamingConvention.ToSnakeCase(field);
        var bucket = GetFieldValue<string>(record, $"{snakeField}_bucket", $"{field}Bucket");
        var key = GetFieldValue<string>(record, $"{snakeField}_key", $"{field}Key");

        await _unitOfWork.BeginAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(bucket) && !string.IsNullOrEmpty(key))
            {
                await _storage.DeleteAsync(bucket, key, ct);
            }

            // Clear file metadata on entity
            var updates = new Dictionary<string, object?>
            {
                [$"{snakeField}_provider"] = null,
                [$"{snakeField}_bucket"] = null,
                [$"{snakeField}_key"] = null,
                [$"{snakeField}_size"] = null,
                [$"{snakeField}_mime_type"] = null,
                [$"{snakeField}_checksum"] = null,
                [$"{snakeField}_uploaded_at"] = null,
                [$"{snakeField}_uploaded_by"] = null
            };

            var (updateSql, updateParams) = _sqlBuilder.BuildUpdateQuery(entityDef, id, updates, effectiveTenantId);
            await _queryExecutor.ExecuteNonQueryAsync(updateSql, updateParams, ct);

            await _unitOfWork.CommitAsync(ct);
            return NoContent();
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private static T? GetFieldValue<T>(Dictionary<string, object?> record, string snakeKey, string pascalKey)
    {
        if (record.TryGetValue(snakeKey, out var val) && val is T typed) return typed;
        if (record.TryGetValue(pascalKey, out val) && val is T typed2) return typed2;
        // Case-insensitive fallback
        var key = record.Keys.FirstOrDefault(k => string.Equals(k, snakeKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(k, pascalKey, StringComparison.OrdinalIgnoreCase));
        if (key != null && record[key] is T typed3) return typed3;
        return default;
    }

    private async Task<IActionResult?> CheckPermissionAsync(
        BMMDL.MetaModel.Structure.BmEntity entityDef,
        CrudOperation operation)
    {
        var userContext = HttpContext.GetUserContext();
        if (userContext == null)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied, "No authenticated user context available."));

        var evalContext = new EvaluationContext
        {
            TenantId = HttpContext.GetTenantId(),
            User = new UserContext
            {
                Id = userContext.UserId,
                Username = userContext.Username,
                Email = userContext.Email,
                TenantId = userContext.TenantId,
                Roles = userContext.Roles.ToList()
            }
        };

        var decision = await _permissionChecker.CheckAccessAsync(entityDef, operation, userContext, null, evalContext);
        if (!decision.IsAllowed)
            return StatusCode(StatusCodes.Status403Forbidden, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied, decision.DeniedReason ?? $"Access denied for {operation} on {entityDef.Name}."));

        return null;
    }

    /// <summary>
    /// Parse the @Storage.AllowedTypes annotation value into a list of MIME type strings.
    /// Handles both list objects and string representations like "['application/pdf', 'image/*']".
    /// </summary>
    private static List<string> ParseAllowedTypes(object value)
    {
        if (value is IEnumerable<object> list)
            return list.Select(v => v.ToString()!.Trim('\'', '"')).Where(t => !string.IsNullOrEmpty(t)).ToList();

        return value.ToString()!
            .Trim('[', ']')
            .Split(',')
            .Select(t => t.Trim().Trim('\'', '"'))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    /// <summary>
    /// Check if a MIME type matches any of the allowed type patterns.
    /// Supports wildcard patterns like 'image/*'.
    /// </summary>
    private static bool IsAllowedMimeType(string contentType, List<string> allowedTypes)
    {
        foreach (var pattern in allowedTypes)
        {
            if (pattern == "*/*" || pattern == "*")
                return true;

            if (pattern.EndsWith("/*"))
            {
                // Wildcard pattern: match the type prefix (e.g., 'image/*' matches 'image/png')
                var prefix = pattern[..^2]; // Remove /*
                if (contentType.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(contentType, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
