namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.DataAccess;

/// <summary>
/// Result for media stream GET operations.
/// </summary>
public class MediaStreamResult
{
    public bool IsSuccess { get; init; }
    public byte[]? Content { get; init; }
    public string? ContentType { get; init; }
    public string? ETag { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; } = 200;
    public bool NotModified { get; init; }

    public static MediaStreamResult Success(byte[] content, string contentType, string? etag)
        => new() { IsSuccess = true, Content = content, ContentType = contentType, ETag = etag, StatusCode = 200 };

    public static MediaStreamResult Empty()
        => new() { IsSuccess = true, StatusCode = 204 };

    public static MediaStreamResult Error(string code, string message, int statusCode = 400)
        => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, StatusCode = statusCode };
}

/// <summary>
/// Handles entity media stream operations (OData v4 HasStream).
/// </summary>
public class MediaStreamService : IMediaStreamService
{
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<MediaStreamService> _logger;

    public MediaStreamService(
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<MediaStreamService> logger)
    {
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Get entity media stream content.
    /// </summary>
    public async Task<MediaStreamResult> GetMediaStreamAsync(
        BmEntity entityDef, Guid id, Guid? tenantId,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var tableName = _sqlBuilder.GetTableName(entityDef);
        var sql = $"SELECT _media_content, _media_content_type, _media_etag FROM {tableName} WHERE id = @id";
        var parameters = new List<Npgsql.NpgsqlParameter> { new("@id", id) };

        if (entityDef.TenantScoped)
        {
            sql += " AND tenant_id = @tenantId";
            parameters.Add(new Npgsql.NpgsqlParameter("@tenantId", tenantId));
        }

        var record = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        if (record == null)
            return MediaStreamResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        var content = record.GetValueOrDefault("MediaContent") as byte[];
        var contentType = record.GetValueOrDefault("MediaContentType")?.ToString();

        if (content == null || contentType == null)
            return MediaStreamResult.Empty();

        var etag = record.GetValueOrDefault("MediaEtag")?.ToString();

        // If-None-Match: return 304 Not Modified if content hasn't changed
        if (!string.IsNullOrEmpty(etag) && !string.IsNullOrEmpty(ifNoneMatch))
        {
            var clientEtag = ifNoneMatch.Trim('"', ' ');
            if (string.Equals(clientEtag, etag, StringComparison.Ordinal))
                return new MediaStreamResult { IsSuccess = true, NotModified = true, ETag = etag, StatusCode = 304 };
        }

        return MediaStreamResult.Success(content, contentType, etag);
    }

    /// <summary>
    /// Upload/replace entity media stream.
    /// </summary>
    public async Task<MediaStreamResult> UpdateMediaStreamAsync(
        BmEntity entityDef, Guid id, byte[] content, string contentType,
        Guid? tenantId, string? ifMatch = null,
        CancellationToken ct = default)
    {
        // Validate If-Match ETag
        if (!string.IsNullOrEmpty(ifMatch))
        {
            var clientEtag = ifMatch.Trim('"', ' ');
            if (clientEtag != "*")
            {
                var etagCheckResult = await CheckMediaETag(entityDef, id, tenantId, clientEtag, ct);
                if (etagCheckResult != null) return etagCheckResult;
            }
        }

        // Enforce max size
        if (content.Length > entityDef.MaxMediaSize)
            return MediaStreamResult.Error("MEDIA_TOO_LARGE",
                $"Media content exceeds maximum size of {entityDef.MaxMediaSize} bytes", 413);

        var etag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var data = new Dictionary<string, object?>
        {
            ["_media_content"] = content,
            ["_media_content_type"] = contentType,
            ["_media_etag"] = etag
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, effectiveTenantId);
        var rowsAffected = await _queryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);
        if (rowsAffected == 0)
            return MediaStreamResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        return new MediaStreamResult { IsSuccess = true, ETag = etag, StatusCode = 204 };
    }

    /// <summary>
    /// Delete entity media stream (set to null).
    /// </summary>
    public async Task<MediaStreamResult> DeleteMediaStreamAsync(
        BmEntity entityDef, Guid id, Guid? tenantId,
        string? ifMatch = null,
        CancellationToken ct = default)
    {
        // Validate If-Match ETag
        if (!string.IsNullOrEmpty(ifMatch))
        {
            var clientEtag = ifMatch.Trim('"', ' ');
            if (clientEtag != "*")
            {
                var etagCheckResult = await CheckMediaETag(entityDef, id, tenantId, clientEtag, ct);
                if (etagCheckResult != null) return etagCheckResult;
            }
        }

        var effectiveTenantId = entityDef.TenantScoped ? tenantId : null;
        var data = new Dictionary<string, object?>
        {
            ["_media_content"] = null,
            ["_media_content_type"] = null,
            ["_media_etag"] = null
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, effectiveTenantId);
        var rowsAffected = await _queryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);
        if (rowsAffected == 0)
            return MediaStreamResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        return new MediaStreamResult { IsSuccess = true, StatusCode = 204 };
    }

    private async Task<MediaStreamResult?> CheckMediaETag(
        BmEntity entityDef, Guid id, Guid? tenantId, string clientEtag,
        CancellationToken ct)
    {
        var tableName = _sqlBuilder.GetTableName(entityDef);
        var checkSql = $"SELECT _media_etag FROM {tableName} WHERE id = @id";
        var checkParams = new List<Npgsql.NpgsqlParameter> { new("@id", id) };
        if (entityDef.TenantScoped)
        {
            checkSql += " AND tenant_id = @tenantId";
            checkParams.Add(new Npgsql.NpgsqlParameter("@tenantId", tenantId));
        }
        var existing = await _queryExecutor.ExecuteSingleAsync(checkSql, checkParams, ct);
        if (existing == null)
            return MediaStreamResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);
        var currentEtag = existing.GetValueOrDefault("MediaEtag")?.ToString() ?? "";
        if (!string.Equals(clientEtag, currentEtag, StringComparison.Ordinal))
            return MediaStreamResult.Error("MEDIA_ETAG_MISMATCH",
                "Media ETag does not match. The media stream has been modified.", 412);
        return null;
    }
}
