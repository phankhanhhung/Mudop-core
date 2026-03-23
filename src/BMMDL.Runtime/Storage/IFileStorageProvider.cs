namespace BMMDL.Runtime.Storage;

public interface IFileStorageProvider
{
    string ProviderName { get; }
    Task<FileUploadResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default);
    Task<FileDownloadResult> DownloadAsync(string bucket, string key, CancellationToken ct = default);
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);
}

public record FileUploadRequest
{
    public required string Bucket { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required Stream Content { get; init; }
    public long ContentLength { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
}

public record FileUploadResult
{
    public required string Provider { get; init; }
    public required string Bucket { get; init; }
    public required string Key { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }
    public required string Checksum { get; init; }
    public required DateTime UploadedAt { get; init; }
    public Guid? UploadedBy { get; init; }
}

public record FileDownloadResult
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public required long ContentLength { get; init; }
}
