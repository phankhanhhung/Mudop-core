using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BMMDL.Runtime.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BMMDL.Runtime.Services;

/// <summary>
/// Configuration options for JWT tokens.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";
    
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "BMMDL.Runtime";
    public string Audience { get; set; } = "BMMDL.Client";
    public int ExpirationMinutes { get; set; } = 60;
    public int RefreshExpirationDays { get; set; } = 7;
}

/// <summary>
/// Service for generating and validating JWT tokens.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generate an access token for the user.
    /// </summary>
    string GenerateAccessToken(UserContext user);

    /// <summary>
    /// Generate a refresh token.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Validate a token and extract claims.
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>
    /// Extract UserContext from claims principal.
    /// </summary>
    UserContext? GetUserContext(ClaimsPrincipal principal);
}

/// <summary>
/// JWT service implementation.
/// </summary>
public class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _securityKey;

    public JwtService(JwtOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        
        if (string.IsNullOrEmpty(_options.SecretKey) || _options.SecretKey.Length < 32)
            throw new ArgumentException("JWT SecretKey must be at least 32 characters", nameof(options));
            
        _securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
    }

    /// <inheritdoc />
    public string GenerateAccessToken(UserContext user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("username", user.Username),
            // tenant_id removed - use X-Tenant-Id header instead
        };

        // Add roles as claims
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Add permissions as claims
        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        // Add allowed tenant IDs as claims (for X-Tenant-Id header validation)
        foreach (var tenantId in user.AllowedTenants)
        {
            if (tenantId != Guid.Empty)
                claims.Add(new Claim("allowed_tenant", tenantId.ToString()));
        }

        var credentials = new SigningCredentials(_securityKey, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _securityKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParams, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public UserContext? GetUserContext(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        // JWT library may map standard claims to different types:
        // - 'sub' → ClaimTypes.NameIdentifier or JwtRegisteredClaimNames.Sub
        // - 'email' → ClaimTypes.Email or JwtRegisteredClaimNames.Email
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                       ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value 
                      ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var usernameClaim = principal.FindFirst("username")?.Value;

        // Validate required claims
        if (!Guid.TryParse(userIdClaim, out var userId) ||
            string.IsNullOrEmpty(emailClaim) ||
            string.IsNullOrEmpty(usernameClaim))
        {
            return null;
        }

        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var permissions = principal.FindAll("permission")
            .Select(c => c.Value)
            .ToList();

        // Read allowed tenant IDs from token claims
        var allowedTenants = principal.FindAll("allowed_tenant")
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        // Tenant context comes from X-Tenant-Id header, not token
        return new UserContext(
            UserId: userId,
            Username: usernameClaim,
            Email: emailClaim,
            TenantId: allowedTenants.FirstOrDefault(),
            Roles: roles,
            Permissions: permissions,
            AllowedTenants: allowedTenants
        );
    }
}
