namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Health check endpoint for monitoring and load balancer probes.
/// </summary>
[ApiController]
[Route("[controller]")]
[Tags("System")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly MetaModelCacheManager _cacheManager;

    public HealthController(
        ILogger<HealthController> logger,
        ITenantConnectionFactory connectionFactory,
        MetaModelCacheManager cacheManager)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
        _cacheManager = cacheManager;
    }

    /// <summary>
    /// Basic health check.
    /// </summary>
    /// <returns>Health status.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        _logger.LogDebug("Health check requested");

        return Ok(new HealthResponse
        {
            Status = "Healthy",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Readiness check - verifies the service is ready to receive traffic.
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness(CancellationToken ct)
    {
        var checks = new Dictionary<string, string>();
        var isReady = true;

        // Check database connection
        try
        {
            await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
            checks["database"] = "healthy";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database readiness check failed");
            checks["database"] = "unhealthy";
            isReady = false;
        }

        // Check MetaModel cache
        try
        {
            var cache = await _cacheManager.GetCacheAsync(ct);
            // Verify cache is loaded without exposing entity counts
            _ = cache.Model.Entities.Count;
            checks["cache"] = "healthy";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache readiness check failed");
            checks["cache"] = "unhealthy";
            isReady = false;
        }

        var response = new HealthResponse
        {
            Status = isReady ? "Ready" : "NotReady",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Timestamp = DateTime.UtcNow,
            Checks = checks
        };

        if (!isReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Liveness check - verifies the service is alive.
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetLiveness()
    {
        return Ok(new HealthResponse
        {
            Status = "Alive",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Timestamp = DateTime.UtcNow
        });
    }
}

/// <summary>
/// Health check response model.
/// </summary>
public record HealthResponse
{
    /// <summary>
    /// Current health status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// API version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Server timestamp.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Additional check results.
    /// </summary>
    public IDictionary<string, string>? Checks { get; init; }
}
