using System.Security.Cryptography;
using BMMDL.Runtime.Models;
using BMMDL.Runtime.Services;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Authentication controller for login, registration, and token management.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPlatformUserService _userService;
    private readonly IPlatformTenantService? _tenantService;
    private readonly IOAuthValidator _oAuthValidator;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AuthController> _logger;

    private const int MaxLoginAttempts = 5;
    private static readonly TimeSpan LoginLockoutWindow = TimeSpan.FromMinutes(15);

    public AuthController(
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        IPlatformUserService userService,
        IOAuthValidator oAuthValidator,
        IMemoryCache memoryCache,
        ILogger<AuthController> logger,
        IPlatformTenantService? tenantService = null)
    {
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _userService = userService;
        _oAuthValidator = oAuthValidator;
        _memoryCache = memoryCache;
        _logger = logger;
        _tenantService = tenantService;
    }

    /// <summary>
    /// Register a new user account.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Check if email already exists
        if (await _userService.UsernameOrEmailExistsAsync("", request.Email, ct))
        {
            _logger.LogWarning("Registration failed: email already exists - {Email}", request.Email);
            return Conflict(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Conflict, "Email already exists"));
        }

        var userId = Guid.NewGuid();
        var passwordHash = _passwordHasher.Hash(request.Password);

        // Create identity in database
        var userData = new Dictionary<string, object?>
        {
            ["Id"] = userId,
            ["Email"] = request.Email,
            ["PasswordHash"] = passwordHash,
            ["DisplayName"] = request.DisplayName ?? (request.Email.Split('@') is { Length: > 0 } parts ? parts[0] : request.Email)
        };

        var createdUser = await _userService.CreateUserAsync(userData, ct);
        if (createdUser == null)
        {
            _logger.LogError("Failed to create identity in database: {Email}", request.Email);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InternalError, "Failed to create user"));
        }

        _logger.LogInformation("Identity registered successfully: {Email} ({UserId})", request.Email, userId);

        // Create user context for token generation
        // New registrations have no tenant assigned yet — use empty AllowedTenants
        // to indicate no tenant access until explicitly granted
        var userContext = new UserContext(
            UserId: userId,
            Username: request.Email, // Use email as username
            Email: request.Email,
            TenantId: Guid.Empty,
            Roles: new[] { "User" },
            Permissions: Array.Empty<string>(),
            AllowedTenants: Array.Empty<Guid>()
        );

        var accessToken = _jwtService.GenerateAccessToken(userContext);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Store refresh token in database for validation
        var tokenHash = HashToken(refreshToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var expiresAt = DateTime.UtcNow.AddDays(7); // Refresh token expires in 7 days
        await _userService.StoreRefreshTokenAsync(userId, tokenHash, deviceInfo, ipAddress, expiresAt, ct);

        return Created(string.Empty, new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600,
            User = new UserInfo
            {
                Id = userId,
                Username = request.Email,
                Email = request.Email,
                TenantId = Guid.Empty
            }
        });
    }

    /// <summary>
    /// Login with username/email and password.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Rate limiting: track failed login attempts per IP address
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitKey = $"login_attempts:{ipAddress}";
        var failedAttempts = _memoryCache.GetOrCreate(rateLimitKey, entry =>
        {
            entry.SlidingExpiration = LoginLockoutWindow;
            return 0;
        });

        if (failedAttempts >= MaxLoginAttempts)
        {
            _logger.LogWarning("Login rate limit exceeded for IP: {IpAddress} ({Attempts} attempts)", ipAddress, failedAttempts);
            return StatusCode(429, ODataErrorResponse.FromException("TOO_MANY_REQUESTS", "Too many failed login attempts. Please try again later."));
        }

        _logger.LogInformation("Login attempt for: {UsernameOrEmail}", request.UsernameOrEmail);

        // Query user from database
        var user = await _userService.GetUserByUsernameOrEmailAsync(request.UsernameOrEmail, ct);
        if (user == null)
        {
            _memoryCache.Set(rateLimitKey, failedAttempts + 1, new MemoryCacheEntryOptions { SlidingExpiration = LoginLockoutWindow });
            _logger.LogWarning("Login failed: user not found - {UsernameOrEmail}", request.UsernameOrEmail);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid username or password"));
        }

        // Verify password
        var passwordHash = user.GetValueOrDefault("PasswordHash")?.ToString();
        if (string.IsNullOrEmpty(passwordHash) || !_passwordHasher.Verify(request.Password, passwordHash))
        {
            _memoryCache.Set(rateLimitKey, failedAttempts + 1, new MemoryCacheEntryOptions { SlidingExpiration = LoginLockoutWindow });
            _logger.LogWarning("Login failed: invalid password for user {UsernameOrEmail}", request.UsernameOrEmail);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid username or password"));
        }

        // Check if account is locked (before clearing rate limit to prevent oracle attack)
        var lockedUntil = user.GetValueOrDefault("LockedUntil") as DateTime?;
        if (lockedUntil.HasValue && lockedUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Login failed: account locked for user {UsernameOrEmail}", request.UsernameOrEmail);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccountLocked, "Account is temporarily locked"));
        }

        // Clear rate limit only after ALL auth checks pass
        _memoryCache.Remove(rateLimitKey);

        if (!Guid.TryParse(user["Id"]?.ToString(), out var userId))
        {
            _logger.LogError("Login failed: invalid user ID format in database for {UsernameOrEmail}", request.UsernameOrEmail);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid user account data"));
        }
        var username = user.GetValueOrDefault("Username")?.ToString() ?? request.UsernameOrEmail;
        var email = user.GetValueOrDefault("Email")?.ToString() ?? "";
        // Identity is @SystemScoped — TenantId is not stored on the identity record.
        // Resolve tenant membership from core.user table via IPlatformTenantService.
        var allowedTenants = Array.Empty<Guid>();
        var tenantId = Guid.Empty;
        if (_tenantService != null)
        {
            var userTenants = await _tenantService.GetUserTenantsAsync(userId, ct);
            allowedTenants = userTenants
                .Select(t => t.GetValueOrDefault("id") as Guid? ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
            tenantId = allowedTenants.FirstOrDefault();
        }

        // Load user roles
        var roles = await _userService.GetUserRolesAsync(userId, ct);
        if (roles.Count == 0)
            roles.Add("User"); // Default role

        // Update last login info
        await _userService.UpdateLastLoginAsync(userId, ipAddress, ct);

        _logger.LogInformation("User logged in successfully: {Username} ({UserId}, tenant={TenantId}, tenants={TenantCount})",
            username, userId, tenantId, allowedTenants.Length);

        // Load permissions from database
        var permissions = await _userService.GetUserPermissionsAsync(userId, ct);

        var userContext = new UserContext(
            UserId: userId,
            Username: username,
            Email: email,
            TenantId: tenantId,
            Roles: roles,
            Permissions: permissions,
            AllowedTenants: allowedTenants
        );

        var accessToken = _jwtService.GenerateAccessToken(userContext);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Store refresh token in database for validation
        var tokenHash = HashToken(refreshToken);
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var expiresAt = DateTime.UtcNow.AddDays(7); // Refresh token expires in 7 days
        await _userService.StoreRefreshTokenAsync(userId, tokenHash, deviceInfo, ipAddress, expiresAt, ct);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600,
            User = new UserInfo
            {
                Id = userId,
                Username = username,
                Email = email,
                TenantId = tenantId,
                Roles = roles
            }
        });
    }

    /// <summary>
    /// Get current authenticated user info.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var userContext = _jwtService.GetUserContext(User);
        if (userContext == null)
            return Unauthorized();

        // Optionally refresh user data from database
        var user = await _userService.GetUserByIdAsync(userContext.UserId, ct);
        if (user == null)
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.NotFound, "User not found"));

        return Ok(new UserInfo
        {
            Id = userContext.UserId,
            Username = user.GetValueOrDefault("Username")?.ToString() ?? userContext.Username,
            Email = user.GetValueOrDefault("Email")?.ToString() ?? userContext.Email,
            TenantId = user.GetValueOrDefault("TenantId") as Guid? ?? userContext.TenantId,
            Roles = userContext.Roles.ToList()
        });
    }

    /// <summary>
    /// Refresh access token using refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var tokenHash = HashToken(request.RefreshToken);

        // Validate refresh token exists and is not expired/revoked
        var userId = await _userService.GetUserIdByRefreshTokenAsync(tokenHash, ct);
        if (userId == null)
        {
            _logger.LogWarning("Refresh token validation failed: token not found or expired");
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid or expired refresh token"));
        }

        // Get user data (from Identity table)
        // Note: Identity uses camelCase field names: email, displayName
        var identity = await _userService.GetUserByIdAsync(userId.Value, ct);
        if (identity == null)
        {
            _logger.LogWarning("Refresh failed: user {UserId} not found", userId);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "User not found"));
        }

        // Map Identity fields (camelCase -> our variables)
        var email = identity.GetValueOrDefault("email")?.ToString() ?? "";
        var displayName = identity.GetValueOrDefault("display_name")?.ToString() ?? email;
        var username = email; // Use email as username
        
        // Resolve tenant membership from core.user table
        var allowedTenants = Array.Empty<Guid>();
        var tenantId = Guid.Empty;
        if (_tenantService != null)
        {
            var userTenants = await _tenantService.GetUserTenantsAsync(userId.Value, ct);
            allowedTenants = userTenants
                .Select(t => t.GetValueOrDefault("id") as Guid? ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
            tenantId = allowedTenants.FirstOrDefault();
        }

        // Load roles and permissions
        var roles = await _userService.GetUserRolesAsync(userId.Value, ct);
        if (roles.Count == 0)
            roles.Add("User");
        var permissions = await _userService.GetUserPermissionsAsync(userId.Value, ct);

        var userContext = new UserContext(
            UserId: userId.Value,
            Username: username,
            Email: email,
            TenantId: tenantId,
            Roles: roles,
            Permissions: permissions,
            AllowedTenants: allowedTenants
        );

        // Generate new access token
        var accessToken = _jwtService.GenerateAccessToken(userContext);
        
        // Rotate refresh token (revoke old, issue new)
        await _userService.RevokeRefreshTokenAsync(tokenHash, "Token rotation during refresh", ct);
        
        var newRefreshToken = _jwtService.GenerateRefreshToken();
        var newTokenHash = HashToken(newRefreshToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        await _userService.StoreRefreshTokenAsync(userId.Value, newTokenHash, deviceInfo, ipAddress, expiresAt, ct);

        _logger.LogInformation("Token refreshed for user {Username} ({UserId})", username, userId);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = 3600,
            User = new UserInfo
            {
                Id = userId.Value,
                Username = username,
                Email = email,
                TenantId = tenantId,
                Roles = roles
            }
        });
    }

    /// <summary>
    /// Hash a token using SHA256 for secure database storage.
    /// </summary>
    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Login or register using external OAuth provider (Google, Microsoft, Apple).
    /// If user doesn't exist, creates new Identity automatically.
    /// </summary>
    [HttpPost("external-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var provider = request.Provider.ToLowerInvariant();
        if (provider != "google" && provider != "microsoft" && provider != "apple")
        {
            return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidProvider, "Invalid provider. Supported: google, microsoft, apple"));
        }

        _logger.LogInformation("External login attempt via {Provider}", provider);

        // Validate the OAuth token with provider
        var externalUser = await _oAuthValidator.ValidateTokenAsync(provider, request.IdToken, ct);
        if (externalUser == null)
        {
            _logger.LogWarning("External login failed: invalid token for {Provider}", provider);
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid or expired OAuth token"));
        }

        // Look up existing user by provider ID
        var providerIdField = provider switch
        {
            "google" => "GoogleId",
            "microsoft" => "MicrosoftId",
            "apple" => "AppleId",
            _ => throw new InvalidOperationException("Invalid provider")
        };

        var existingUser = await _userService.GetUserByExternalIdAsync(providerIdField, externalUser.ProviderId, ct);
        bool isNewUser = false;
        Guid userId;
        string email;
        string displayName;

        if (existingUser != null)
        {
            // Existing user - just login
            if (!Guid.TryParse(existingUser["Id"]?.ToString(), out userId))
            {
                _logger.LogError("External login failed: invalid user ID format in database for {Provider} user", provider);
                return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid user account data"));
            }
            email = existingUser.GetValueOrDefault("Email")?.ToString() ?? externalUser.Email;
            displayName = existingUser.GetValueOrDefault("DisplayName")?.ToString() ?? externalUser.Name;

            _logger.LogInformation("External login: existing user {Email} via {Provider}", email, provider);
        }
        else
        {
            // Check if email already exists (might have registered with password)
            var userByEmail = await _userService.GetUserByUsernameOrEmailAsync(externalUser.Email, ct);
            if (userByEmail != null)
            {
                // Email exists but not linked to this provider
                // Return special status suggesting to link account
                return Conflict(new 
                { 
                    error = "Email already registered",
                    code = "EMAIL_EXISTS_DIFFERENT_PROVIDER",
                    message = "This email is already registered. Please login with your password and link this provider."
                });
            }

            // New user - create Identity
            userId = Guid.NewGuid();
            email = externalUser.Email;
            var emailParts = email.Split('@');
            displayName = externalUser.Name ?? (emailParts.Length > 0 ? emailParts[0] : email);
            isNewUser = true;

            var userData = new Dictionary<string, object?>
            {
                ["Id"] = userId,
                ["Email"] = email,
                ["DisplayName"] = displayName,
                [providerIdField] = externalUser.ProviderId,
                ["IsEmailVerified"] = true  // OAuth emails are pre-verified
            };

            var createdUser = await _userService.CreateUserAsync(userData, ct);
            if (createdUser == null)
            {
                _logger.LogError("Failed to create identity for external user: {Email}", email);
                return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InternalError, "Failed to create user"));
            }

            _logger.LogInformation("External login: new user created {Email} via {Provider}", email, provider);
        }

        // Generate tokens
        var roles = await _userService.GetUserRolesAsync(userId, ct);
        if (roles.Count == 0) roles.Add("User");
        var permissions = await _userService.GetUserPermissionsAsync(userId, ct);

        var userContext = new UserContext(
            UserId: userId,
            Username: email,
            Email: email,
            TenantId: Guid.Empty,
            Roles: roles,
            Permissions: permissions,
            AllowedTenants: Array.Empty<Guid>()
        );

        var accessToken = _jwtService.GenerateAccessToken(userContext);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Store refresh token
        var tokenHash = HashToken(refreshToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var deviceInfo = Request.Headers["User-Agent"].ToString();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        await _userService.StoreRefreshTokenAsync(userId, tokenHash, deviceInfo, ipAddress, expiresAt, ct);

        return Ok(new ExternalLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600,
            User = new UserInfo
            {
                Id = userId,
                Username = email,
                Email = email,
                TenantId = Guid.Empty,
                Roles = roles
            },
            IsNewUser = isNewUser,
            RequiresProfileCompletion = isNewUser  // Prompt new users to complete profile
        });
    }

    /// <summary>
    /// Link external OAuth provider to existing authenticated account.
    /// </summary>
    [HttpPost("link-provider")]
    [Authorize]
    public async Task<IActionResult> LinkProvider([FromBody] ExternalLoginRequest request, CancellationToken ct)
    {
        var userContext = _jwtService.GetUserContext(User);
        if (userContext == null)
            return Unauthorized();

        var provider = request.Provider.ToLowerInvariant();
        if (provider != "google" && provider != "microsoft" && provider != "apple")
        {
            return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InvalidProvider, "Invalid provider. Supported: google, microsoft, apple"));
        }

        // Validate the OAuth token
        var externalUser = await _oAuthValidator.ValidateTokenAsync(provider, request.IdToken, ct);
        if (externalUser == null)
        {
            return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid or expired OAuth token"));
        }

        var providerIdField = provider switch
        {
            "google" => "GoogleId",
            "microsoft" => "MicrosoftId",
            "apple" => "AppleId",
            _ => throw new InvalidOperationException("Invalid provider")
        };

        // Check if this external ID is already linked to another account
        var existingUser = await _userService.GetUserByExternalIdAsync(providerIdField, externalUser.ProviderId, ct);
        if (existingUser != null)
        {
            if (!Guid.TryParse(existingUser["Id"]?.ToString(), out var existingUserId))
            {
                _logger.LogError("Link provider failed: invalid user ID format in database for {Provider}", provider);
                return Unauthorized(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Unauthorized, "Invalid user account data"));
            }
            if (existingUserId != userContext.UserId)
            {
                return Conflict(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.Conflict, "This provider account is already linked to another user"));
            }
            // Already linked to current user - no-op
            return Ok(new { message = "Provider already linked" });
        }

        // Link provider to current user
        var success = await _userService.LinkExternalProviderAsync(userContext.UserId, providerIdField, externalUser.ProviderId, ct);
        if (!success)
        {
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.InternalError, "Failed to link provider"));
        }

        _logger.LogInformation("User {UserId} linked {Provider} account", userContext.UserId, provider);

        return Ok(new { message = $"{provider} account linked successfully" });
    }
}

#region Request/Response Models

public class RegisterRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? DisplayName { get; set; }
}

public class LoginRequest
{
    public required string UsernameOrEmail { get; set; }
    public required string Password { get; set; }
}

public class RefreshTokenRequest
{
    public required string RefreshToken { get; set; }
}

public class AuthResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public required UserInfo User { get; set; }
}

public class UserInfo
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public Guid TenantId { get; set; }
    public List<string>? Roles { get; set; }
}

public class ExternalLoginRequest
{
    public required string Provider { get; set; }  // "google", "microsoft", "apple"
    public required string IdToken { get; set; }    // OAuth ID token from provider
}

public class ExternalLoginResponse : AuthResponse
{
    public bool IsNewUser { get; set; }
    public bool RequiresProfileCompletion { get; set; }
}

#endregion

