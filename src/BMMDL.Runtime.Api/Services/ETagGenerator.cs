namespace BMMDL.Runtime.Api.Services;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// ETag generator for OData v4 optimistic concurrency control.
/// Generates weak ETags based on entity state (id + updated_at).
/// </summary>
public static class ETagGenerator
{
    /// <summary>
    /// Generate ETag from entity data.
    /// Uses SHA256 hash of (id + updated_at) for efficient uniqueness.
    /// </summary>
    /// <param name="entity">Entity dictionary with id and updated_at fields.</param>
    /// <returns>Base64-encoded hash suitable for ETag header.</returns>
    public static string Generate(Dictionary<string, object?> entity)
    {
        // Get id (try multiple casing)
        var id = entity.GetValueOrDefault("id")?.ToString() 
              ?? entity.GetValueOrDefault("Id")?.ToString() 
              ?? "";
        
        // Get updated_at (try multiple casing/formats)
        var updatedAt = entity.GetValueOrDefault("updated_at")?.ToString() 
                     ?? entity.GetValueOrDefault("UpdatedAt")?.ToString()
                     ?? entity.GetValueOrDefault("updatedAt")?.ToString()
                     ?? "";
        
        // Create hash input
        var input = $"{id}:{updatedAt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        
        // Use first 12 bytes (16 chars base64) for shorter ETag
        return Convert.ToBase64String(hash, 0, 12);
    }

    /// <summary>
    /// Generate weak ETag header value (W/"...").
    /// </summary>
    public static string GenerateWeakETag(Dictionary<string, object?> entity)
    {
        var hash = Generate(entity);
        return $"W/\"{hash}\"";
    }

    /// <summary>
    /// Generate strong ETag header value ("...") without the W/ prefix.
    /// Use when clients require byte-for-byte identical responses.
    /// </summary>
    public static string GenerateStrongETag(Dictionary<string, object?> entity)
    {
        var hash = Generate(entity);
        return $"\"{hash}\"";
    }

    /// <summary>
    /// Generate ETag header value with configurable strength.
    /// </summary>
    /// <param name="entity">Entity dictionary with id and updated_at fields.</param>
    /// <param name="useStrongETag">When true, generates a strong ETag (no W/ prefix). Default is false (weak).</param>
    /// <returns>Formatted ETag string.</returns>
    public static string GenerateETag(Dictionary<string, object?> entity, bool useStrongETag = false)
    {
        return useStrongETag ? GenerateStrongETag(entity) : GenerateWeakETag(entity);
    }

    /// <summary>
    /// Check if client-provided ETag matches current ETag.
    /// Handles weak ETags (W/"...") and strong ETags ("...").
    /// </summary>
    /// <param name="clientETag">ETag from If-Match header.</param>
    /// <param name="serverETag">Current entity ETag (raw hash, not quoted).</param>
    /// <returns>True if ETags match or client sent wildcard.</returns>
    public static bool Matches(string? clientETag, string serverETag)
    {
        if (string.IsNullOrEmpty(clientETag))
            return false;
        
        // Strip W/ prefix and quotes
        var normalized = clientETag.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        normalized = normalized.Trim('"');
        
        // Handle wildcard - always matches
        if (normalized == "*")
            return true;
            
        return string.Equals(normalized, serverETag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parse and normalize an ETag header value.
    /// Removes W/ prefix and surrounding quotes.
    /// </summary>
    public static string Normalize(string etag)
    {
        var normalized = etag.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        return normalized.Trim('"');
    }
}
