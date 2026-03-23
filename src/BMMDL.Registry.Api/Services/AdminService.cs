using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Api.Models;
using BMMDL.Registry.Data;
using BMMDL.Registry.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Administrative operations facade.
/// Coordinates ModuleCompilationService, SchemaManagementService, and ModuleDiscoveryService.
/// </summary>
public class AdminService : IAdminService
{
    private readonly RegistryDbContext _registryDb;
    private readonly ILogger<AdminService> _logger;
    private readonly IModuleCompilationService _compilationService;
    private readonly ISchemaManagementService _schemaService;
    private readonly ModuleDiscoveryService _discoveryService;
    private readonly IModuleInstallationService? _installService;

    public AdminService(
        RegistryDbContext registryDb,
        ILogger<AdminService> logger,
        IModuleCompilationService compilationService,
        ISchemaManagementService schemaService,
        ModuleDiscoveryService discoveryService,
        IModuleInstallationService? installService = null)
    {
        _registryDb = registryDb;
        _logger = logger;
        _compilationService = compilationService;
        _schemaService = schemaService;
        _discoveryService = discoveryService;
        _installService = installService;
    }

    /// <summary>
    /// Clear database - drop business schemas and/or truncate registry tables.
    /// </summary>
    public async Task<ClearDatabaseResponse> ClearDatabaseAsync(ClearDatabaseRequest request)
    {
        var messages = new List<string>();
        int schemasDropped = 0;
        int tablesCleared = 0;

        try
        {
            var connString = _schemaService.GetConnectionString();

            // Drop business schemas
            if (request.DropSchemas)
            {
                await using var conn = new NpgsqlConnection(connString);
                await conn.OpenAsync();

                List<string> schemas;

                if (request.Schemas?.Count > 0)
                {
                    // Validate schemas against forbidden list
                    var forbiddenSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "public", "information_schema", "pg_catalog", "pg_toast" };
                    foreach (var schema in request.Schemas)
                    {
                        if (forbiddenSchemas.Contains(schema) || schema.StartsWith("pg_"))
                        {
                            return new ClearDatabaseResponse
                            {
                                Success = false,
                                Error = $"Cannot drop system schema '{schema}'"
                            };
                        }
                    }

                    // Use specified schemas
                    schemas = request.Schemas;
                }
                else
                {
                    // Query ALL schemas from database (except PostgreSQL system schemas only)
                    schemas = new List<string>();

                    var querySchemaSql = @"
                        SELECT schema_name
                        FROM information_schema.schemata
                        WHERE schema_name NOT IN ('public', 'information_schema')
                          AND schema_name NOT LIKE 'pg_%'
                        ORDER BY schema_name;
                    ";

                    await using var queryCmd = new NpgsqlCommand(querySchemaSql, conn);
                    await using var reader = await queryCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0))
                            schemas.Add(reader.GetString(0));
                    }

                    _logger.LogInformation("Found {SchemaCount} schemas to drop: {Schemas}",
                        schemas.Count, string.Join(", ", schemas));
                    messages.Add($"Found {schemas.Count} schemas to drop");
                }

                foreach (var schema in schemas)
                {
                    try
                    {
                        // Use quoted identifier to handle special characters (escape internal double quotes)
                        var safeSchema = NamingConvention.QuoteIdentifier(schema);
                        await using var cmd = new NpgsqlCommand(
                            $"DROP SCHEMA IF EXISTS {safeSchema} CASCADE;", conn);
                        await cmd.ExecuteNonQueryAsync();
                        schemasDropped++;
                        messages.Add($"Dropped schema: {schema}");
                        _logger.LogInformation("Dropped schema: {Schema}", schema);
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"Failed to drop {schema}: {ex.Message}");
                        _logger.LogWarning(ex, "Failed to drop schema {Schema}", schema);
                    }
                }
            }

            // Re-create registry schema after dropping all schemas
            if (request.ClearRegistry)
            {
                try
                {
                    // Registry was dropped above, re-create it now
                    var schemaWasCreated = await _schemaService.EnsureRegistrySchemaExistsAsync(messages);

                    if (schemaWasCreated)
                    {
                        tablesCleared = 7;  // Fresh empty tables
                        messages.Add("Registry schema re-created with empty tables");
                        _logger.LogInformation("Registry schema re-created with empty tables");
                    }
                }
                catch (Exception ex)
                {
                    messages.Add($"Failed to re-create registry: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to re-create registry schema");
                }
            }

            return new ClearDatabaseResponse
            {
                Success = true,
                SchemasDropped = schemasDropped,
                TablesCleared = tablesCleared,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearDatabase failed");
            return new ClearDatabaseResponse
            {
                Success = false,
                Messages = messages,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Compile BMMDL source and optionally publish to registry and initialize schema.
    /// Delegates to ModuleCompilationService.
    /// </summary>
    public async Task<CompileResponse> CompileAndInstallAsync(CompileRequest request)
    {
        return await _compilationService.CompileAndInstallAsync(request);
    }

    /// <summary>
    /// Bootstrap the platform: compile the Platform and Core modules, publish to registry, and create tables.
    /// This is the API equivalent of "bmmdlc bootstrap --init-platform".
    /// </summary>
    public async Task<BootstrapResponse> BootstrapPlatformAsync()
    {
        var messages = new List<string>();
        var totalEntities = 0;

        try
        {
            // Ensure system tenant exists
            var connString = _schemaService.GetConnectionString();
            await EnsureSystemTenantAsync(connString, messages);

            // Bootstrap modules in order
            var modules = new[]
            {
                ("Platform", "erp_modules/00_platform/module.bmmdl"),
                ("Core", "erp_modules/01_core/module.bmmdl")
            };

            foreach (var (moduleName, relativePath) in modules)
            {
                var modulePath = _discoveryService.FindModulePath(relativePath);
                if (modulePath == null)
                {
                    return new BootstrapResponse
                    {
                        Success = false,
                        Messages = messages,
                        Error = $"Module file not found: {relativePath}",
                        EntityCount = totalEntities
                    };
                }

                messages.Add($"Found {moduleName} module: {modulePath}");
                var bmmdlSource = await File.ReadAllTextAsync(modulePath);

                var compileResult = await CompileAndInstallAsync(new CompileRequest
                {
                    BmmdlSource = bmmdlSource,
                    ModuleName = moduleName,
                    TenantId = Guid.Empty,
                    Publish = true,
                    InitSchema = true,
                    Force = true
                });

                if (!compileResult.Success)
                {
                    return new BootstrapResponse
                    {
                        Success = false,
                        Messages = messages,
                        Error = $"{moduleName}: {string.Join("; ", compileResult.Errors)}",
                        EntityCount = totalEntities
                    };
                }

                totalEntities += compileResult.EntityCount;
                messages.Add($"{moduleName}: {compileResult.EntityCount} entities installed");
                if (compileResult.SchemaResult != null)
                    messages.Add($"{moduleName} schema: {compileResult.SchemaResult}");
            }

            return new BootstrapResponse
            {
                Success = true,
                Messages = messages,
                EntityCount = totalEntities
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootstrap platform failed");
            return new BootstrapResponse
            {
                Success = false,
                Messages = messages,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Uninstall a module by name. Checks for dependent modules, drops schema tables,
    /// and removes module metadata from the registry.
    /// </summary>
    public async Task<UninstallModuleResponse> UninstallModuleByNameAsync(string moduleName, Guid? tenantId = null)
    {
        var effectiveTenantId = tenantId ?? Guid.Empty;

        try
        {
            // 1. Find the module in the registry
            var module = await _registryDb.Modules
                .Include(m => m.Dependencies)
                .FirstOrDefaultAsync(m =>
                    m.TenantId == effectiveTenantId &&
                    m.Name == moduleName);

            if (module == null)
            {
                return new UninstallModuleResponse
                {
                    Success = false,
                    Error = $"Module '{moduleName}' not found in registry."
                };
            }

            // 2. Check that no other installed modules depend on this one
            var dependentModules = await _registryDb.Set<Registry.Entities.ModuleDependency>()
                .Where(d => d.DependsOnName == moduleName && d.Module.TenantId == effectiveTenantId)
                .Select(d => d.Module.Name)
                .Distinct()
                .ToListAsync();

            // Exclude self-references
            dependentModules = dependentModules
                .Where(n => !string.Equals(n, moduleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (dependentModules.Count > 0)
            {
                return new UninstallModuleResponse
                {
                    Success = false,
                    Error = $"Cannot uninstall '{moduleName}': other modules depend on it.",
                    DependentModules = dependentModules
                };
            }

            var messages = new List<string>();

            // 3. Drop the module's schema tables
            var connString = _schemaService.GetConnectionString();
            var schemaName = NamingConvention.GetSchemaName(moduleName);

            try
            {
                await using var conn = new NpgsqlConnection(connString);
                await conn.OpenAsync();

                var checkSql = "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)";
                await using var checkCmd = new NpgsqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@schema", schemaName);
                var schemaExists = (bool)(await checkCmd.ExecuteScalarAsync() ?? false);

                if (schemaExists)
                {
                    var safeSchemaName = NamingConvention.QuoteIdentifier(schemaName);
                    await using var dropCmd = new NpgsqlCommand(
                        $"DROP SCHEMA IF EXISTS {safeSchemaName} CASCADE;", conn);
                    await dropCmd.ExecuteNonQueryAsync();
                    messages.Add($"Dropped schema: {schemaName}");
                    _logger.LogInformation("Dropped schema {Schema} for module {Module}", schemaName, moduleName);
                }
                else
                {
                    messages.Add($"Schema '{schemaName}' did not exist (skipped)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop schema {Schema} for module {Module}", schemaName, moduleName);
                messages.Add($"Warning: failed to drop schema '{schemaName}': {ex.Message}");
            }

            // 4. Remove the module's installation record if it exists
            if (_installService != null)
            {
                try
                {
                    var uninstallResult = await _installService.UninstallModuleAsync(
                        effectiveTenantId, module.Id, "AdminService");
                    if (uninstallResult.Success)
                    {
                        messages.Add("Installation record removed");
                    }
                    else
                    {
                        messages.Add($"Installation record removal: {uninstallResult.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove installation record for {Module}", moduleName);
                    messages.Add($"Warning: installation record removal failed: {ex.Message}");
                }
            }

            // 5. Remove module metadata from registry DB (entities, services, types, etc.)
            // These are cascade-deleted via FK when the Module record is removed
            _registryDb.Modules.Remove(module);
            await _registryDb.SaveChangesAsync();
            messages.Add($"Module '{moduleName}' removed from registry");
            _logger.LogInformation("Module {Module} uninstalled and removed from registry", moduleName);

            // 6. Notify Runtime API to reload cache
            var warnings = new List<string>();
            await _compilationService.NotifyRuntimeCacheReloadAsync(warnings);
            messages.AddRange(warnings);

            return new UninstallModuleResponse
            {
                Success = true,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UninstallModule failed for {Module}", moduleName);
            return new UninstallModuleResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get all published modules with their schema initialization status.
    /// </summary>
    public async Task<List<ModuleStatusDto>> GetModulesWithSchemaStatusAsync()
    {
        // Get all modules from registry (including dependencies)
        var modules = await _registryDb.Modules
            .Include(m => m.Dependencies)
            .OrderBy(m => m.Name)
            .ToListAsync();

        // Get existing schemas from database
        var connString = _schemaService.GetConnectionString();
        var existingSchemas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // Get schemas and their table counts
        var schemaSql = """
            SELECT t.table_schema, COUNT(*) as table_count
            FROM information_schema.tables t
            WHERE t.table_schema NOT IN ('public', 'information_schema', 'registry')
              AND t.table_schema NOT LIKE 'pg_%'
              AND t.table_type = 'BASE TABLE'
            GROUP BY t.table_schema
            """;
        await using var cmd = new NpgsqlCommand(schemaSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
                existingSchemas[reader.GetString(0)] = reader.GetInt32(1);
        }

        // Get entity counts per module
        var entityCountList = await _registryDb.Entities
            .Where(e => e.ModuleId != null)
            .GroupBy(e => e.ModuleId!.Value)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToListAsync();
        var entityCounts = entityCountList.ToDictionary(x => x.ModuleId, x => x.Count);

        var serviceCountList = await _registryDb.Services
            .Where(s => s.ModuleId != null)
            .GroupBy(s => s.ModuleId!.Value)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToListAsync();
        var serviceCounts = serviceCountList.ToDictionary(x => x.ModuleId, x => x.Count);

        var result = new List<ModuleStatusDto>();
        foreach (var m in modules)
        {
            // Use the same naming convention as DDL generator (snake_case)
            var schemaName = NamingConvention.GetSchemaName(m.Name);
            var hasSchema = existingSchemas.TryGetValue(schemaName, out var tableCount);

            result.Add(new ModuleStatusDto
            {
                Id = m.Id,
                Name = m.Name,
                Version = m.Version ?? "1.0.0",
                Author = m.Author,
                EntityCount = entityCounts.GetValueOrDefault(m.Id, 0),
                ServiceCount = serviceCounts.GetValueOrDefault(m.Id, 0),
                CreatedAt = m.CreatedAt,
                PublishedAt = m.PublishedAt,
                SchemaInitialized = hasSchema,
                TableCount = hasSchema ? tableCount : 0,
                SchemaName = schemaName,
                Dependencies = m.Dependencies?.Select(d => new ModuleDependencyDto
                {
                    DependsOnName = d.DependsOnName,
                    VersionRange = d.VersionRange,
                    ResolvedId = d.ResolvedId,
                    IsResolved = d.ResolvedId != null
                }).ToList() ?? new()
            });
        }

        return result;
    }

    /// <summary>
    /// Get a dependency graph of all installed modules.
    /// Returns nodes (modules) and edges (dependency relationships).
    /// </summary>
    public async Task<DependencyGraphResponse> GetDependencyGraphAsync()
    {
        var modules = await _registryDb.Modules
            .Include(m => m.Dependencies)
            .OrderBy(m => m.Name)
            .ToListAsync();

        var nodes = modules.Select(m => new DependencyGraphNode
        {
            Id = m.Id,
            Name = m.Name,
            Version = m.Version ?? "1.0.0",
            Status = m.Status.ToString()
        }).ToList();

        var edges = new List<DependencyGraphEdge>();
        foreach (var module in modules)
        {
            if (module.Dependencies == null) continue;
            foreach (var dep in module.Dependencies)
            {
                edges.Add(new DependencyGraphEdge
                {
                    From = module.Name,
                    To = dep.DependsOnName,
                    VersionRange = dep.VersionRange ?? ""
                });
            }
        }

        return new DependencyGraphResponse
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    /// <summary>
    /// Preview DDL that would be generated for the given BMMDL source.
    /// Delegates to ModuleCompilationService.
    /// </summary>
    public async Task<DdlPreviewResponse> PreviewDdlAsync(DdlPreviewRequest request)
    {
        return await _compilationService.PreviewDdlAsync(request);
    }

    private async Task EnsureSystemTenantAsync(string connString, List<string> messages)
    {
        var systemTenantId = Guid.Empty;
        var existing = await _registryDb.Tenants.FindAsync(systemTenantId);
        if (existing == null)
        {
            _registryDb.Tenants.Add(new Registry.Entities.Tenant
            {
                Id = systemTenantId,
                Name = "System Tenant",
                CreatedAt = DateTime.UtcNow
            });
            await _registryDb.SaveChangesAsync();
            messages.Add("Created System Tenant");
        }
        else
        {
            messages.Add("System Tenant already exists");
        }
    }
}
