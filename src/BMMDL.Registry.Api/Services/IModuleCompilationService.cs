using BMMDL.Registry.Api.Models;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Handles BMMDL compilation, dependency resolution, model filtering,
/// and publishing compiled artifacts.
/// </summary>
public interface IModuleCompilationService
{
    Task<CompileResponse> CompileAndInstallAsync(CompileRequest request);
    Task<DdlPreviewResponse> PreviewDdlAsync(DdlPreviewRequest request);
    Task NotifyRuntimeCacheReloadAsync(List<string> warnings);
}
