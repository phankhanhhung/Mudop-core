namespace BMMDL.Runtime.Storage;

using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

public class S3FileStorageProvider : IFileStorageProvider
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3FileStorageProvider> _logger;

    public string ProviderName => "S3";

    public S3FileStorageProvider(IAmazonS3 s3Client, ILogger<S3FileStorageProvider> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
    }

    public async Task<FileUploadResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default)
    {
        var tenantPrefix = request.TenantId?.ToString() ?? "global";
        var datePath = DateTime.UtcNow.ToString("yyyy/MM");
        var uniqueName = $"{Guid.NewGuid()}_{SanitizeKey(request.FileName)}";
        var key = $"{tenantPrefix}/{datePath}/{uniqueName}";

        // Buffer the stream to compute checksum and get size
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var checksum = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        var size = buffer.Length;
        buffer.Position = 0;

        var putRequest = new PutObjectRequest
        {
            BucketName = request.Bucket,
            Key = key,
            InputStream = buffer,
            ContentType = request.ContentType,
        };

        await _s3Client.PutObjectAsync(putRequest, ct);

        _logger.LogInformation("File uploaded to S3: {Bucket}/{Key} ({Size} bytes)", request.Bucket, key, size);

        return new FileUploadResult
        {
            Provider = ProviderName,
            Bucket = request.Bucket,
            Key = key,
            Size = size,
            MimeType = request.ContentType,
            Checksum = checksum,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = request.UserId
        };
    }

    public async Task<FileDownloadResult> DownloadAsync(string bucket, string key, CancellationToken ct = default)
    {
        var response = await _s3Client.GetObjectAsync(bucket, key, ct);

        var fileName = Path.GetFileName(key);
        var underscoreIdx = fileName.IndexOf('_');
        if (underscoreIdx > 30)
            fileName = fileName[(underscoreIdx + 1)..];

        return new FileDownloadResult
        {
            Content = response.ResponseStream,
            ContentType = response.Headers.ContentType ?? "application/octet-stream",
            FileName = fileName,
            ContentLength = response.ContentLength
        };
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        await _s3Client.DeleteObjectAsync(bucket, key, ct);
        _logger.LogInformation("File deleted from S3: {Bucket}/{Key}", bucket, key);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string SanitizeKey(string fileName)
    {
        // S3 keys are more permissive, but strip control chars
        return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }
}
