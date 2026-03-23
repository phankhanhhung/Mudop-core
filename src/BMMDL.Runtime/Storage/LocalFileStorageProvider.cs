namespace BMMDL.Runtime.Storage;

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    public string ProviderName => "Local";

    public LocalFileStorageProvider(string basePath, ILogger<LocalFileStorageProvider> logger)
    {
        _basePath = basePath;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<FileUploadResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default)
    {
        var bucketPath = Path.Combine(_basePath, request.Bucket);
        Directory.CreateDirectory(bucketPath);

        // Generate unique key: tenantId/yyyy/MM/uuid_filename
        var tenantPrefix = request.TenantId?.ToString() ?? "global";
        var datePath = DateTime.UtcNow.ToString("yyyy/MM");
        var uniqueName = $"{Guid.NewGuid()}_{SanitizeFileName(request.FileName)}";
        var key = $"{tenantPrefix}/{datePath}/{uniqueName}";

        var fullPath = Path.Combine(bucketPath, key.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        // Write file and compute checksum
        string checksum;
        long size;
        using (var sha256 = SHA256.Create())
        {
            await using var fileStream = File.Create(fullPath);
            await using var cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write);
            await request.Content.CopyToAsync(cryptoStream, ct);
            await cryptoStream.FlushFinalBlockAsync(ct);
            checksum = Convert.ToHexStringLower(sha256.Hash!);
            size = fileStream.Length;
        }

        _logger.LogInformation("File uploaded: {Bucket}/{Key} ({Size} bytes)", request.Bucket, key, size);

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

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains("..") || key.Contains("\\"))
            throw new ArgumentException("Invalid storage key format", nameof(key));
    }

    public Task<FileDownloadResult> DownloadAsync(string bucket, string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var fullPath = Path.Combine(_basePath, bucket, key.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {bucket}/{key}");

        var fileInfo = new FileInfo(fullPath);
        var fileName = Path.GetFileName(key);
        // Remove UUID prefix from filename if present
        var underscoreIdx = fileName.IndexOf('_');
        if (underscoreIdx > 30) // UUID is 36 chars with hyphens
            fileName = fileName[(underscoreIdx + 1)..];

        var contentType = GetContentType(fileName);
        var stream = File.OpenRead(fullPath);

        return Task.FromResult(new FileDownloadResult
        {
            Content = stream,
            ContentType = contentType,
            FileName = fileName,
            ContentLength = fileInfo.Length
        });
    }

    public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var fullPath = Path.Combine(_basePath, bucket, key.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("File deleted: {Bucket}/{Key}", bucket, key);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var fullPath = Path.Combine(_basePath, bucket, key.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(File.Exists(fullPath));
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
