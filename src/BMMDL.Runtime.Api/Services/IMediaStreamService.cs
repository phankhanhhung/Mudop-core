namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;

/// <summary>
/// Interface for entity media stream operations (OData v4 HasStream).
/// </summary>
public interface IMediaStreamService
{
    /// <summary>
    /// Get entity media stream content.
    /// </summary>
    Task<MediaStreamResult> GetMediaStreamAsync(
        BmEntity entityDef, Guid id, Guid? tenantId,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Upload/replace entity media stream.
    /// </summary>
    Task<MediaStreamResult> UpdateMediaStreamAsync(
        BmEntity entityDef, Guid id, byte[] content, string contentType,
        Guid? tenantId, string? ifMatch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete entity media stream (set to null).
    /// </summary>
    Task<MediaStreamResult> DeleteMediaStreamAsync(
        BmEntity entityDef, Guid id, Guid? tenantId,
        string? ifMatch = null,
        CancellationToken ct = default);
}
