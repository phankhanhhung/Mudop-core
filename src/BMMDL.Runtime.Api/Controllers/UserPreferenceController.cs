using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Controllers;

// ── Request / Response models ──

public record CreatePreferenceRequest(
    string Category,
    string EntityKey,
    string Name,
    bool IsDefault,
    object Settings
);

public record UpdatePreferenceRequest(
    string? Name,
    bool? IsDefault,
    object? Settings
);

public record PreferenceResponse(
    Guid Id,
    string Category,
    string EntityKey,
    string Name,
    bool IsDefault,
    object Settings,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// REST endpoints for server-backed user preferences (saved views, etc.).
/// </summary>
[ApiController]
[Authorize]
[Route("api/user-preferences")]
[RequiresPlugin("UserPreferences")]
[Tags("UserPreferences")]
public class UserPreferenceController : ControllerBase
{
    private readonly IUserPreferenceService _service;
    private readonly IJwtService _jwtService;
    private readonly ILogger<UserPreferenceController> _logger;

    public UserPreferenceController(IUserPreferenceService service, IJwtService jwtService, ILogger<UserPreferenceController> logger)
    {
        _service = service;
        _jwtService = jwtService;
        _logger = logger;
    }

    /// <summary>
    /// List preferences for the current user filtered by category + entityKey.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string category,
        [FromQuery] string entityKey,
        CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        var prefs = await _service.GetPreferencesAsync(userId.Value, tenantId.Value, category, entityKey, ct);
        return Ok(prefs.Select(ToResponse));
    }

    [HttpGet("by-category")]
    public async Task<IActionResult> ListByCategory(
        [FromQuery] string category,
        CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        var prefs = await _service.GetPreferencesByCategoryAsync(userId.Value, tenantId.Value, category, ct);
        return Ok(prefs.Select(ToResponse));
    }

    /// <summary>
    /// Create a new preference.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePreferenceRequest request, CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        var settingsJson = request.Settings is string s
            ? s
            : System.Text.Json.JsonSerializer.Serialize(request.Settings);

        var pref = new UserPreference
        {
            UserId = userId.Value,
            TenantId = tenantId.Value,
            Category = request.Category,
            EntityKey = request.EntityKey,
            Name = request.Name,
            IsDefault = request.IsDefault,
            Settings = settingsJson,
        };

        // If this is marked as default, unmark others first
        if (request.IsDefault)
        {
            await _service.SetDefaultAsync(userId.Value, tenantId.Value, request.Category, request.EntityKey, Guid.Empty, ct);
        }

        var created = await _service.SavePreferenceAsync(pref, ct);

        // If default, mark it now that we have the ID
        if (request.IsDefault)
        {
            await _service.SetDefaultAsync(userId.Value, tenantId.Value, request.Category, request.EntityKey, created.Id, ct);
            created.IsDefault = true;
        }

        return Created($"/api/user-preferences/{created.Id}", ToResponse(created));
    }

    /// <summary>
    /// Update an existing preference.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePreferenceRequest request, CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        // Verify ownership (user + tenant)
        var existing = await _service.GetPreferenceByIdAsync(id, ct);
        if (existing == null) return NotFound();
        if (existing.UserId != userId.Value) return Forbid();
        if (existing.TenantId != tenantId.Value) return Forbid();

        string? settingsJson = null;
        if (request.Settings != null)
        {
            settingsJson = request.Settings is string s
                ? s
                : System.Text.Json.JsonSerializer.Serialize(request.Settings);
        }

        var updated = await _service.UpdatePreferenceAsync(id, request.Name, request.IsDefault, settingsJson, ct);
        if (updated == null) return NotFound();
        return Ok(ToResponse(updated));
    }

    /// <summary>
    /// Delete a preference (only own preferences).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        // Verify ownership (user + tenant) before deleting
        var existing = await _service.GetPreferenceByIdAsync(id, ct);
        if (existing == null) return NotFound();
        if (existing.UserId != userId.Value) return Forbid();
        if (existing.TenantId != tenantId.Value) return Forbid();

        var deleted = await _service.DeletePreferenceAsync(id, userId.Value, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Set a preference as the default for its category+entityKey (unmarks others).
    /// </summary>
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        var (userId, tenantId) = GetUserAndTenant();
        if (userId == null || tenantId == null)
            return Unauthorized(ODataErrorResponse.FromException("UNAUTHORIZED", "Authentication required"));

        var existing = await _service.GetPreferenceByIdAsync(id, ct);
        if (existing == null) return NotFound();
        if (existing.UserId != userId.Value) return Forbid();
        if (existing.TenantId != tenantId.Value) return Forbid();

        await _service.SetDefaultAsync(
            userId.Value, tenantId.Value, existing.Category, existing.EntityKey, id, ct);

        return NoContent();
    }

    // ── Helpers ──

    private (Guid? UserId, Guid? TenantId) GetUserAndTenant()
    {
        var userContext = _jwtService.GetUserContext(User);
        if (userContext == null) return (null, null);

        // Resolve effective tenant from X-Tenant-Id header (TenantContextMiddleware) or JWT
        var effectiveTenantId = HttpContext.Items.TryGetValue("TenantId", out var tid) && tid is Guid g
            ? g
            : userContext.TenantId;
        return (userContext.UserId, effectiveTenantId);
    }

    private static PreferenceResponse ToResponse(UserPreference p)
    {
        object settings;
        try
        {
            settings = System.Text.Json.JsonSerializer.Deserialize<object>(p.Settings)
                       ?? p.Settings;
        }
        catch
        {
            settings = p.Settings;
        }

        return new PreferenceResponse(
            p.Id, p.Category, p.EntityKey, p.Name, p.IsDefault,
            settings, p.CreatedAt, p.UpdatedAt);
    }
}
