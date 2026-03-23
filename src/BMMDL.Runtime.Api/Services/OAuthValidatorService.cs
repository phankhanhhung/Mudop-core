using System.IdentityModel.Tokens.Jwt;
using Google.Apis.Auth;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BMMDL.Runtime.Api.Services;

/// <summary>
/// External user info extracted from OAuth token.
/// </summary>
public class ExternalUserInfo
{
    public required string ProviderId { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Interface for OAuth token validation.
/// </summary>
public interface IOAuthValidator
{
    /// <summary>
    /// Validate an OAuth ID token from a provider.
    /// </summary>
    /// <param name="provider">Provider name: google, microsoft, apple</param>
    /// <param name="idToken">The ID token from the OAuth provider</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>User info if valid, null if invalid</returns>
    Task<ExternalUserInfo?> ValidateTokenAsync(string provider, string idToken, CancellationToken ct);
}

/// <summary>
/// OAuth token validation service.
/// Supports Google (via Google.Apis.Auth), Microsoft/Azure AD, and Apple Sign-In.
/// </summary>
public class OAuthValidatorService : IOAuthValidator
{
    private readonly OAuthOptions _options;
    private readonly ILogger<OAuthValidatorService> _logger;
    private readonly bool _isDevelopment;

    // OIDC configuration managers (lazy-initialized, handles JWKS caching)
    private ConfigurationManager<OpenIdConnectConfiguration>? _microsoftConfigManager;
    private ConfigurationManager<OpenIdConnectConfiguration>? _appleConfigManager;

    public OAuthValidatorService(OAuthOptions options, ILogger<OAuthValidatorService> logger, IHostEnvironment? hostEnvironment = null)
    {
        _options = options;
        _logger = logger;
        _isDevelopment = hostEnvironment?.EnvironmentName == "Development";
    }

    public async Task<ExternalUserInfo?> ValidateTokenAsync(string provider, string idToken, CancellationToken ct)
    {
        // 1. Check for TEST_ tokens — only allowed in Development environment
        if (_options.AllowTestTokens && idToken.StartsWith("TEST_"))
        {
            if (!_isDevelopment)
            {
                _logger.LogWarning("TEST_ token rejected: AllowTestTokens is enabled but environment is not Development");
                return null;
            }

            _logger.LogDebug("Accepting TEST_ token for provider {Provider}", provider);
            return ParseTestToken(idToken);
        }

        // 2. Real validation by provider
        return provider.ToLowerInvariant() switch
        {
            "google" => await ValidateGoogleTokenAsync(idToken, ct),
            "microsoft" => await ValidateMicrosoftTokenAsync(idToken, ct),
            "apple" => await ValidateAppleTokenAsync(idToken, ct),
            _ => null
        };
    }

    /// <summary>
    /// Parse test token format: TEST_{providerId}_{email}_{name}
    /// </summary>
    private static ExternalUserInfo? ParseTestToken(string idToken)
    {
        var parts = idToken.Split('_');
        if (parts.Length >= 3)
        {
            return new ExternalUserInfo
            {
                ProviderId = parts[1],
                Email = parts[2],
                Name = parts.Length > 3 ? parts[3] : null
            };
        }
        return null;
    }

    /// <summary>
    /// Validate Google ID token using Google.Apis.Auth library.
    /// </summary>
    private async Task<ExternalUserInfo?> ValidateGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        if (!_options.Google.Enabled || string.IsNullOrEmpty(_options.Google.ClientId))
        {
            _logger.LogWarning("Google OAuth is not configured. Set OAuth:Google:ClientId and OAuth:Google:Enabled=true");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _options.Google.ClientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            _logger.LogInformation("Google token validated for user: {Email} (sub: {Subject})",
                payload.Email, payload.Subject);

            return new ExternalUserInfo
            {
                ProviderId = payload.Subject,
                Email = payload.Email,
                Name = payload.Name
            };
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning("Google token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating Google token");
            return null;
        }
    }

    /// <summary>
    /// Validate Microsoft/Azure AD ID token via OIDC discovery + JWKS.
    /// Supports single-tenant and multi-tenant (common) configurations.
    /// </summary>
    private async Task<ExternalUserInfo?> ValidateMicrosoftTokenAsync(string idToken, CancellationToken ct)
    {
        if (!_options.Microsoft.Enabled || string.IsNullOrEmpty(_options.Microsoft.ClientId))
        {
            _logger.LogWarning("Microsoft OAuth is not configured. Set OAuth:Microsoft:ClientId and OAuth:Microsoft:Enabled=true");
            return null;
        }

        try
        {
            var tenantId = _options.Microsoft.TenantId;
            var metadataAddress = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";

            _microsoftConfigManager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress, new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var config = await _microsoftConfigManager.GetConfigurationAsync(ct);

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = tenantId != "common",
                ValidIssuer = tenantId != "common"
                    ? $"https://login.microsoftonline.com/{tenantId}/v2.0"
                    : null,
                IssuerValidator = tenantId == "common" ? ValidateMicrosoftIssuer : null,
                ValidateAudience = true,
                ValidAudience = _options.Microsoft.ClientId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(idToken, validationParams, out var validatedToken);

            var sub = principal.FindFirst("sub")?.Value
                   ?? principal.FindFirst("oid")?.Value
                   ?? throw new SecurityTokenException("Token missing sub/oid claim");
            var email = principal.FindFirst("email")?.Value
                     ?? principal.FindFirst("preferred_username")?.Value
                     ?? throw new SecurityTokenException("Token missing email/preferred_username claim");
            var name = principal.FindFirst("name")?.Value;

            _logger.LogInformation("Microsoft token validated for user: {Email} (sub: {Subject})", email, sub);

            return new ExternalUserInfo
            {
                ProviderId = sub,
                Email = email,
                Name = name
            };
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Microsoft token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating Microsoft token");
            return null;
        }
    }

    /// <summary>
    /// Issuer validator for multi-tenant (common) Microsoft tokens.
    /// Accepts any Azure AD v2.0 issuer format.
    /// </summary>
    private static string ValidateMicrosoftIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (issuer.StartsWith("https://login.microsoftonline.com/") && issuer.EndsWith("/v2.0"))
            return issuer;
        throw new SecurityTokenInvalidIssuerException($"Invalid Microsoft issuer: {issuer}");
    }

    /// <summary>
    /// Validate Apple Sign-In ID token via Apple's JWKS endpoint.
    /// </summary>
    private async Task<ExternalUserInfo?> ValidateAppleTokenAsync(string idToken, CancellationToken ct)
    {
        if (!_options.Apple.Enabled || string.IsNullOrEmpty(_options.Apple.ServiceId))
        {
            _logger.LogWarning("Apple OAuth is not configured. Set OAuth:Apple:ServiceId and OAuth:Apple:Enabled=true");
            return null;
        }

        try
        {
            var metadataAddress = "https://appleid.apple.com/.well-known/openid-configuration";

            _appleConfigManager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress, new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var config = await _appleConfigManager.GetConfigurationAsync(ct);

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidIssuer = "https://appleid.apple.com",
                ValidateAudience = true,
                ValidAudience = _options.Apple.ServiceId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(idToken, validationParams, out var validatedToken);

            var sub = principal.FindFirst("sub")?.Value
                   ?? throw new SecurityTokenException("Token missing sub claim");
            var email = principal.FindFirst("email")?.Value
                     ?? throw new SecurityTokenException("Token missing email claim");

            _logger.LogInformation("Apple token validated for user: {Email} (sub: {Subject})", email, sub);

            return new ExternalUserInfo
            {
                ProviderId = sub,
                Email = email,
                Name = null // Apple doesn't include name in ID token after first auth
            };
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Apple token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating Apple token");
            return null;
        }
    }
}
