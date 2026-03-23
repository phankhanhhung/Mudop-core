using System.Net.Http.Json;
using System.Text.Json;
using BMMDL.Runtime.Plugins;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Api.Services;

/// <summary>
/// HTTP client for communicating with the BMMDL Registry API.
/// Sends compilation requests to the Registry to install BMMDL modules from plugins.
/// </summary>
public sealed class RegistryHttpClient : IRegistryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegistryHttpClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RegistryHttpClient(HttpClient httpClient, ILogger<RegistryHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ModuleInstallResult> CompileAndInstallModuleAsync(
        ModuleInstallRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Sending BMMDL module '{ModuleName}' to Registry for compilation ({SourceLength} chars)",
            request.ModuleName, request.BmmdlSource.Length);

        var payload = new
        {
            bmmdlSource = request.BmmdlSource,
            moduleName = request.ModuleName,
            tenantId = request.TenantId,
            publish = true,
            initSchema = request.InitSchema,
            force = request.Force
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/admin/compile", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Registry API returned {StatusCode} for module '{ModuleName}': {Error}",
                    response.StatusCode, request.ModuleName, errorBody);

                return new ModuleInstallResult
                {
                    Success = false,
                    Errors = [$"Registry API returned {response.StatusCode}: {errorBody}"]
                };
            }

            var result = await response.Content.ReadFromJsonAsync<RegistryCompileResponse>(JsonOptions, ct);

            if (result is null)
            {
                return new ModuleInstallResult
                {
                    Success = false,
                    Errors = ["Failed to deserialize Registry API response"]
                };
            }

            _logger.LogInformation(
                "Module '{ModuleName}' compilation {Result}: {EntityCount} entities",
                request.ModuleName, result.Success ? "succeeded" : "failed", result.EntityCount);

            return new ModuleInstallResult
            {
                Success = result.Success,
                EntityCount = result.EntityCount,
                ServiceCount = result.ServiceCount,
                Errors = result.Errors,
                Warnings = result.Warnings,
                SchemaResult = result.SchemaResult
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Registry API for module '{ModuleName}'", request.ModuleName);
            return new ModuleInstallResult
            {
                Success = false,
                Errors = [$"Failed to connect to Registry API: {ex.Message}"]
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Registry API request timed out for module '{ModuleName}'", request.ModuleName);
            return new ModuleInstallResult
            {
                Success = false,
                Errors = ["Registry API request timed out"]
            };
        }
    }

    public async Task<TenantModuleListResult> GetInstalledModulesAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Fetching installed modules from Registry for tenant {TenantId}",
            tenantId);

        try
        {
            var response = await _httpClient.GetAsync($"api/registry/modules?tenantId={tenantId:D}", ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Registry API returned {StatusCode} when listing modules: {Error}",
                    response.StatusCode, errorBody);

                return new TenantModuleListResult
                {
                    Success = false,
                    Errors = [$"Registry API returned {response.StatusCode}: {errorBody}"]
                };
            }

            var modules = await response.Content.ReadFromJsonAsync<List<RegistryModuleStatusResponse>>(JsonOptions, ct);

            if (modules is null)
            {
                return new TenantModuleListResult
                {
                    Success = false,
                    Errors = ["Failed to deserialize Registry API response"]
                };
            }

            return new TenantModuleListResult
            {
                Success = true,
                Modules = modules.Select(m => new TenantModuleInfo
                {
                    Name = m.Name,
                    Version = m.Version,
                    Author = m.Author,
                    EntityCount = m.EntityCount,
                    ServiceCount = m.ServiceCount,
                    InstalledAt = m.CreatedAt,
                    SchemaInitialized = m.SchemaInitialized,
                    SchemaName = m.SchemaName
                }).ToList()
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Registry API when listing modules");
            return new TenantModuleListResult
            {
                Success = false,
                Errors = [$"Failed to connect to Registry API: {ex.Message}"]
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Registry API request timed out when listing modules");
            return new TenantModuleListResult
            {
                Success = false,
                Errors = ["Registry API request timed out"]
            };
        }
    }

    public async Task<TenantModuleInstallResult> InstallModuleForTenantAsync(
        Guid tenantId,
        string moduleName,
        string installedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        _logger.LogInformation(
            "Installing module '{ModuleName}' for tenant {TenantId} by {InstalledBy}",
            moduleName, tenantId, installedBy);

        var payload = new
        {
            moduleName,
            tenantId,
            installedBy,
            initSchema = true,
            force = false
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"api/admin/modules/{Uri.EscapeDataString(moduleName)}/tenants/{tenantId}",
                payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Registry API returned {StatusCode} when installing module '{ModuleName}' for tenant {TenantId}: {Error}",
                    response.StatusCode, moduleName, tenantId, errorBody);

                return new TenantModuleInstallResult
                {
                    Success = false,
                    ModuleName = moduleName,
                    Errors = [$"Registry API returned {response.StatusCode}: {errorBody}"]
                };
            }

            var result = await response.Content.ReadFromJsonAsync<RegistryCompileResponse>(JsonOptions, ct);

            if (result is null)
            {
                return new TenantModuleInstallResult
                {
                    Success = false,
                    ModuleName = moduleName,
                    Errors = ["Failed to deserialize Registry API response"]
                };
            }

            _logger.LogInformation(
                "Module '{ModuleName}' installation for tenant {TenantId} {Result}: {EntityCount} entities",
                moduleName, tenantId, result.Success ? "succeeded" : "failed", result.EntityCount);

            return new TenantModuleInstallResult
            {
                Success = result.Success,
                ModuleName = moduleName,
                EntityCount = result.EntityCount,
                ServiceCount = result.ServiceCount,
                Errors = result.Errors,
                Warnings = result.Warnings,
                SchemaResult = result.SchemaResult
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Failed to connect to Registry API when installing module '{ModuleName}' for tenant {TenantId}",
                moduleName, tenantId);
            return new TenantModuleInstallResult
            {
                Success = false,
                ModuleName = moduleName,
                Errors = [$"Failed to connect to Registry API: {ex.Message}"]
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Registry API request timed out when installing module '{ModuleName}' for tenant {TenantId}",
                moduleName, tenantId);
            return new TenantModuleInstallResult
            {
                Success = false,
                ModuleName = moduleName,
                Errors = ["Registry API request timed out"]
            };
        }
    }

    public async Task<TenantModuleUninstallResult> UninstallModuleForTenantAsync(
        Guid tenantId,
        string moduleName,
        string uninstalledBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        _logger.LogInformation(
            "Uninstalling module '{ModuleName}' from tenant {TenantId} by {UninstalledBy}",
            moduleName, tenantId, uninstalledBy);

        try
        {
            var response = await _httpClient.DeleteAsync(
                $"api/admin/modules/{Uri.EscapeDataString(moduleName)}/tenants/{tenantId}",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Registry API returned {StatusCode} when uninstalling module '{ModuleName}' from tenant {TenantId}: {Error}",
                    response.StatusCode, moduleName, tenantId, errorBody);

                // Try to parse as uninstall response (may contain DependentModules)
                try
                {
                    var errorResult = JsonSerializer.Deserialize<RegistryUninstallResponse>(errorBody, JsonOptions);
                    if (errorResult != null)
                    {
                        return new TenantModuleUninstallResult
                        {
                            Success = false,
                            Errors = errorResult.Error != null ? [errorResult.Error] : [$"Registry API returned {response.StatusCode}"],
                            Messages = errorResult.Messages,
                            DependentModules = errorResult.DependentModules
                        };
                    }
                }
                catch
                {
                    // Fall through to generic error
                }

                return new TenantModuleUninstallResult
                {
                    Success = false,
                    Errors = [$"Registry API returned {response.StatusCode}: {errorBody}"]
                };
            }

            var result = await response.Content.ReadFromJsonAsync<RegistryUninstallResponse>(JsonOptions, ct);

            if (result is null)
            {
                return new TenantModuleUninstallResult
                {
                    Success = false,
                    Errors = ["Failed to deserialize Registry API response"]
                };
            }

            _logger.LogInformation(
                "Module '{ModuleName}' uninstall from tenant {TenantId} {Result}",
                moduleName, tenantId, result.Success ? "succeeded" : "failed");

            return new TenantModuleUninstallResult
            {
                Success = result.Success,
                Messages = result.Messages,
                Errors = result.Error != null ? [result.Error] : [],
                DependentModules = result.DependentModules
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Failed to connect to Registry API when uninstalling module '{ModuleName}' from tenant {TenantId}",
                moduleName, tenantId);
            return new TenantModuleUninstallResult
            {
                Success = false,
                Errors = [$"Failed to connect to Registry API: {ex.Message}"]
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Registry API request timed out when uninstalling module '{ModuleName}' from tenant {TenantId}",
                moduleName, tenantId);
            return new TenantModuleUninstallResult
            {
                Success = false,
                Errors = ["Registry API request timed out"]
            };
        }
    }

    /// <summary>
    /// Internal model matching the Registry API CompileResponse shape.
    /// </summary>
    private sealed class RegistryCompileResponse
    {
        public bool Success { get; set; }
        public int EntityCount { get; set; }
        public int ServiceCount { get; set; }
        public List<string> Errors { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
        public string? SchemaResult { get; set; }
    }

    /// <summary>
    /// Internal model matching the Registry API ModuleStatusDto shape.
    /// </summary>
    private sealed class RegistryModuleStatusResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string? Author { get; set; }
        public int EntityCount { get; set; }
        public int ServiceCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool SchemaInitialized { get; set; }
        public int TableCount { get; set; }
        public string? SchemaName { get; set; }
    }

    /// <summary>
    /// Internal model matching the Registry API UninstallModuleResponse shape.
    /// </summary>
    private sealed class RegistryUninstallResponse
    {
        public bool Success { get; set; }
        public List<string> Messages { get; set; } = [];
        public string? Error { get; set; }
        public List<string>? DependentModules { get; set; }
    }
}
