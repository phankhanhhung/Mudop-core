namespace BMMDL.Runtime.Api.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Configuration options for delta token behavior.
/// </summary>
public class DeltaTokenOptions
{
    public const string SectionName = "DeltaToken";

    /// <summary>
    /// Token expiry duration in hours. Defaults to 24 hours.
    /// </summary>
    public double ExpiryHours { get; set; } = 24;
}

/// <summary>
/// Service for OData v4 Delta Token management.
/// Delta tokens encode the timestamp of the last successful query.
/// </summary>
public class DeltaTokenService : IDeltaTokenService
{
    private readonly ILogger<DeltaTokenService> _logger;
    private readonly TimeSpan _tokenExpiry;
    private readonly byte[] _tokenKey;

    public DeltaTokenService(ILogger<DeltaTokenService> logger, IConfiguration configuration, IOptions<DeltaTokenOptions>? options = null)
    {
        _logger = logger;
        var expiryHours = options?.Value.ExpiryHours ?? 24;
        _tokenExpiry = TimeSpan.FromHours(expiryHours);

        var configuredKey = configuration["DeltaToken:SigningKey"];
        if (!string.IsNullOrEmpty(configuredKey))
        {
            _tokenKey = Encoding.UTF8.GetBytes(configuredKey);
        }
        else
        {
            _tokenKey = RandomNumberGenerator.GetBytes(32);
            _logger.LogWarning("No DeltaToken:SigningKey configured. Using a random key — delta tokens will not survive application restarts.");
        }
    }

    /// <summary>
    /// Generate a delta token encoding the current timestamp and query context.
    /// </summary>
    /// <param name="entityName">Fully qualified entity name.</param>
    /// <param name="tenantId">Optional tenant ID for scoped queries.</param>
    /// <param name="filter">Original filter expression (if any).</param>
    /// <returns>Base64-encoded delta token.</returns>
    public string GenerateToken(string entityName, Guid? tenantId = null, string? filter = null)
    {
        var payload = new DeltaTokenPayload
        {
            EntityName = entityName,
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            Filter = filter
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        // Add HMAC signature for integrity
        using var hmac = new HMACSHA256(_tokenKey);
        var signature = hmac.ComputeHash(bytes);

        // Combine payload + signature
        var combined = new byte[bytes.Length + signature.Length];
        bytes.CopyTo(combined, 0);
        signature.CopyTo(combined, bytes.Length);
        
        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Parse and validate a delta token.
    /// </summary>
    /// <param name="token">Base64-encoded delta token.</param>
    /// <returns>Parsed payload, or null if invalid.</returns>
    public DeltaTokenPayload? ParseToken(string token)
    {
        try
        {
            var combined = Convert.FromBase64String(token);
            
            if (combined.Length <= 32)
            {
                _logger.LogWarning("Delta token too short");
                return null;
            }
            
            var payloadLength = combined.Length - 32;
            var bytes = combined[..payloadLength];
            var providedSignature = combined[payloadLength..];
            
            // Verify signature
            using var hmac = new HMACSHA256(_tokenKey);
            var expectedSignature = hmac.ComputeHash(bytes);
            
            if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            {
                _logger.LogWarning("Delta token signature mismatch");
                return null;
            }
            
            var json = Encoding.UTF8.GetString(bytes);
            var payload = JsonSerializer.Deserialize<DeltaTokenPayload>(json);

            // Check token expiry
            if (payload != null && DateTimeOffset.UtcNow - payload.Timestamp > _tokenExpiry)
            {
                _logger.LogWarning("Delta token expired. Token timestamp: {Timestamp}, Expiry: {Expiry}",
                    payload.Timestamp, _tokenExpiry);
                return null;
            }

            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse delta token");
            return null;
        }
    }

    /// <summary>
    /// Generate a delta link URL for a given entity and filter.
    /// </summary>
    public string GenerateDeltaLink(
        string baseUrl,
        string module,
        string entity,
        Guid? tenantId = null,
        string? filter = null)
    {
        var token = GenerateToken($"{module}.{entity}", tenantId, filter);
        var encodedToken = Uri.EscapeDataString(token);
        return $"{baseUrl}/api/odata/{module}/{entity}?$deltatoken={encodedToken}";
    }
}

/// <summary>
/// Interface for delta token operations.
/// </summary>
public interface IDeltaTokenService
{
    string GenerateToken(string entityName, Guid? tenantId = null, string? filter = null);
    DeltaTokenPayload? ParseToken(string token);
    string GenerateDeltaLink(string baseUrl, string module, string entity, Guid? tenantId = null, string? filter = null);
}

/// <summary>
/// Payload stored in a delta token.
/// </summary>
public class DeltaTokenPayload
{
    public string EntityName { get; set; } = "";
    public Guid? TenantId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Filter { get; set; }
}
