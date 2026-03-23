using BMMDL.Runtime.Reports;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;

namespace BMMDL.Runtime.Api.Controllers;

// =============================================================================
// DTOs
// =============================================================================

public record ReportTemplateDto(
    Guid Id, string Name, string? Description,
    string Module, string EntityType, string LayoutType,
    List<ReportField> Fields, string? GroupBy, List<SortConfig> SortBy,
    string? Header, string? Footer,
    bool IsPublic, string? ShareToken,
    string? ScheduleCron, List<string> ScheduleRecipients,
    DateTime CreatedAt, DateTime? UpdatedAt
);

public record CreateReportRequest(
    string Name, string? Description,
    string Module, string EntityType, string LayoutType,
    List<ReportField> Fields, string? GroupBy, List<SortConfig> SortBy,
    string? Header, string? Footer,
    bool IsPublic,
    string? ScheduleCron, List<string> ScheduleRecipients
);

public record UpdateReportRequest(
    string Name, string? Description,
    string Module, string EntityType, string LayoutType,
    List<ReportField> Fields, string? GroupBy, List<SortConfig> SortBy,
    string? Header, string? Footer,
    bool IsPublic,
    string? ScheduleCron, List<string> ScheduleRecipients
);

public record ShareTokenResponse(string ShareToken, string ShareUrl);

public record ReportDataResponse(ReportTemplateDto Template, List<Dictionary<string, object?>> Rows, int TotalCount);

// =============================================================================
// Controller
// =============================================================================

/// <summary>
/// Admin REST controller for Report Template management.
/// Provides full lifecycle management for report templates, share tokens, data retrieval, and scheduled delivery.
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Policy = "AdminKeyPolicy")]
[RequiresPlugin("Reporting")]
public class ReportController : ControllerBase
{
    private readonly IReportStore _reportStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        IReportStore reportStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ReportController> logger)
    {
        _reportStore = reportStore ?? throw new ArgumentNullException(nameof(reportStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the configured base URL, falling back to the request's scheme and host
    /// only after validating the Host header against a strict allowlist of characters.
    /// </summary>
    private string GetBaseUrl()
    {
        var configured = _configuration["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.TrimEnd('/');
        }

        var host = Request.Host.ToString();
        if (!System.Text.RegularExpressions.Regex.IsMatch(host, @"^[a-zA-Z0-9.:_-]+$"))
        {
            throw new InvalidOperationException("Invalid Host header detected.");
        }

        return $"{Request.Scheme}://{host}";
    }

    // =========================================================================
    // TEMPLATE CRUD
    // =========================================================================

    /// <summary>
    /// GET api/admin/reports/templates
    /// Returns all report templates.
    /// </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var templates = await _reportStore.GetAllAsync(tenantId);
        var dtos = templates.Select(ToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// GET api/admin/reports/templates/{id}
    /// Returns a single report template by ID.
    /// </summary>
    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var template = await _reportStore.GetByIdAsync(id, tenantId);
        if (template == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        return Ok(ToDto(template));
    }

    /// <summary>
    /// POST api/admin/reports/templates
    /// Creates a new report template.
    /// </summary>
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateReportRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Module))
        {
            return BadRequest(new { error = "Module is required." });
        }

        if (string.IsNullOrWhiteSpace(request.EntityType))
        {
            return BadRequest(new { error = "EntityType is required." });
        }

        var template = new ReportTemplate
        {
            Name = request.Name,
            Description = request.Description,
            Module = request.Module,
            EntityType = request.EntityType,
            LayoutType = string.IsNullOrWhiteSpace(request.LayoutType) ? "list" : request.LayoutType,
            Fields = request.Fields ?? [],
            GroupBy = request.GroupBy,
            SortBy = request.SortBy ?? [],
            Header = request.Header,
            Footer = request.Footer,
            IsPublic = request.IsPublic,
            ScheduleCron = request.ScheduleCron,
            ScheduleRecipients = request.ScheduleRecipients ?? []
        };

        var created = await _reportStore.CreateAsync(template, tenantId);
        _logger.LogInformation("Report template created: {TemplateId} ({Name})", created.Id, created.Name);

        return CreatedAtAction(nameof(GetTemplate), new { id = created.Id }, ToDto(created));
    }

    /// <summary>
    /// PUT api/admin/reports/templates/{id}
    /// Updates an existing report template.
    /// </summary>
    [HttpPut("templates/{id:guid}")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateReportRequest request, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var existing = await _reportStore.GetByIdAsync(id, tenantId);
        if (existing == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        existing.Name = request.Name;
        existing.Description = request.Description;
        existing.Module = request.Module;
        existing.EntityType = request.EntityType;
        existing.LayoutType = string.IsNullOrWhiteSpace(request.LayoutType) ? "list" : request.LayoutType;
        existing.Fields = request.Fields ?? [];
        existing.GroupBy = request.GroupBy;
        existing.SortBy = request.SortBy ?? [];
        existing.Header = request.Header;
        existing.Footer = request.Footer;
        existing.IsPublic = request.IsPublic;
        existing.ScheduleCron = request.ScheduleCron;
        existing.ScheduleRecipients = request.ScheduleRecipients ?? [];

        var updated = await _reportStore.UpdateAsync(existing, tenantId);
        return Ok(ToDto(updated));
    }

    /// <summary>
    /// DELETE api/admin/reports/templates/{id}
    /// Permanently removes a report template.
    /// </summary>
    [HttpDelete("templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var existing = await _reportStore.GetByIdAsync(id, tenantId);
        if (existing == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        await _reportStore.DeleteAsync(id, tenantId);
        return NoContent();
    }

    // =========================================================================
    // SHARE TOKEN MANAGEMENT
    // =========================================================================

    /// <summary>
    /// POST api/admin/reports/templates/{id}/share
    /// Generates or refreshes the public share token for a template.
    /// </summary>
    [HttpPost("templates/{id:guid}/share")]
    public async Task<IActionResult> GenerateShareToken(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var template = await _reportStore.GetByIdAsync(id, tenantId);
        if (template == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        template.IsPublic = true;
        template.ShareToken = Guid.NewGuid().ToString();

        await _reportStore.UpdateAsync(template, tenantId);

        var baseUrl = GetBaseUrl();
        var shareUrl = $"{baseUrl}/reports/{template.ShareToken}";
        _logger.LogInformation("Share token generated for report template {TemplateId}", id);

        return Ok(new ShareTokenResponse(template.ShareToken, shareUrl));
    }

    /// <summary>
    /// DELETE api/admin/reports/templates/{id}/share
    /// Revokes the public share token for a template.
    /// </summary>
    [HttpDelete("templates/{id:guid}/share")]
    public async Task<IActionResult> RevokeShareToken(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var template = await _reportStore.GetByIdAsync(id, tenantId);
        if (template == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        template.IsPublic = false;
        template.ShareToken = null;

        await _reportStore.UpdateAsync(template, tenantId);
        _logger.LogInformation("Share token revoked for report template {TemplateId}", id);

        return NoContent();
    }

    // =========================================================================
    // DATA RETRIEVAL
    // =========================================================================

    /// <summary>
    /// GET api/admin/reports/templates/{id}/data?$top=1000&amp;$filter=...
    /// Fetches the report data by forwarding the OData query internally.
    /// </summary>
    [HttpGet("templates/{id:guid}/data")]
    [Authorize(Policy = "AdminKeyPolicy")]
    public async Task<IActionResult> GetReportData(
        Guid id,
        [FromQuery(Name = "$top")] int top = 1000,
        [FromQuery(Name = "$filter")] string? filter = null,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top < 0 || top > 5000)
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                "$top must be between 0 and 5000"));

        var tenantId = HttpContext.GetRequiredTenantId();
        var template = await _reportStore.GetByIdAsync(id, tenantId);
        if (template == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        top = Math.Clamp(top, 1, 5000);

        var fieldNames = template.Fields.Select(f => f.Name).ToList();
        var selectParam = fieldNames.Count > 0 ? string.Join(",", fieldNames) : null;

        // Build OData URL: /odata/{module}/{entityType}?$select={fields}&$top={top}&$filter={filter}&$count=true
        var queryParts = new List<string>();
        if (selectParam != null)
        {
            queryParts.Add($"$select={Uri.EscapeDataString(selectParam)}");
        }
        queryParts.Add($"$top={top}");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");
        }
        queryParts.Add("$count=true");

        // Apply sort from template
        if (template.SortBy.Count > 0)
        {
            var orderBy = string.Join(",", template.SortBy.Select(s =>
                s.Direction?.ToLowerInvariant() == "desc"
                    ? $"{s.Field} desc"
                    : s.Field));
            queryParts.Add($"$orderby={Uri.EscapeDataString(orderBy)}");
        }

        var odataPath = $"/odata/{template.Module}/{template.EntityType}?{string.Join("&", queryParts)}";

        try
        {
            var client = _httpClientFactory.CreateClient("BatchInternal");

            // Build absolute URL from the current request context
            var requestUri = new Uri(GetBaseUrl() + odataPath);

            using var odataRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);

            // Forward tenant header if present (validate GUID format to prevent header injection)
            if (Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) && tenantHeader.Count > 0)
            {
                var value = tenantHeader.ToString();
                if (Guid.TryParse(value, out _))
                {
                    odataRequest.Headers.TryAddWithoutValidation("X-Tenant-Id", value);
                }
            }

            // Forward authorization header (validate Bearer token format, no control characters)
            if (Request.Headers.TryGetValue("Authorization", out var authHeader) && authHeader.Count > 0)
            {
                var value = authHeader.ToString();
                if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && IsValidHeaderValue(value))
                {
                    odataRequest.Headers.TryAddWithoutValidation("Authorization", value);
                }
            }

            // Forward admin key header (validate printable ASCII only, no control characters)
            if (Request.Headers.TryGetValue("X-Admin-Key", out var adminKey) && adminKey.Count > 0)
            {
                var value = adminKey.ToString();
                if (IsValidHeaderValue(value))
                {
                    odataRequest.Headers.TryAddWithoutValidation("X-Admin-Key", value);
                }
            }

            using var response = await client.SendAsync(odataRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "OData call failed for report {TemplateId}: {StatusCode} {Body}",
                    id, (int)response.StatusCode, errorBody);
                return StatusCode((int)response.StatusCode, new { error = $"OData query failed: {errorBody}" });
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var rows = new List<Dictionary<string, object?>>();
            if (root.TryGetProperty("value", out var valueArray) && valueArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.EnumerateObject())
                    {
                        row[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => prop.Value.GetRawText()
                        };
                    }
                    rows.Add(row);
                }
            }

            var totalCount = rows.Count;
            if (root.TryGetProperty("@odata.count", out var countProp) && countProp.TryGetInt32(out var count))
            {
                totalCount = count;
            }

            return Ok(new ReportDataResponse(ToDto(template), rows, totalCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch data for report template {TemplateId}", id);
            return StatusCode(500, new { error = "Failed to fetch report data. Check server logs for details." });
        }
    }

    // =========================================================================
    // SCHEDULED SEND
    // =========================================================================

    /// <summary>
    /// POST api/admin/reports/templates/{id}/schedule-send
    /// Triggers a scheduled report delivery (stub).
    /// </summary>
    [HttpPost("templates/{id:guid}/schedule-send")]
    public async Task<IActionResult> TriggerScheduledSend(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetRequiredTenantId();
        var template = await _reportStore.GetByIdAsync(id, tenantId);
        if (template == null)
        {
            return NotFound(new { error = $"Report template {id} not found." });
        }

        _logger.LogInformation(
            "Scheduled send triggered for report template {TemplateId} ({Name}), recipients: {Recipients}",
            id, template.Name, string.Join(", ", template.ScheduleRecipients));

        return Accepted(new { message = "Scheduled delivery triggered (stub)" });
    }

    // =========================================================================
    // PUBLIC SHARE ENDPOINT
    // =========================================================================

    /// <summary>
    /// GET api/reports/{shareToken}
    /// Public endpoint — returns report template metadata by share token.
    /// No authentication required; share token acts as capability URL.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [Route("api/reports/{shareToken}")]
    public async Task<IActionResult> GetSharedReport(string shareToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shareToken))
        {
            return BadRequest(new { error = "Share token is required." });
        }

        ReportTemplate? template;
        try
        {
            template = await _reportStore.GetByShareTokenAsync(shareToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid share token format (length={TokenLength})", shareToken?.Length ?? 0);
            return NotFound(new { error = "Report not found." });
        }

        if (template == null || !template.IsPublic)
        {
            return NotFound(new { error = "Report not found." });
        }

        return Ok(ToDto(template));
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static bool IsValidHeaderValue(string value)
    {
        return !string.IsNullOrEmpty(value) &&
               value.All(c => c >= 0x20 && c < 0x7F);  // Printable ASCII only — blocks CRLF injection
    }

    private static ReportTemplateDto ToDto(ReportTemplate t) =>
        new(
            t.Id,
            t.Name,
            t.Description,
            t.Module,
            t.EntityType,
            t.LayoutType,
            t.Fields,
            t.GroupBy,
            t.SortBy,
            t.Header,
            t.Footer,
            t.IsPublic,
            t.ShareToken,
            t.ScheduleCron,
            t.ScheduleRecipients,
            t.CreatedAt,
            t.UpdatedAt);
}
