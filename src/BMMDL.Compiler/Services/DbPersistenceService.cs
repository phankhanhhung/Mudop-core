using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BMMDL.MetaModel;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;
using BMMDL.Registry.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Services;

/// <summary>
/// Service to persist compiled BMMDL model to the database.
/// Uses RegistryDbContext for direct DB access from CLI.
/// </summary>
public class DbPersistenceService
{
    private readonly bool _verbose;
    private readonly ICompilerOutput _output;
    private readonly ILogger _logger;
    
    public DbPersistenceService(bool verbose = false, ICompilerOutput? output = null)
    {
        _verbose = verbose;
        _output = output ?? new ConsoleCompilerOutput();
        _logger = CompilerLoggerFactory.CreateLogger("Persistence");
    }
    
    /// <summary>
    /// Build a connection string from environment variables or explicit value.
    /// </summary>
    public static string BuildConnectionString(string? connectionString = null)
    {
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;
        
        return $"Host={Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost"};" +
               $"Port={Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};" +
               $"Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bmmdl"};" +
               $"Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres"};" +
               $"Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres"}";
    }
    
    /// <summary>
    /// Publish a compiled model to the database.
    /// Includes consistency check against existing model.
    /// </summary>
    /// <param name="model">Compiled model to publish</param>
    /// <param name="tenantId">Target tenant ID</param>
    /// <param name="connectionString">Optional connection string</param>
    /// <param name="force">Skip consistency checks if true</param>
    /// <param name="sourceHash">Optional source content hash for empty version bump detection</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<bool> PublishAsync(
        BmModel model, 
        Guid tenantId,
        string? connectionString = null, 
        bool force = false,
        string? sourceHash = null,
        CancellationToken ct = default)
    {
        var connStr = BuildConnectionString(connectionString);
        
        if (_verbose)
        {
            _output.WriteLine("📦 Connecting to database...");
            _logger.LogDebug("Connecting to database");
        }
        
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseNpgsql(connStr)
            .Options;
        
        await using var db = new RegistryDbContext(options);
        
        // Ensure database exists and migrations applied
        if (_verbose)
        {
            _output.WriteLine("  Ensuring database schema...");
            _logger.LogDebug("Ensuring database schema");
        }
        
        await db.Database.EnsureCreatedAsync(ct);
        
        // Ensure tenant exists
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct);
        if (tenant == null)
        {
            if (_verbose)
            {
                _output.WriteLine($"  Creating tenant {tenantId}...");
                _logger.LogDebug("Creating tenant {TenantId}", tenantId);
            }
            tenant = new BMMDL.Registry.Entities.Tenant
            {
                Id = tenantId,
                Name = $"CLI-{tenantId:N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
        }
        
        var repo = new EfCoreMetaModelRepository(db, tenantId);
        
        // Step 1: Load existing model for consistency check
        _output.WriteLine("🔍 Running consistency check...");
        _logger.LogInformation("Running consistency check for tenant {TenantId}", tenantId);
        
        BmModel? existingModel = null;
        try
        {
            existingModel = await repo.LoadModelAsync(ct);
            if (_verbose)
            {
                _output.WriteLine($"  Loaded existing model: {existingModel.Entities.Count} entities");
                _logger.LogDebug("Loaded existing model with {EntityCount} entities", existingModel.Entities.Count);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                _output.WriteLine($"  No existing model found: {ex.Message}");
                _logger.LogDebug(ex, "No existing model found");
            }
        }
        
        // Step 2: Run consistency check (skip if force=true)
        if (!force)
        {
            var checker = new ModuleConsistencyChecker();
            var moduleNamespace = model.Module?.Name ?? model.Namespace;
            var checkResult = checker.Check(model, existingModel, moduleNamespace);
            
            // Show warnings
            foreach (var warning in checkResult.Warnings)
            {
                _output.WriteWarning($"[{warning.Code}] {warning.Message}");
                _logger.LogWarning("[{Code}] {Message}", warning.Code, warning.Message);
            }
            
            // Fail on errors
            if (!checkResult.IsConsistent)
            {
                _output.WriteError("Consistency check failed:");
                foreach (var error in checkResult.Errors)
                {
                    _output.WriteLine($"  ❌ [{error.Code}] {error.Message}");
                    _logger.LogError("[{Code}] {Message}", error.Code, error.Message);
                }
                return false;
            }
            
            _output.WriteSuccess("Consistency check passed");
            _logger.LogInformation("Consistency check passed");
        }
        else
        {
            _output.WriteWarning("Force mode: Skipping consistency check");
            _logger.LogWarning("Force mode enabled - skipping consistency check");
        }
        
        // Step 2.5: Save module metadata and capture module ID for artifact tagging
        Guid? savedModuleId = null;
        var modulesToSave = model.AllModules.Count > 0
            ? model.AllModules
            : (model.Module != null ? new List<BmModuleDeclaration> { model.Module } : new List<BmModuleDeclaration>());

        if (modulesToSave.Count > 0)
        {
            _output.WriteLine($"📦 Saving {modulesToSave.Count} module(s)...");
            _logger.LogInformation("Saving {ModuleCount} modules", modulesToSave.Count);
            foreach (var moduleDecl in modulesToSave)
            {
                var savedModule = await SaveModuleAsync(db, moduleDecl, model, tenantId, null, sourceHash, ct);
                // Use the primary module's ID (the one matching model.Module)
                if (savedModule != null && model.Module != null && moduleDecl.Name == model.Module.Name)
                {
                    savedModuleId = savedModule.Id;
                }
            }
        }

        // Step 3: Save all model artifacts (with module ID for entity-module association)
        _output.WriteLine("📦 Publishing to database...");
        _logger.LogInformation("Publishing model to database");

        var artifactRepo = savedModuleId.HasValue
            ? new EfCoreMetaModelRepository(db, tenantId, savedModuleId.Value)
            : repo;
        await SaveArtifactsAsync(artifactRepo, model, ct);

        _output.WriteSuccess("Published to database:");
        _output.WriteLine($"     Module:   {model.Module?.Name ?? "(none)"} {model.Module?.Version ?? ""}");
        _output.WriteLine($"     Entities: {model.Entities.Count}");
        _output.WriteLine($"     Services: {model.Services.Count}");
        _output.WriteLine($"     Types:    {model.Types.Count}");
        _output.WriteLine($"     Enums:    {model.Enums.Count}");
        _output.WriteLine($"     Aspects:  {model.Aspects.Count}");
        _output.WriteLine($"     Views:    {model.Views.Count}");
        _output.WriteLine($"     Rules:    {model.Rules.Count}");
        _output.WriteLine($"     Sequences:{model.Sequences.Count}");
        _output.WriteLine($"     Events:   {model.Events.Count}");
        _output.WriteLine($"     ACLs:     {model.AccessControls.Count}");
        _output.WriteLine($"     Migrations:{model.Migrations.Count}");

        _logger.LogInformation("Published {EntityCount} entities, {ServiceCount} services, {TypeCount} types",
            model.Entities.Count, model.Services.Count, model.Types.Count);
        
        return true;
    }
    
    /// <summary>
    /// Save all model artifacts to the repository.
    /// </summary>
    private async Task SaveArtifactsAsync(EfCoreMetaModelRepository repo, BmModel model, CancellationToken ct)
    {
        if (model.Entities.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Entities.Count} entities...");
            foreach (var entity in model.Entities)
                await repo.SaveEntityAsync(entity, ct);
        }
        
        if (model.Services.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Services.Count} services...");
            foreach (var service in model.Services)
                await repo.SaveServiceAsync(service, ct);
        }
        
        if (model.Types.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Types.Count} types...");
            foreach (var type in model.Types)
                await repo.SaveTypeAsync(type, ct);
        }
        
        if (model.Enums.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Enums.Count} enums...");
            foreach (var enumDef in model.Enums)
                await repo.SaveEnumAsync(enumDef, ct);
        }
        
        if (model.Aspects.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Aspects.Count} aspects...");
            foreach (var aspect in model.Aspects)
                await repo.SaveAspectAsync(aspect, ct);
        }
        
        if (model.Views.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Views.Count} views...");
            foreach (var view in model.Views)
                await repo.SaveViewAsync(view, ct);
        }
        
        if (model.Rules.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Rules.Count} rules...");
            foreach (var rule in model.Rules)
                await repo.SaveRuleAsync(rule, ct);
        }
        
        if (model.Sequences.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Sequences.Count} sequences...");
            foreach (var seq in model.Sequences)
                await repo.SaveSequenceAsync(seq, ct);
        }
        
        if (model.Events.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Events.Count} events...");
            foreach (var evt in model.Events)
                await repo.SaveEventAsync(evt, ct);
        }
        
        if (model.AccessControls.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.AccessControls.Count} access controls...");
            foreach (var acl in model.AccessControls)
                await repo.SaveAccessControlAsync(acl, ct);
        }

        if (model.Migrations.Count > 0)
        {
            if (_verbose) _output.WriteLine($"  Saving {model.Migrations.Count} migration defs...");
            foreach (var migration in model.Migrations)
                await repo.SaveMigrationDefAsync(migration, ct);
        }
    }
    
    /// <summary>
    /// Save module metadata to the database.
    /// Creates Module entity, resolves dependencies, and creates installation record.
    /// </summary>
    private async Task<Module?> SaveModuleAsync(
        RegistryDbContext db,
        BmModuleDeclaration decl,
        BmModel model,
        Guid tenantId,
        string? installedBy,
        string? sourceHash,
        CancellationToken ct)
    {
        if (_verbose)
        {
            _output.WriteLine($"  📦 Saving module metadata: {decl.Name} v{decl.Version}");
            _logger.LogDebug("Saving module {ModuleName} v{Version}", decl.Name, decl.Version);
        }
        
        // 1. Check if exact same name+version already exists
        var existing = await db.Modules
            .Include(m => m.Dependencies)
            .FirstOrDefaultAsync(m => 
                m.TenantId == tenantId && 
                m.Name == decl.Name && 
                m.Version == decl.Version, ct);
        
        if (existing != null)
        {
            // Already published - skip
            _output.WriteWarning($"Module {decl.Name} v{decl.Version} already exists, skipping...");
            _logger.LogWarning("Module {ModuleName} v{Version} already exists", decl.Name, decl.Version);
            return existing;
        }
        
        // 2. VERSION DOWNGRADE PROTECTION - check against latest version
        var latestModule = await db.Modules
            .Where(m => m.TenantId == tenantId && m.Name == decl.Name)
            .OrderByDescending(m => m.VersionMajor)
            .ThenByDescending(m => m.VersionMinor)
            .ThenByDescending(m => m.VersionPatch)
            .FirstOrDefaultAsync(ct);
        
        if (latestModule != null)
        {
            var incomingVersion = VersionParser.Parse(decl.Version);
            var existingVersion = VersionParser.Parse(latestModule.Version);
            
            if (incomingVersion <= existingVersion)
            {
                var errorMsg = $"VERSION DOWNGRADE NOT ALLOWED: Module '{decl.Name}' version '{decl.Version}' is OLDER OR EQUAL to existing version '{latestModule.Version}'. Use a version GREATER than '{latestModule.Version}'.";
                _output.WriteError(errorMsg);
                _logger.LogWarning("Version downgrade blocked: {ModuleName} {NewVersion} <= {ExistingVersion}",
                    decl.Name, decl.Version, latestModule.Version);
                throw new InvalidOperationException(errorMsg);
            }
            
            // 3. EMPTY VERSION BUMP DETECTION - compare content hash
            if (sourceHash != null)
            {
                var latestInstall = await db.ModuleInstallations
                    .Where(i => i.ModuleId == latestModule.Id)
                    .FirstOrDefaultAsync(ct);
                
                if (latestInstall?.SourceHash != null && latestInstall.SourceHash == sourceHash)
                {
                    var errorMsg = $"EMPTY VERSION BUMP NOT ALLOWED: Module '{decl.Name}' version '{decl.Version}' has IDENTICAL content to existing version '{latestModule.Version}'. You cannot bump version without making actual changes.";
                    _output.WriteError(errorMsg);
                    _logger.LogWarning("Empty version bump blocked: {ModuleName} {NewVersion} has same hash as {ExistingVersion}",
                        decl.Name, decl.Version, latestModule.Version);
                    throw new InvalidOperationException(errorMsg);
                }
            }
            
            _output.WriteLine($"  ✅ Version upgrade validated: {decl.Name} {latestModule.Version} → {decl.Version}");
        }
        
        // 2. Create new module entity
        var module = new Module
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = decl.Name,
            Version = decl.Version,
            Author = decl.Author,
            Status = ModuleStatus.Published,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        ParseVersion(module);
        
        // 3. Map dependencies
        foreach (var dep in decl.Dependencies)
        {
            module.Dependencies.Add(new ModuleDependency
            {
                Id = Guid.NewGuid(),
                DependsOnName = dep.ModuleName,
                VersionRange = dep.VersionRange
            });
        }
        
        // 4. Resolve dependencies against existing modules
        await ResolveDependenciesAsync(db, module, tenantId, ct);
        
        // 5. Save module
        db.Modules.Add(module);
        await db.SaveChangesAsync(ct);
        
        // 6. Create installation record
        var nextOrder = await db.ModuleInstallations
            .Where(i => i.TenantId == tenantId && i.Status == InstallationStatus.Installed)
            .MaxAsync(i => (int?)i.InstallOrder, ct) ?? 0;
        
        // Count artifacts that belong to this module (by namespace)
        // A module publishes specific namespaces, so we count artifacts in those namespaces
        var moduleNamespaces = decl.Publishes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (moduleNamespaces.Count == 0)
        {
            // If no explicit publishes, use module name as namespace
            moduleNamespaces.Add(decl.Name);
        }
        
        var entityCount = model.Entities.Count(e => moduleNamespaces.Contains(e.Namespace ?? ""));
        var typeCount = model.Types.Count(t => moduleNamespaces.Contains(t.Namespace ?? ""));
        var enumCount = model.Enums.Count(e => moduleNamespaces.Contains(e.Namespace ?? ""));
        var serviceCount = model.Services.Count(s => moduleNamespaces.Contains(s.Namespace ?? ""));
        // BmRule doesn't have Namespace, count by matching TargetEntity's namespace prefix
        var ruleCount = model.Rules.Count(r => 
        {
            var targetNs = r.TargetEntity?.Split('.').FirstOrDefault() ?? "";
            return moduleNamespaces.Contains(targetNs);
        });
        
        var installation = new ModuleInstallation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ModuleId = module.Id,
            InstallOrder = nextOrder + 1,
            InstalledAt = DateTime.UtcNow,
            InstalledBy = installedBy ?? Environment.UserName,
            Status = InstallationStatus.Installed,
            EntityCount = entityCount,
            TypeCount = typeCount,
            EnumCount = enumCount,
            ServiceCount = serviceCount,
            RuleCount = ruleCount,
            SourceHash = sourceHash
        };
        db.ModuleInstallations.Add(installation);
        await db.SaveChangesAsync(ct);
        
        if (_verbose)
        {
            _output.WriteSuccess($"Module saved: {module.Name} v{module.Version} (install order #{nextOrder + 1})");
            _logger.LogInformation("Saved module {ModuleName} v{Version} order #{Order}", 
                module.Name, module.Version, nextOrder + 1);
        }
        
        return module;
    }
    
    /// <summary>
    /// Parse semantic version string into component parts.
    /// </summary>
    private static void ParseVersion(Module module)
    {
        var parts = module.Version.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            module.VersionMajor = major;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            module.VersionMinor = minor;
        if (parts.Length >= 3 && int.TryParse(parts[2].Split('-')[0], out var patch))
            module.VersionPatch = patch;
    }
    
    /// <summary>
    /// Resolve module dependencies against already-published modules.
    /// </summary>
    private async Task ResolveDependenciesAsync(
        RegistryDbContext db,
        Module module,
        Guid tenantId,
        CancellationToken ct)
    {
        foreach (var dep in module.Dependencies)
        {
            // Find published module matching the dependency name
            var resolved = await db.Modules
                .Where(m => m.TenantId == tenantId && 
                            m.Name == dep.DependsOnName && 
                            m.Status == ModuleStatus.Published)
                .OrderByDescending(m => m.VersionMajor)
                .ThenByDescending(m => m.VersionMinor)
                .ThenByDescending(m => m.VersionPatch)
                .FirstOrDefaultAsync(ct);
            
            if (resolved != null)
            {
                dep.ResolvedId = resolved.Id;
                dep.IsCompatible = true;
                if (_verbose)
                {
                    _output.WriteSuccess($"  Dependency {dep.DependsOnName} resolved to v{resolved.Version}");
                    _logger.LogDebug("Resolved dependency {DepName} to v{Version}", dep.DependsOnName, resolved.Version);
                }
            }
            else
            {
                dep.IsCompatible = false;
                _output.WriteWarning($"  Dependency {dep.DependsOnName} not found in registry");
                _logger.LogWarning("Dependency {DepName} not found", dep.DependsOnName);
            }
        }
    }
    
    /// <summary>
    /// Legacy overload for backward compatibility.
    /// Uses default tenant ID.
    /// </summary>
    public async Task PublishAsync(BmModel model, string? connectionString = null, CancellationToken ct = default)
    {
        // Use a default tenant ID for legacy calls
        var defaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await PublishAsync(model, defaultTenantId, connectionString, force: false, sourceHash: null, ct);
    }
    
    /// <summary>
    /// Compute source content hash for empty version bump detection.
    /// Normalizes version number before hashing so same content = same hash.
    /// </summary>
    public static string ComputeSourceHash(string source)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;
            
        // Normalize version: 'version '1.2.3'' → 'version '*.*.*''
        // This ensures same content = same hash, ignoring version number
        var normalized = Regex.Replace(
            source,
            @"(version\s+')[^']+(')""",
            m => m.Groups[1].Value + "*.*.*" + m.Groups[2].Value,
            RegexOptions.IgnoreCase);
        
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
