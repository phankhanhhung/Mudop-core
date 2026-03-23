namespace BMMDL.MetaModel.Types;

/// <summary>
/// Represents a file reference type for external file storage.
/// FileReference fields expand to multiple metadata columns in the database
/// while actual binary data is stored in external storage (S3, MinIO, etc.).
///
/// Storage annotations configure the provider and constraints:
/// @Storage.Provider: 'S3' | 'GCS' | 'MinIO' | 'AzureBlob' | 'Local'
/// @Storage.Bucket: 'bucket-name'
/// @Storage.MaxSize: 52428800  // 50MB in bytes
/// @Storage.AllowedTypes: ['application/pdf', 'image/jpeg']
///
/// Example:
/// entity Document {
///     key ID: UUID;
///     @Storage.Provider: 'S3'
///     @Storage.Bucket: 'documents'
///     @Storage.MaxSize: 52428800
///     content: FileReference;
/// }
///
/// This expands to columns:
/// - content_provider VARCHAR(50)
/// - content_bucket VARCHAR(255)
/// - content_key VARCHAR(1024)
/// - content_size BIGINT
/// - content_mime_type VARCHAR(100)
/// - content_checksum VARCHAR(64)
/// - content_uploaded_at TIMESTAMP
/// - content_uploaded_by UUID
/// </summary>
public class BmFileReferenceType : BmTypeReference
{
    /// <summary>
    /// Storage provider configured via @Storage.Provider annotation.
    /// Valid values: S3, GCS, MinIO, AzureBlob, Local
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Bucket or container name configured via @Storage.Bucket annotation.
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// Maximum file size in bytes configured via @Storage.MaxSize annotation.
    /// Example: 52428800 for 50MB
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// Allowed MIME types configured via @Storage.AllowedTypes annotation.
    /// Example: ['application/pdf', 'image/jpeg', 'image/png']
    /// </summary>
    public List<string>? AllowedMimeTypes { get; set; }

    /// <summary>
    /// Get the canonical string representation.
    /// </summary>
    public override string ToTypeString()
    {
        return IsNullable ? "FileReference?" : "FileReference";
    }

    /// <summary>
    /// Get the list of metadata column names that this FileReference expands to.
    /// </summary>
    /// <param name="fieldName">The field name in the entity</param>
    /// <returns>List of column names</returns>
    public static List<string> GetMetadataColumnNames(string fieldName)
    {
        return new List<string>
        {
            $"{fieldName}_provider",
            $"{fieldName}_bucket",
            $"{fieldName}_key",
            $"{fieldName}_size",
            $"{fieldName}_mime_type",
            $"{fieldName}_checksum",
            $"{fieldName}_uploaded_at",
            $"{fieldName}_uploaded_by"
        };
    }

    /// <summary>
    /// Get metadata column definitions for PostgreSQL DDL generation.
    /// </summary>
    /// <param name="fieldName">The field name in the entity</param>
    /// <param name="isNullable">Whether the field is nullable</param>
    /// <returns>Dictionary of column name to PostgreSQL type</returns>
    public static Dictionary<string, string> GetMetadataColumnDefinitions(
        string fieldName,
        bool isNullable)
    {
        var nullClause = isNullable ? "" : " NOT NULL";

        return new Dictionary<string, string>
        {
            [$"{fieldName}_provider"] = $"VARCHAR(50){nullClause}",
            [$"{fieldName}_bucket"] = $"VARCHAR(255){nullClause}",
            [$"{fieldName}_key"] = $"VARCHAR(1024){nullClause}",
            [$"{fieldName}_size"] = $"BIGINT{nullClause}",
            [$"{fieldName}_mime_type"] = $"VARCHAR(100){nullClause}",
            [$"{fieldName}_checksum"] = $"VARCHAR(64){nullClause}",
            [$"{fieldName}_uploaded_at"] = $"TIMESTAMP{nullClause}",
            [$"{fieldName}_uploaded_by"] = $"UUID{nullClause}"
        };
    }

    /// <summary>
    /// Validate storage provider value.
    /// </summary>
    public static bool IsValidProvider(string? provider)
    {
        return provider switch
        {
            "S3" or "GCS" or "MinIO" or "AzureBlob" or "Local" => true,
            _ => false
        };
    }

    /// <summary>
    /// Validate MIME type format.
    /// </summary>
    public static bool IsValidMimeType(string mimeType)
    {
        // Basic validation: type/subtype format
        if (string.IsNullOrWhiteSpace(mimeType)) return false;

        var parts = mimeType.Split('/');
        return parts.Length == 2
            && !string.IsNullOrWhiteSpace(parts[0])
            && !string.IsNullOrWhiteSpace(parts[1]);
    }
}

/// <summary>
/// DTO for file reference metadata used in API responses.
/// </summary>
public class FileReferenceMetadata
{
    public string Provider { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Key { get; set; } = "";
    public long Size { get; set; }
    public string MimeType { get; set; } = "";
    public string Checksum { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public Guid UploadedBy { get; set; }

    /// <summary>
    /// Full storage URL (for display purposes).
    /// Example: s3://bucket-name/tenant-id/entity/field/file.pdf
    /// </summary>
    public string StorageUrl => $"{Provider.ToLower()}://{Bucket}/{Key}";
}
