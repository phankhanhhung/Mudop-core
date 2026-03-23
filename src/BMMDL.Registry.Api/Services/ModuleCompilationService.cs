using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Api.Models;
using BMMDL.Registry.Data;
using BMMDL.Registry.Repositories;
using BMMDL.Registry.Services;
using BMMDL.Runtime.Plugins;
using BMMDL.SchemaManager;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Handles BMMDL compilation, dependency resolution, model filtering,
/// and publishing compiled artifacts.
/// </summary>
public class ModuleCompilationService : IModuleCompilationService
{
    private readonly RegistryDbContext _registryDb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleCompilationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ModuleDiscoveryService _discoveryService;
    private readonly ISchemaManagementService _schemaService;
    private readonly VersioningService? _versioningService;
    private readonly EfCoreMetaModelRepository? _metaModelRepo;
    private readonly IModuleInstallationService? _installService;

    public ModuleCompilationService(
        RegistryDbContext registryDb,
        IConfiguration configuration,
        ILogger<ModuleCompilationService> logger,
        HttpClient httpClient,
        ModuleDiscoveryService discoveryService,
        ISchemaManagementService schemaService,
        VersioningService? versioningService = null,
        EfCoreMetaModelRepository? metaModelRepo = null,
        IModuleInstallationService? installService = null)
    {
        _registryDb = registryDb;
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
        _discoveryService = discoveryService;
        _schemaService = schemaService;
        _versioningService = versioningService;
        _metaModelRepo = metaModelRepo;
        _installService = installService;
    }

    /// <summary>
    /// Compile BMMDL source and optionally publish to registry and initialize schema.
    /// Automatically resolves dependencies by including common.bmmdl and dependency module sources.
    /// </summary>
    public async Task<CompileResponse> CompileAndInstallAsync(CompileRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        string? schemaResult = null;

        try
        {
            // Step 0: Check source hash to avoid unnecessary recompilation
            var sourceHash = DbPersistenceService.ComputeSourceHash(request.BmmdlSource);
            if (!request.Force)
            {
                var effectiveTenantId = request.TenantId ?? Guid.Empty;
                var existingModule = await _registryDb.Modules
                    .FirstOrDefaultAsync(m => m.TenantId == effectiveTenantId && m.Name == request.ModuleName);

                if (existingModule != null)
                {
                    var existingInstall = await _registryDb.ModuleInstallations
                        .FirstOrDefaultAsync(i => i.TenantId == effectiveTenantId && i.ModuleId == existingModule.Id);

                    if (existingInstall?.SourceHash != null && existingInstall.SourceHash == sourceHash)
                    {
                        _logger.LogInformation(
                            "Source hash unchanged for module {ModuleName} (hash={Hash}), skipping recompilation",
                            request.ModuleName, sourceHash);

                        return new CompileResponse
                        {
                            Success = true,
                            Warnings = new List<string> { "No changes detected — source hash matches the installed version. Use force=true to recompile." },
                            CompilationTime = TimeSpan.Zero,
                            SchemaResult = "Skipped (no source changes)"
                        };
                    }
                }
            }

            // Step 1: Compile from string (with dependency resolution)
            _logger.LogInformation("Starting compilation for module: {ModuleName}", request.ModuleName);

            var options = new CompilationOptions
            {
                Verbose = false,
                ShowProgress = false,
                UseColors = false
            };

            // Build source dictionary: target module + dependencies
            var sources = new Dictionary<string, string>();
            var virtualFileName = $"{request.ModuleName}.bmmdl";
            sources[virtualFileName] = request.BmmdlSource;

            // Resolve dependencies from erp_modules/ directory
            var depWarnings = ResolveDependencySources(request.BmmdlSource, request.ModuleName, sources);
            warnings.AddRange(depWarnings);

            // Build a filtered feature registry that respects plugin exclusions from config.
            // This ensures DDL is only generated for features whose plugins are active.
            var registry = BuildFilteredFeatureRegistry();
            var pipeline = new CompilerPipeline(options, registry);
            var result = pipeline.CompileFromString(sources);

            // Collect diagnostics
            foreach (var diag in result.Context.Diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                    errors.Add(diag.ToString());
                else if (diag.Severity == DiagnosticSeverity.Warning)
                    warnings.Add(diag.ToString());
            }

            // Build structured diagnostics list
            var diagnostics = result.Context.Diagnostics.Select(d => new CompileDiagnosticDto
            {
                Code = d.Code,
                Message = d.Message,
                Severity = d.Severity.ToString(),
                SourceFile = d.SourceFile != null ? Path.GetFileName(d.SourceFile) : null,
                Line = d.Line,
                Column = d.Column,
                Pass = d.PassName
            }).ToList();

            if (!result.Success || result.Context.Model == null)
            {
                _logger.LogWarning("Compilation failed with {ErrorCount} errors", errors.Count);
                return new CompileResponse
                {
                    Success = false,
                    Errors = errors,
                    Warnings = warnings,
                    Diagnostics = diagnostics,
                    CompilationTime = result.TotalTime
                };
            }

            var model = result.Context.Model;
            _logger.LogInformation("Compilation successful: {EntityCount} entities, {ServiceCount} services",
                model.Entities.Count, model.Services.Count);

            // Filter models for target module:
            // - publishModel: strict filter (only module's own types/enums) for registry publish
            // - schemaModel: includes shared types/enums needed for DDL type resolution
            var publishModel = FilterModelForModule(model, request.ModuleName, includeSharedTypes: false);
            var schemaModel = FilterModelForModule(model, request.ModuleName, includeSharedTypes: true);
            _logger.LogInformation("Filtered to {EntityCount} entities for module {ModuleName}",
                publishModel.Entities.Count, request.ModuleName);

            // Step 2: Publish to registry (non-blocking for InitSchema)
            var publishSucceeded = false;
            if (request.Publish)
            {
                var tenantId = request.TenantId ?? Guid.Empty;
                var connString = _schemaService.GetConnectionString();

                // sourceHash already computed in Step 0 above
                var dbService = new DbPersistenceService(verbose: false);

                try
                {
                    publishSucceeded = await dbService.PublishAsync(
                        publishModel, tenantId, connString,
                        force: request.Force,
                        sourceHash: sourceHash);

                    if (!publishSucceeded)
                    {
                        warnings.Add("Failed to publish to registry");
                    }
                    else
                    {
                        _logger.LogInformation("Published to registry for tenant {TenantId}", tenantId);
                    }
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("VERSION DOWNGRADE") ||
                    ex.Message.Contains("EMPTY VERSION BUMP"))
                {
                    // Already published with same source - not an error, just skip publish
                    warnings.Add($"Publish skipped: {ex.Message}");
                    _logger.LogInformation("Publish skipped for {ModuleName}: {Reason}", request.ModuleName, ex.Message);
                }

                // Route through ModuleInstallationService for installation records + dependency validation
                if (publishSucceeded && _installService != null)
                {
                    try
                    {
                        var installResult = await _installService.InstallModuleAsync(
                            tenantId, publishModel, sourceHash,
                            installedBy: "AdminService");

                        if (!installResult.Success)
                        {
                            // Installation tracking failed — log but don't block schema init
                            warnings.Add($"Installation record: {installResult.Message}");
                            _logger.LogWarning("Installation record creation failed: {Message}", installResult.Message);
                        }
                        else
                        {
                            _logger.LogInformation("Installation record created: {Message}", installResult.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ModuleInstallationService failed, continuing without installation tracking");
                        warnings.Add($"Installation tracking: {ex.Message}");
                    }
                }

                // Version Detection (informational only, never blocks InitSchema)
                if (publishSucceeded && _versioningService != null && _metaModelRepo != null)
                {
                    try
                    {
                        var existingModel = await _metaModelRepo.LoadModelAsync();
                        var moduleId = await GetOrCreateModuleIdAsync(tenantId, request.ModuleName);

                        var versionResult = await _versioningService.ProcessModelVersionAsync(
                            tenantId, moduleId, existingModel, publishModel, createdBy: "AdminService");

                        _logger.LogInformation(
                            "Versioning: {Status}, NewVersion={Version}, Changes={Changes}, Breaking={Breaking}",
                            versionResult.Status, versionResult.NewVersion,
                            versionResult.DetectionResult?.TotalChanges ?? 0,
                            versionResult.HasBreakingChanges);

                        if (versionResult.RequiresApproval && !request.Force)
                        {
                            warnings.Add($"Breaking changes detected! Version {versionResult.NewVersion} requires approval.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Versioning failed, continuing without version tracking");
                        warnings.Add($"Versioning warning: {ex.Message}");
                    }
                }
            }

            // Step 3: Initialize schema - runs independently of publish result
            // If compilation succeeded and user requested InitSchema, always do it
            if (request.InitSchema)
            {
                _logger.LogInformation("Initializing schema for module {ModuleName}", request.ModuleName);
                var connString = _schemaService.GetConnectionString();
                var moduleSchemaName = NamingConvention.GetSchemaName(schemaModel.Module?.Name ?? request.ModuleName);

                if (request.Force)
                {
                    // Force mode: DROP+CREATE (legacy behavior, destroys data)
                    _logger.LogWarning("Force mode: dropping schema {Schema} for clean rebuild", moduleSchemaName);
                    await _schemaService.DropSchemaIfExistsAsync(connString, moduleSchemaName);
                    schemaResult = await _schemaService.InitSchemaFreshAsync(schemaModel, connString);
                }
                else
                {
                    // Migration mode: try ALTER TABLE first, fall back to DROP+CREATE
                    schemaResult = await _schemaService.MigrateSchemaAsync(schemaModel, connString, moduleSchemaName, warnings);
                }
            }

            // Notify Runtime API to reload its cache
            await NotifyRuntimeCacheReloadAsync(warnings);

            return new CompileResponse
            {
                Success = true,
                EntityCount = publishModel.Entities.Count,
                ServiceCount = publishModel.Services.Count,
                TypeCount = publishModel.Types.Count,
                EnumCount = publishModel.Enums.Count,
                CompilationTime = result.TotalTime,
                Errors = errors,
                Warnings = warnings,
                Diagnostics = diagnostics,
                SchemaResult = schemaResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CompileAndInstall failed");
            errors.Add(ex.Message);
            return new CompileResponse
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Preview DDL that would be generated for the given BMMDL source.
    /// Compiles the source and generates DDL without executing it.
    /// </summary>
    public async Task<DdlPreviewResponse> PreviewDdlAsync(DdlPreviewRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Build source dictionary with dependencies
            var sources = new Dictionary<string, string>();
            var virtualFileName = $"{request.ModuleName}.bmmdl";
            sources[virtualFileName] = request.BmmdlSource;

            var depWarnings = ResolveDependencySources(request.BmmdlSource, request.ModuleName, sources);
            warnings.AddRange(depWarnings);

            var options = new CompilationOptions { Verbose = false, ShowProgress = false, UseColors = false };
            var registry = BuildFilteredFeatureRegistry();
            var pipeline = new CompilerPipeline(options, registry);
            var result = pipeline.CompileFromString(sources);

            foreach (var diag in result.Context.Diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                    errors.Add(diag.ToString());
                else if (diag.Severity == DiagnosticSeverity.Warning)
                    warnings.Add(diag.ToString());
            }

            if (!result.Success || result.Context.Model == null)
            {
                return new DdlPreviewResponse
                {
                    Success = false,
                    Errors = errors,
                    Warnings = warnings
                };
            }

            // Filter model for target module and generate DDL
            var schemaModel = FilterModelForModule(result.Context.Model, request.ModuleName, includeSharedTypes: true);
            var schemaManager = new PostgresSchemaManager(new SchemaManagerOptions());
            var ddl = schemaManager.GenerateDdlPreview(schemaModel);

            return new DdlPreviewResponse
            {
                Success = true,
                Ddl = ddl,
                TableCount = schemaModel.Entities.Count,
                Errors = errors,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DDL preview failed for {ModuleName}", request.ModuleName);
            errors.Add(ex.Message);
            return new DdlPreviewResponse
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Notify the Runtime API to reload its MetaModel cache.
    /// Best-effort: logs a warning if the Runtime API is unreachable.
    /// </summary>
    public async Task NotifyRuntimeCacheReloadAsync(List<string> warnings)
    {
        var runtimeBaseUrl = _configuration["RuntimeApi:BaseUrl"];
        if (string.IsNullOrEmpty(runtimeBaseUrl))
        {
            _logger.LogDebug("RuntimeApi:BaseUrl not configured, skipping cache reload notification");
            return;
        }

        var adminKey = _configuration["Admin:ApiKey"];
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{runtimeBaseUrl.TrimEnd('/')}/api/admin/reload-cache");
            if (!string.IsNullOrEmpty(adminKey))
            {
                request.Headers.Add("X-Admin-Key", adminKey);
            }

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Runtime API cache reloaded successfully");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Runtime API cache reload returned {StatusCode}: {Body}", response.StatusCode, body);
                warnings.Add($"Runtime cache reload failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify Runtime API to reload cache (is it running?)");
            warnings.Add("Runtime cache reload skipped: Runtime API unreachable");
        }
    }

    /// <summary>
    /// Create a filtered model containing only the target module's entities.
    /// When includeSharedTypes=true, includes all types/enums/aspects (needed for DDL generation).
    /// When includeSharedTypes=false, only includes the module's own types (for registry publish).
    /// </summary>
    internal static BmModel FilterModelForModule(BmModel fullModel, string moduleName, bool includeSharedTypes = true)
    {
        var filtered = new BmModel
        {
            Module = fullModel.Module,
            Namespace = moduleName
        };

        // Copy AllModules so DDL generator can produce CREATE SCHEMA statements
        foreach (var mod in fullModel.AllModules)
        {
            if (string.Equals(mod.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                filtered.AllModules.Add(mod);
        }

        // Include entities from the target module's namespace
        foreach (var entity in fullModel.Entities)
        {
            if (string.Equals(entity.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(entity.Namespace))
            {
                filtered.Entities.Add(entity);
            }
        }

        if (includeSharedTypes)
        {
            // Include ALL types, enums, aspects (needed for DDL type resolution)
            foreach (var type in fullModel.Types) filtered.Types.Add(type);
            foreach (var en in fullModel.Enums) filtered.Enums.Add(en);
            foreach (var aspect in fullModel.Aspects) filtered.Aspects.Add(aspect);
        }
        else
        {
            // Include types/enums/aspects from the target module's namespace
            // AND root-level (no namespace) items — global shared types from common.bmmdl
            foreach (var type in fullModel.Types)
            {
                if (string.Equals(type.Namespace, moduleName, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(type.Namespace))
                    filtered.Types.Add(type);
            }
            foreach (var en in fullModel.Enums)
            {
                if (string.Equals(en.Namespace, moduleName, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(en.Namespace))
                    filtered.Enums.Add(en);
            }
            foreach (var aspect in fullModel.Aspects)
            {
                if (string.Equals(aspect.Namespace, moduleName, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(aspect.Namespace))
                    filtered.Aspects.Add(aspect);
            }
        }

        // Include services from the target module only
        foreach (var service in fullModel.Services)
        {
            if (string.Equals(service.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(service.Namespace))
            {
                filtered.Services.Add(service);
            }
        }

        // Include rules whose target entity is in the filtered entity set
        var filteredEntityNames = filtered.Entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in fullModel.Rules)
        {
            if (filteredEntityNames.Contains(rule.TargetEntity))
            {
                filtered.Rules.Add(rule);
            }
        }

        // Include access controls whose target entity is in the filtered entity set
        foreach (var acl in fullModel.AccessControls)
        {
            if (filteredEntityNames.Contains(acl.TargetEntity))
            {
                filtered.AccessControls.Add(acl);
            }
        }

        // Include views from the target module's namespace (H45)
        foreach (var view in fullModel.Views)
        {
            if (string.Equals(view.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(view.Namespace))
            {
                filtered.Views.Add(view);
            }
        }

        // Include events from the target module's namespace (H46)
        foreach (var evt in fullModel.Events)
        {
            if (string.Equals(evt.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(evt.Namespace))
            {
                filtered.Events.Add(evt);
            }
        }

        // Include sequences bound to filtered entities (H47)
        // BmSequence has ForEntity but no Namespace, so filter by entity association
        foreach (var seq in fullModel.Sequences)
        {
            if (string.IsNullOrEmpty(seq.ForEntity) || filteredEntityNames.Contains(seq.ForEntity))
            {
                filtered.Sequences.Add(seq);
            }
        }

        // Include migrations from the target module's namespace (H48)
        foreach (var migration in fullModel.Migrations)
        {
            if (string.Equals(migration.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(migration.Namespace))
            {
                filtered.Migrations.Add(migration);
            }
        }

        // Include seed data from the target module's namespace
        foreach (var seed in fullModel.Seeds)
        {
            if (string.Equals(seed.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(seed.Namespace))
            {
                filtered.Seeds.Add(seed);
            }
        }

        // Include extensions targeting filtered entities
        foreach (var ext in fullModel.Extensions)
        {
            if (filteredEntityNames.Contains(ext.TargetName))
            {
                filtered.Extensions.Add(ext);
            }
        }

        // Include modifications targeting filtered entities
        foreach (var mod in fullModel.Modifications)
        {
            if (filteredEntityNames.Contains(mod.TargetName))
            {
                filtered.Modifications.Add(mod);
            }
        }

        // Include annotate directives from the target module's namespace
        foreach (var dir in fullModel.AnnotateDirectives)
        {
            if (string.Equals(dir.Namespace, moduleName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(dir.Namespace))
            {
                filtered.AnnotateDirectives.Add(dir);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Resolve dependency sources for multi-file compilation.
    /// Includes common.bmmdl (shared types/aspects) and any declared dependency modules.
    /// </summary>
    internal List<string> ResolveDependencySources(
        string bmmdlSource, string moduleName, Dictionary<string, string> sources)
    {
        var warnings = new List<string>();

        // Find erp_modules directory
        var erpModulesDir = _discoveryService.FindErpModulesDirectory();
        if (erpModulesDir == null)
        {
            _logger.LogWarning("Could not find erp_modules directory - compiling without dependencies");
            warnings.Add("Could not find erp_modules directory; dependency types may be unresolved");
            return warnings;
        }

        _logger.LogInformation("Found erp_modules at: {Path}", erpModulesDir);

        // Always include common.bmmdl (shared types, aspects)
        var commonPath = Path.Combine(erpModulesDir, "common.bmmdl");
        if (File.Exists(commonPath))
        {
            try
            {
                sources["common.bmmdl"] = File.ReadAllText(commonPath);
                _logger.LogInformation("Included common.bmmdl (shared types/aspects)");
            }
            catch (Exception ex) when (ex is FileNotFoundException or IOException)
            {
                _logger.LogWarning(ex, "Failed to read common.bmmdl");
                warnings.Add($"Failed to read common.bmmdl: {ex.Message}");
            }
        }
        else
        {
            warnings.Add("common.bmmdl not found in erp_modules/");
        }

        // Parse dependency module names from source (e.g., "depends on Core version '>=1.0.0';")
        var dependencyNames = ParseDependencyNames(bmmdlSource);
        if (dependencyNames.Count == 0)
        {
            _logger.LogInformation("No module dependencies declared");
            return warnings;
        }

        _logger.LogInformation("Module declares dependencies: {Deps}", string.Join(", ", dependencyNames));

        // Build lookup: module name → source file path
        var moduleFiles = _discoveryService.DiscoverModuleFiles(erpModulesDir);

        // Use a queue for breadth-first transitive dependency resolution
        // with a visited set to prevent infinite loops from circular dependencies
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            moduleName // Don't include self
        };
        var queue = new Queue<string>(dependencyNames);

        while (queue.Count > 0)
        {
            var depName = queue.Dequeue();

            if (visited.Contains(depName))
                continue; // Already processed or is self
            visited.Add(depName);

            if (moduleFiles.TryGetValue(depName, out var depPath))
            {
                var depFileName = $"{depName}.bmmdl";
                if (!sources.ContainsKey(depFileName))
                {
                    try
                    {
                        sources[depFileName] = File.ReadAllText(depPath);
                        _logger.LogInformation("Included dependency module: {DepName} from {Path}", depName, depPath);

                        // Enqueue transitive dependencies for processing
                        var transitiveDeps = ParseDependencyNames(sources[depFileName]);
                        foreach (var transDep in transitiveDeps)
                        {
                            if (!visited.Contains(transDep))
                            {
                                queue.Enqueue(transDep);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or IOException)
                    {
                        _logger.LogWarning(ex, "Failed to read dependency module {DepName} from {Path}", depName, depPath);
                        warnings.Add($"Failed to read dependency module '{depName}': {ex.Message}");
                    }
                }
            }
            else
            {
                warnings.Add($"Dependency module '{depName}' not found in erp_modules/");
                _logger.LogWarning("Dependency module {DepName} not found on disk", depName);
            }
        }

        return warnings;
    }

    /// <summary>
    /// Parse "depends on ModuleName" declarations from BMMDL source.
    /// </summary>
    internal static List<string> ParseDependencyNames(string bmmdlSource)
    {
        var names = new List<string>();
        var matches = Regex.Matches(
            bmmdlSource,
            @"depends\s+on\s+(\w+)",
            RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    /// <summary>
    /// Get or create module ID for versioning.
    /// </summary>
    private async Task<Guid> GetOrCreateModuleIdAsync(Guid tenantId, string moduleName)
    {
        var module = await _registryDb.Modules
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == moduleName);

        if (module != null)
            return module.Id;

        // Create new module record
        var newModule = new Registry.Entities.Module
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = moduleName,
            Version = "1.0.0",
            Status = Registry.Entities.ModuleStatus.Published,
            CreatedAt = DateTime.UtcNow
        };

        _registryDb.Modules.Add(newModule);
        await _registryDb.SaveChangesAsync();
        return newModule.Id;
    }

    /// <summary>
    /// Build a PlatformFeatureRegistry filtered by the Plugins:Bootstrap:Exclude config.
    /// Features listed in the exclusion list (and their dependents) are removed,
    /// so the compiler won't generate DDL for plugins that aren't active at runtime.
    /// </summary>
    private PlatformFeatureRegistry BuildFilteredFeatureRegistry()
    {
        var allFeatures = BMMDL.Runtime.Plugins.Loading.FeatureDiscovery.DiscoverBuiltInFeatures();

        var excludeList = _configuration.GetSection("Plugins:Bootstrap:Exclude")
            .Get<List<string>>() ?? [];

        if (excludeList.Count == 0)
            return new PlatformFeatureRegistry(allFeatures);

        var excluded = new HashSet<string>(excludeList, StringComparer.OrdinalIgnoreCase);

        // Cascade: if a dependency is excluded, exclude the dependent too
        foreach (var feature in allFeatures)
        {
            if (excluded.Contains(feature.Name)) continue;
            foreach (var dep in feature.DependsOn)
            {
                if (excluded.Contains(dep))
                {
                    excluded.Add(feature.Name);
                    break;
                }
            }
        }

        var filtered = allFeatures.Where(f => !excluded.Contains(f.Name)).ToList();
        _logger.LogDebug("Feature registry: {Total} discovered, {Excluded} excluded, {Active} active",
            allFeatures.Count, excluded.Count, filtered.Count);

        return new PlatformFeatureRegistry(filtered);
    }
}
