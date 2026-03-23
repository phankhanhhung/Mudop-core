using BMMDL.Registry.Api.Models;
using BMMDL.Registry.Api.Services;
using BMMDL.Registry.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Registry.Api.Controllers;

/// <summary>
/// Administrative API endpoints for database management and module compilation.
/// Requires X-Admin-Key header for authentication (via AdminKeyPolicy).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminKeyPolicy")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;
    private readonly RegistryDbContext _registryDb;

    public AdminController(
        IAdminService adminService,
        ILogger<AdminController> logger,
        RegistryDbContext registryDb)
    {
        _adminService = adminService;
        _logger = logger;
        _registryDb = registryDb;
    }

    /// <summary>
    /// Clear database - drop business schemas and/or truncate registry tables.
    /// WARNING: This is a destructive operation!
    /// </summary>
    /// <remarks>
    /// Requires X-Admin-Key header for authentication.
    ///
    /// Example request:
    /// ```json
    /// {
    ///   "clearRegistry": true,
    ///   "dropSchemas": true,
    ///   "schemas": null  // null = all known schemas
    /// }
    /// ```
    /// </remarks>
    [HttpPost("clear-database")]
    [ProducesResponseType<ClearDatabaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ClearDatabase([FromBody] ClearDatabaseRequest request)
    {
        _logger.LogWarning("Clear database requested: DropSchemas={DropSchemas}, ClearRegistry={ClearRegistry}",
            request.DropSchemas, request.ClearRegistry);

        var result = await _adminService.ClearDatabaseAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Compile BMMDL source and install into system.
    /// </summary>
    /// <remarks>
    /// Requires X-Admin-Key header for authentication.
    ///
    /// Example request:
    /// ```json
    /// {
    ///   "bmmdlSource": "module TestModule { ... }",
    ///   "moduleName": "TestModule",
    ///   "tenantId": null,  // null = system tenant
    ///   "publish": true,
    ///   "initSchema": false,
    ///   "force": false
    /// }
    /// ```
    /// </remarks>
    [HttpPost("compile")]
    [ProducesResponseType<CompileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Compile([FromBody] CompileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BmmdlSource))
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "BmmdlSource is required"));
        }

        if (string.IsNullOrWhiteSpace(request.ModuleName))
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "ModuleName is required"));
        }

        _logger.LogInformation("Compile requested: Module={ModuleName}, Publish={Publish}, InitSchema={InitSchema}",
            request.ModuleName, request.Publish, request.InitSchema);

        var result = await _adminService.CompileAndInstallAsync(request);

        if (!result.Success)
        {
            // Return 200 with failure status so client can read error details
            return Ok(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Bootstrap platform - compile and install Module 0 (Platform).
    /// Equivalent to "bmmdlc bootstrap --init-platform".
    /// </summary>
    [HttpPost("bootstrap")]
    [ProducesResponseType<BootstrapResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BootstrapPlatform()
    {
        _logger.LogWarning("Platform bootstrap requested via API");
        var result = await _adminService.BootstrapPlatformAsync();
        return Ok(result);
    }

    /// <summary>
    /// List all published modules with schema initialization status.
    /// </summary>
    [HttpGet("modules")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListModules()
    {
        var result = await _adminService.GetModulesWithSchemaStatusAsync();
        return Ok(result);
    }

    /// <summary>
    /// Preview DDL that would be generated for the given BMMDL source without executing it.
    /// </summary>
    [HttpPost("ddl-preview")]
    [ProducesResponseType<DdlPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PreviewDdl([FromBody] DdlPreviewRequest request)
    {
        var result = await _adminService.PreviewDdlAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Uninstall a module by name.
    /// Checks for dependent modules, drops schema tables, and removes registry metadata.
    /// Returns 409 Conflict if other modules depend on this one.
    /// </summary>
    [HttpDelete("modules/{moduleName}")]
    [ProducesResponseType<UninstallModuleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UninstallModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return BadRequest(new { error = "Module name is required." });

        _logger.LogWarning("Module uninstall requested: {ModuleName}", moduleName);

        var result = await _adminService.UninstallModuleByNameAsync(moduleName);

        if (!result.Success)
        {
            if (result.DependentModules is { Count: > 0 })
            {
                return Conflict(result);
            }

            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Get module dependency graph as a JSON structure with nodes and edges.
    /// Useful for visualizing module relationships.
    /// </summary>
    [HttpGet("modules/dependency-graph")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDependencyGraph()
    {
        var result = await _adminService.GetDependencyGraphAsync();
        return Ok(result);
    }

    /// <summary>
    /// Health check for admin endpoints.
    /// Verifies database connectivity. Returns 503 if DB is unreachable.
    /// Does not require authentication.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health()
    {
        bool dbHealthy;
        try
        {
            dbHealthy = await _registryDb.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check: database connectivity failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                database = "unreachable",
                error = ex.Message
            });
        }

        if (!dbHealthy)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                database = "unreachable"
            });
        }

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            database = "connected"
        });
    }
}
