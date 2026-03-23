using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Api.Plugins;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Plugins.Loading;
using BMMDL.Runtime.Plugins.Staging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMMDL.Runtime.Api.Controllers;

/// <summary>
/// Admin API for managing platform plugins.
/// All endpoints require admin authorization via X-Admin-Key header
/// (same policy as <see cref="RuntimeAdminController"/>).
///
/// Provides lifecycle management (install/enable/disable/uninstall),
/// settings configuration, and manifest aggregation for the frontend.
/// </summary>
[ApiController]
[Route("api/plugins")]
[Authorize(Policy = "AdminKeyPolicy")]
[Tags("Plugins")]
public class PluginController : ControllerBase
{
    private readonly IPluginManager _pluginManager;
    private readonly PluginManifestService _manifestService;
    private readonly PluginStagingService? _stagingService;
    private readonly ILogger<PluginController> _logger;

    public PluginController(
        IPluginManager pluginManager,
        PluginManifestService manifestService,
        ILogger<PluginController> logger,
        PluginStagingService? stagingService = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        _stagingService = stagingService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// List all registered plugins with their current state.
    /// Returns name, status, version, timestamps, capabilities, and dependencies.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PluginListResponse), 200)]
    public async Task<IActionResult> ListPlugins(CancellationToken ct)
    {
        _logger.LogInformation("Admin listing all plugins");

        var states = await _pluginManager.GetAllPluginStatesAsync(ct);
        var items = states.Select(s => MapToResponse(s)).ToList();

        return Ok(new PluginListResponse { Value = items });
    }

    /// <summary>
    /// Get detailed information for a specific plugin.
    /// Includes state, settings schema, and dependencies.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(PluginDetailResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    public async Task<IActionResult> GetPlugin(string name, CancellationToken ct)
    {
        _logger.LogInformation("Admin requesting plugin detail: {PluginName}", name);

        var state = await _pluginManager.GetPluginStateAsync(name, ct);
        if (state == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                $"Plugin '{name}' not found in registry"));
        }

        return Ok(MapToDetailResponse(state));
    }

    /// <summary>
    /// Install a plugin. Runs pending migrations and sets status to Installed.
    /// The plugin must be registered in the platform feature registry.
    /// </summary>
    [HttpPost("{name}/install")]
    [ProducesResponseType(typeof(PluginDetailResponse), 201)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    public async Task<IActionResult> InstallPlugin(string name, CancellationToken ct)
    {
        _logger.LogInformation("Admin installing plugin: {PluginName}", name);

        try
        {
            var state = await _pluginManager.InstallPluginAsync(name, ct);

            _logger.LogInformation("Plugin installed: {PluginName} (version {Version})",
                name, state.Version);

            return CreatedAtAction(
                nameof(GetPlugin),
                new { name },
                MapToDetailResponse(state));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already installed"))
        {
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginAlreadyInstalled,
                ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to install plugin: {PluginName}", name);
            return StatusCode(500, ODataErrorResponse.FromException(
                ErrorCodes.PluginInstallFailed,
                $"Failed to install plugin '{name}'. Check server logs for details."));
        }
    }

    /// <summary>
    /// Enable a previously installed plugin.
    /// Calls the plugin's OnEnabledAsync lifecycle hook.
    /// All dependencies must be enabled first.
    /// </summary>
    [HttpPost("{name}/enable")]
    [ProducesResponseType(typeof(PluginDetailResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    public async Task<IActionResult> EnablePlugin(string name, CancellationToken ct)
    {
        _logger.LogInformation("Admin enabling plugin: {PluginName}", name);

        var existing = await _pluginManager.GetPluginStateAsync(name, ct);
        if (existing == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                $"Plugin '{name}' not found in registry"));
        }

        if (existing.Status == PluginStatus.Enabled)
        {
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginAlreadyEnabled,
                $"Plugin '{name}' is already enabled"));
        }

        try
        {
            var state = await _pluginManager.EnablePluginAsync(name, ct);

            _logger.LogInformation("Plugin enabled: {PluginName}", name);
            return Ok(MapToDetailResponse(state));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot enable plugin: {PluginName}", name);
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginDependencyNotMet,
                ex.Message));
        }
    }

    /// <summary>
    /// Disable an enabled plugin.
    /// Fails if other enabled plugins depend on this one.
    /// Calls the plugin's OnDisabledAsync lifecycle hook.
    /// </summary>
    [HttpPost("{name}/disable")]
    [ProducesResponseType(typeof(PluginDetailResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    public async Task<IActionResult> DisablePlugin(string name, CancellationToken ct)
    {
        _logger.LogInformation("Admin disabling plugin: {PluginName}", name);

        var existing = await _pluginManager.GetPluginStateAsync(name, ct);
        if (existing == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                $"Plugin '{name}' not found in registry"));
        }

        if (existing.Status != PluginStatus.Enabled)
        {
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotEnabled,
                $"Plugin '{name}' is not enabled (status: {existing.Status})"));
        }

        try
        {
            var state = await _pluginManager.DisablePluginAsync(name, ct);

            _logger.LogInformation("Plugin disabled: {PluginName}", name);
            return Ok(MapToDetailResponse(state));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot disable plugin: {PluginName}", name);
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginHasDependents,
                ex.Message));
        }
    }

    /// <summary>
    /// Uninstall a disabled plugin.
    /// Runs down-migrations and removes plugin state.
    /// The plugin must be disabled first.
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    public async Task<IActionResult> UninstallPlugin(string name, CancellationToken ct)
    {
        _logger.LogInformation("Admin uninstalling plugin: {PluginName}", name);

        var existing = await _pluginManager.GetPluginStateAsync(name, ct);
        if (existing == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                $"Plugin '{name}' not found in registry"));
        }

        if (existing.Status == PluginStatus.Enabled)
        {
            return Conflict(ODataErrorResponse.FromException(
                ErrorCodes.PluginMustBeDisabled,
                $"Plugin '{name}' must be disabled before uninstalling"));
        }

        try
        {
            await _pluginManager.UninstallPluginAsync(name, ct);

            _logger.LogInformation("Plugin uninstalled: {PluginName}", name);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to uninstall plugin: {PluginName}", name);
            return StatusCode(500, ODataErrorResponse.FromException(
                ErrorCodes.PluginUninstallFailed,
                $"Failed to uninstall plugin '{name}'. Check server logs for details."));
        }
    }

    /// <summary>
    /// Update settings for an installed plugin.
    /// Settings are validated via the plugin's ISettingsProvider if available.
    /// </summary>
    [HttpPut("{name}/settings")]
    [ProducesResponseType(typeof(PluginSettingsResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 400)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    public async Task<IActionResult> UpdateSettings(
        string name,
        [FromBody] Dictionary<string, object?> settings,
        CancellationToken ct)
    {
        _logger.LogInformation("Admin updating settings for plugin: {PluginName}", name);

        var existing = await _pluginManager.GetPluginStateAsync(name, ct);
        if (existing == null)
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                $"Plugin '{name}' not found in registry"));
        }

        try
        {
            var updatedSettings = await _pluginManager.UpdateSettingsAsync(name, settings, ct);

            _logger.LogInformation("Plugin settings updated: {PluginName}", name);
            return Ok(new PluginSettingsResponse
            {
                PluginName = name,
                Settings = updatedSettings
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid settings for plugin: {PluginName}", name);
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                ex.Message));
        }
    }

    /// <summary>
    /// Install BMMDL modules bundled with a plugin into the Registry.
    /// This is called automatically during plugin installation if modules have autoInstall=true,
    /// but can also be triggered manually.
    /// </summary>
    [HttpPost("{name}/install-modules")]
    [ProducesResponseType(typeof(PluginModuleInstallResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    public async Task<IActionResult> InstallPluginModules(
        string name,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Admin installing BMMDL modules for plugin: {PluginName} (force={Force})", name, force);

        try
        {
            var results = await _pluginManager.InstallPluginModulesAsync(name, force, ct);

            return Ok(new PluginModuleInstallResponse
            {
                PluginName = name,
                Results = results.Select(r => new ModuleInstallResultDto
                {
                    Success = r.Success,
                    EntityCount = r.EntityCount,
                    ServiceCount = r.ServiceCount,
                    Errors = r.Errors,
                    Warnings = r.Warnings,
                    SchemaResult = r.SchemaResult
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install BMMDL modules for plugin: {PluginName}", name);
            return StatusCode(500, ODataErrorResponse.FromException(
                ErrorCodes.PluginInstallFailed,
                $"Failed to install BMMDL modules for plugin '{name}'. Check server logs for details."));
        }
    }

    /// <summary>
    /// Get the aggregated plugin manifest for the frontend.
    /// Collects menu items, page definitions, and settings schemas
    /// from all enabled plugins into a single response.
    /// </summary>
    [HttpGet("manifest")]
    [ProducesResponseType(typeof(PluginManifest), 200)]
    public async Task<IActionResult> GetManifest(CancellationToken ct)
    {
        _logger.LogDebug("Fetching plugin manifest for frontend");

        var manifest = await _manifestService.GetManifestAsync(ct);
        return Ok(manifest);
    }

    // ── Dynamic Loading Endpoints ────────────────────────────────

    /// <summary>
    /// Scan the plugins directory for new plugins and load them.
    /// Already-loaded plugins are skipped.
    /// </summary>
    [HttpPost("scan")]
    [ProducesResponseType(typeof(PluginScanResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    public IActionResult ScanPluginDirectory()
    {
        _logger.LogInformation("Admin scanning plugins directory");

        try
        {
            var loaded = _pluginManager.ScanPluginDirectory();

            return Ok(new PluginScanResponse
            {
                NewlyLoaded = loaded,
                TotalLoaded = _pluginManager.GetLoadedExternalPlugins().Count
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed,
                ex.Message));
        }
    }

    /// <summary>
    /// Load a plugin from a specific directory or zip file path.
    /// If the path ends with <c>.zip</c>, the archive is extracted first.
    /// </summary>
    [HttpPost("load")]
    [ProducesResponseType(typeof(PluginLoadResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 400)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    public IActionResult LoadPlugin([FromBody] PluginLoadRequest request)
    {
        var path = request.Path ?? request.Directory;
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                "Plugin path is required (use 'path' or 'directory' field)"));
        }

        // V2: Defense-in-depth path traversal check at controller level.
        // The PluginDirectoryLoader also validates this, but we catch it early here.
        // Append DirectorySeparatorChar to prevent "/pluginsevil/" matching "/plugins".
        var fullPath = Path.GetFullPath(path);
        var pluginsDir = Path.GetFullPath("plugins");
        if (!fullPath.StartsWith(pluginsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, pluginsDir, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected plugin load from outside plugins directory: {Path}", path);
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                "Plugin path must be within the plugins directory."));
        }

        _logger.LogInformation("Admin loading plugin from: {Path}", path);

        try
        {
            var isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var descriptor = isZip
                ? _pluginManager.LoadPluginFromZip(path)
                : _pluginManager.LoadPluginFromDirectory(path);

            if (descriptor is null)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    ErrorCodes.PluginLoadFailed,
                    $"No valid plugin found at: {path}"));
            }

            return Ok(new PluginLoadResponse
            {
                Name = descriptor.Manifest.Name,
                Version = descriptor.Manifest.Version,
                Description = descriptor.Manifest.Description,
                FeatureCount = descriptor.Features.Count,
                Features = descriptor.Features.Select(f => f.Name).ToList()
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed,
                ex.Message));
        }
    }

    /// <summary>
    /// Upload a plugin <c>.zip</c> file for staging and validation.
    /// The zip is extracted to a staging directory, validated, and a staging record
    /// is created. The plugin is NOT loaded or installed until explicitly approved
    /// via <c>POST /api/plugins/staging/{id}/approve</c>.
    ///
    /// The zip must contain <c>plugin.json</c> at the root or inside a single
    /// top-level directory.
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(PluginStagingResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 400)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB max
    public async Task<IActionResult> UploadPlugin(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                "A .zip file is required"));
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ODataErrorResponse.FromException(
                ODataConstants.ErrorCodes.ValidationError,
                $"File must be a .zip archive, got: {file.FileName}"));
        }

        if (_stagingService == null)
        {
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed,
                "Plugin staging service is not available. Ensure dynamic plugin loading is configured."));
        }

        _logger.LogInformation("Admin uploading plugin zip for staging: {FileName} ({Size} bytes)",
            file.FileName, file.Length);

        try
        {
            using var stream = file.OpenReadStream();
            var staging = await _stagingService.StagePluginAsync(stream, file.FileName, ct);

            return Ok(MapToStagingResponse(staging));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed,
                ex.Message));
        }
    }

    /// <summary>
    /// Unload a previously loaded external plugin.
    /// The plugin must be disabled and uninstalled first.
    /// Removes features from registry and releases the assembly.
    /// </summary>
    [HttpPost("{name}/unload")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ODataErrorResponse), 400)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    public IActionResult UnloadPlugin(string name)
    {
        _logger.LogInformation("Admin unloading plugin: {PluginName}", name);

        try
        {
            _pluginManager.UnloadPlugin(name);
            _logger.LogInformation("Plugin '{PluginName}' unloaded successfully", name);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not loaded"))
        {
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound,
                ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ODataErrorResponse.FromException(
                ErrorCodes.PluginUnloadFailed,
                ex.Message));
        }
    }

    /// <summary>
    /// List all currently loaded external plugins (DLL-based).
    /// </summary>
    [HttpGet("external")]
    [ProducesResponseType(typeof(ExternalPluginListResponse), 200)]
    public IActionResult ListExternalPlugins()
    {
        var loaded = _pluginManager.GetLoadedExternalPlugins();

        var items = loaded.Values.Select(d => new ExternalPluginResponse
        {
            Name = d.Manifest.Name,
            Version = d.Manifest.Version,
            Description = d.Manifest.Description,
            Author = d.Manifest.Author,
            DirectoryPath = d.DirectoryPath,
            FeatureCount = d.Features.Count,
            Features = d.Features.Select(f => f.Name).ToList(),
            LoadedAt = d.LoadedAt
        }).ToList();

        return Ok(new ExternalPluginListResponse { Value = items });
    }

    // ── Staging Endpoints ─────────────────────────────────────────

    /// <summary>
    /// List all staged (uploaded but not yet installed) plugins.
    /// </summary>
    [HttpGet("staging")]
    [ProducesResponseType(typeof(PluginStagingListResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 503)]
    public async Task<IActionResult> ListStagedPlugins(CancellationToken ct)
    {
        if (_stagingService == null)
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed, "Plugin staging service is not available."));

        var staged = await _stagingService.GetStagedPluginsAsync(ct);
        return Ok(new PluginStagingListResponse
        {
            Value = staged.Select(MapToStagingResponse).ToList()
        });
    }

    /// <summary>
    /// Get details of a specific staged plugin including validation results.
    /// </summary>
    [HttpGet("staging/{id:int}")]
    [ProducesResponseType(typeof(PluginStagingResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 503)]
    public async Task<IActionResult> GetStagedPlugin(int id, CancellationToken ct)
    {
        if (_stagingService == null)
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed, "Plugin staging service is not available."));

        var staging = await _stagingService.GetStagedPluginAsync(id, ct);
        if (staging == null)
            return NotFound(ODataErrorResponse.FromException(
                ErrorCodes.PluginNotFound, $"Staged plugin with ID {id} not found"));

        return Ok(MapToStagingResponse(staging));
    }

    /// <summary>
    /// Re-run validation on a staged plugin.
    /// </summary>
    [HttpPost("staging/{id:int}/validate")]
    [ProducesResponseType(typeof(PluginStagingResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    [ProducesResponseType(typeof(ODataErrorResponse), 503)]
    public async Task<IActionResult> RevalidateStagedPlugin(int id, CancellationToken ct)
    {
        if (_stagingService == null)
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed, "Plugin staging service is not available."));

        try
        {
            var staging = await _stagingService.RevalidateAsync(id, ct);
            return Ok(MapToStagingResponse(staging));
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("not found")
                ? NotFound(ODataErrorResponse.FromException(ErrorCodes.PluginNotFound, ex.Message))
                : Conflict(ODataErrorResponse.FromException(ErrorCodes.PluginStagingError, ex.Message));
        }
    }

    /// <summary>
    /// Approve a staged plugin: moves it to the live plugins directory,
    /// loads the DLL, and registers features. After approval, the plugin
    /// can be installed and enabled via the standard lifecycle endpoints.
    /// </summary>
    [HttpPost("staging/{id:int}/approve")]
    [ProducesResponseType(typeof(PluginLoadResponse), 200)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    [ProducesResponseType(typeof(ODataErrorResponse), 500)]
    [ProducesResponseType(typeof(ODataErrorResponse), 503)]
    public async Task<IActionResult> ApproveStagedPlugin(int id, CancellationToken ct)
    {
        if (_stagingService == null)
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed, "Plugin staging service is not available."));

        _logger.LogInformation("Admin approving staged plugin: {StagingId}", id);

        try
        {
            // Fetch staging record before approval so we can use its metadata in all cases
            var staging = await _stagingService.GetStagedPluginAsync(id, ct);
            var descriptor = await _stagingService.ApproveAsync(id, ct);

            if (descriptor == null)
            {
                // Loader not available — use staging record metadata
                return Ok(new PluginLoadResponse
                {
                    Name = staging?.Name ?? "unknown",
                    Version = staging?.Version ?? "unknown",
                    Description = staging?.Description,
                    FeatureCount = 0,
                    Features = []
                });
            }

            return Ok(new PluginLoadResponse
            {
                Name = descriptor.Manifest.Name,
                Version = descriptor.Manifest.Version,
                Description = descriptor.Manifest.Description,
                FeatureCount = descriptor.Features.Count,
                Features = descriptor.Features.Select(f => f.Name).ToList()
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot approve staged plugin {StagingId}", id);
            return ex.Message.Contains("not found")
                ? NotFound(ODataErrorResponse.FromException(ErrorCodes.PluginNotFound, ex.Message))
                : Conflict(ODataErrorResponse.FromException(ErrorCodes.PluginStagingError, ex.Message));
        }
    }

    /// <summary>
    /// Reject a staged plugin: cleans up staging files and removes the record.
    /// </summary>
    [HttpDelete("staging/{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ODataErrorResponse), 404)]
    [ProducesResponseType(typeof(ODataErrorResponse), 409)]
    [ProducesResponseType(typeof(ODataErrorResponse), 503)]
    public async Task<IActionResult> RejectStagedPlugin(int id, CancellationToken ct)
    {
        if (_stagingService == null)
            return StatusCode(503, ODataErrorResponse.FromException(
                ErrorCodes.PluginLoadFailed, "Plugin staging service is not available."));

        _logger.LogInformation("Admin rejecting staged plugin: {StagingId}", id);

        try
        {
            await _stagingService.RejectAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("not found")
                ? NotFound(ODataErrorResponse.FromException(ErrorCodes.PluginNotFound, ex.Message))
                : Conflict(ODataErrorResponse.FromException(ErrorCodes.PluginStagingError, ex.Message));
        }
    }

    #region Helpers

    private static PluginResponse MapToResponse(PluginStateRecord state) => new()
    {
        Name = state.Name,
        Version = state.Version,
        Status = state.Status.ToString().ToLowerInvariant(),
        InstalledAt = state.InstalledAt,
        EnabledAt = state.EnabledAt
    };

    private static PluginDetailResponse MapToDetailResponse(PluginStateRecord state) => new()
    {
        Name = state.Name,
        Version = state.Version,
        Status = state.Status.ToString().ToLowerInvariant(),
        InstalledAt = state.InstalledAt,
        EnabledAt = state.EnabledAt,
        Settings = state.Settings
    };

    private static PluginStagingResponse MapToStagingResponse(PluginStagingRecord staging) => new()
    {
        Id = staging.Id,
        Name = staging.Name,
        Version = staging.Version,
        Description = staging.Description,
        Author = staging.Author,
        FileHash = staging.FileHash,
        FileSize = staging.FileSize,
        FileName = staging.FileName,
        ValidationStatus = staging.ValidationStatus.ToString().ToLowerInvariant(),
        UploadedAt = staging.UploadedAt,
        ApprovedAt = staging.ApprovedAt,
        ValidationResults = staging.ValidationResults.Select(r => new ValidationCheckResultDto
        {
            CheckName = r.CheckName,
            Passed = r.Passed,
            Severity = r.Severity.ToString().ToLowerInvariant(),
            Message = r.Message,
            Details = r.Details
        }).ToList()
    };

    #endregion

    #region Error Codes

    /// <summary>
    /// Plugin-specific error codes.
    /// </summary>
    private static class ErrorCodes
    {
        public const string PluginNotFound = "PLUGIN_NOT_FOUND";
        public const string PluginAlreadyInstalled = "PLUGIN_ALREADY_INSTALLED";
        public const string PluginAlreadyEnabled = "PLUGIN_ALREADY_ENABLED";
        public const string PluginNotEnabled = "PLUGIN_NOT_ENABLED";
        public const string PluginMustBeDisabled = "PLUGIN_MUST_BE_DISABLED";
        public const string PluginDependencyNotMet = "PLUGIN_DEPENDENCY_NOT_MET";
        public const string PluginHasDependents = "PLUGIN_HAS_DEPENDENTS";
        public const string PluginInstallFailed = "PLUGIN_INSTALL_FAILED";
        public const string PluginUninstallFailed = "PLUGIN_UNINSTALL_FAILED";
        public const string PluginLoadFailed = "PLUGIN_LOAD_FAILED";
        public const string PluginUnloadFailed = "PLUGIN_UNLOAD_FAILED";
        public const string PluginStagingError = "PLUGIN_STAGING_ERROR";
    }

    #endregion
}

#region Response Models

/// <summary>
/// Plugin list response.
/// </summary>
public class PluginListResponse
{
    public required IReadOnlyList<PluginResponse> Value { get; init; }
}

/// <summary>
/// Summary plugin response for list views.
/// </summary>
public class PluginResponse
{
    public required string Name { get; init; }
    public required int Version { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset? EnabledAt { get; init; }
}

/// <summary>
/// Detailed plugin response including settings.
/// </summary>
public class PluginDetailResponse : PluginResponse
{
    public Dictionary<string, object?> Settings { get; init; } = new();
}

/// <summary>
/// Response for settings update.
/// </summary>
public class PluginSettingsResponse
{
    public required string PluginName { get; init; }
    public required Dictionary<string, object?> Settings { get; init; }
}

/// <summary>
/// Request to load a plugin from a directory or zip file path.
/// Use <c>path</c> (preferred) or <c>directory</c> (backward compatible).
/// If the path ends with <c>.zip</c>, the archive is extracted automatically.
/// </summary>
public class PluginLoadRequest
{
    /// <summary>
    /// Path to plugin directory or .zip file. Preferred over <see cref="Directory"/>.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Path to plugin directory. Backward compatible — use <see cref="Path"/> for new code.
    /// </summary>
    public string? Directory { get; init; }
}

/// <summary>
/// Response for plugin scan operation.
/// </summary>
public class PluginScanResponse
{
    public required IReadOnlyList<string> NewlyLoaded { get; init; }
    public required int TotalLoaded { get; init; }
}

/// <summary>
/// Response for single plugin load operation.
/// </summary>
public class PluginLoadResponse
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public required int FeatureCount { get; init; }
    public required List<string> Features { get; init; }
}

/// <summary>
/// Response for listing external (DLL-loaded) plugins.
/// </summary>
public class ExternalPluginListResponse
{
    public required IReadOnlyList<ExternalPluginResponse> Value { get; init; }
}

/// <summary>
/// Detail about a loaded external plugin.
/// </summary>
public class ExternalPluginResponse
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public required string DirectoryPath { get; init; }
    public required int FeatureCount { get; init; }
    public required List<string> Features { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }
}

/// <summary>
/// Response from plugin BMMDL module installation.
/// </summary>
public class PluginModuleInstallResponse
{
    public required string PluginName { get; init; }
    public required List<ModuleInstallResultDto> Results { get; init; }
}

/// <summary>
/// Result of a single BMMDL module compilation/installation.
/// </summary>
public class ModuleInstallResultDto
{
    public bool Success { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? SchemaResult { get; init; }
}

// ── Staging Response Models ────────────────────────────────────

/// <summary>
/// List of staged plugins.
/// </summary>
public class PluginStagingListResponse
{
    public required IReadOnlyList<PluginStagingResponse> Value { get; init; }
}

/// <summary>
/// Response for a staged plugin with validation details.
/// </summary>
public class PluginStagingResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public required string FileHash { get; init; }
    public required long FileSize { get; init; }
    public required string FileName { get; init; }
    public required string ValidationStatus { get; init; }
    public required DateTimeOffset UploadedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public required IReadOnlyList<ValidationCheckResultDto> ValidationResults { get; init; }
}

/// <summary>
/// DTO for a single validation check result.
/// </summary>
public class ValidationCheckResultDto
{
    public required string CheckName { get; init; }
    public required bool Passed { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
}

#endregion
