using BMMDL.Registry.Api.Models;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Administrative operations facade.
/// Coordinates compilation, schema management, and module discovery.
/// </summary>
public interface IAdminService
{
    Task<ClearDatabaseResponse> ClearDatabaseAsync(ClearDatabaseRequest request);
    Task<CompileResponse> CompileAndInstallAsync(CompileRequest request);
    Task<BootstrapResponse> BootstrapPlatformAsync();
    Task<UninstallModuleResponse> UninstallModuleByNameAsync(string moduleName, Guid? tenantId = null);
    Task<List<ModuleStatusDto>> GetModulesWithSchemaStatusAsync();
    Task<DependencyGraphResponse> GetDependencyGraphAsync();
    Task<DdlPreviewResponse> PreviewDdlAsync(DdlPreviewRequest request);
}
