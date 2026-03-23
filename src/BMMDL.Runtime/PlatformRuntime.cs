using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Data;
using BMMDL.Runtime.DataAccess;
using BMMDL.SchemaManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime;

/// <summary>
/// Main entry point for Platform Runtime.
/// Handles initialization, migration, and provides access to runtime services.
/// </summary>
public class PlatformRuntime
{
    private readonly string _registryConnectionString;
    private readonly string _platformConnectionString;
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly ILogger<PlatformRuntime> _logger;
    
    private MetaModelCache? _cache;
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    
    // System Tenant ID (fixed)
    public static readonly Guid SystemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000000");
    public const string PlatformModuleName = "Platform";
    
    public PlatformRuntime(
        string registryConnectionString,
        string platformConnectionString,
        ILogger<PlatformRuntime> logger)
        : this(registryConnectionString, platformConnectionString, new TenantConnectionFactory(platformConnectionString), logger)
    {
    }
    
    public PlatformRuntime(
        string registryConnectionString,
        string platformConnectionString,
        ITenantConnectionFactory connectionFactory,
        ILogger<PlatformRuntime> logger)
    {
        _registryConnectionString = registryConnectionString;
        _platformConnectionString = platformConnectionString;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger;
    }
    
    /// <summary>
    /// Gets the meta-model cache (available after Initialize).
    /// </summary>
    public MetaModelCache Cache => _cache ?? throw new InvalidOperationException("Runtime not initialized. Call Initialize() first.");
    
    /// <summary>
    /// Gets whether the runtime has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;
    
    /// <summary>
    /// Initialize the platform runtime:
    /// 1. Load meta-model from Registry
    /// 2. Migrate tables to platform database
    /// 3. Cache meta-model for CRUD operations
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
        {
            _logger.LogWarning("Platform runtime already initialized");
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                _logger.LogWarning("Platform runtime already initialized");
                return;
            }

            _logger.LogInformation("Initializing Platform Runtime...");

            // Step 1: Load meta-model from Registry
            _logger.LogInformation("Loading meta-model from Registry...");
            var model = await LoadMetaModelFromRegistryAsync(ct);

            if (model == null)
            {
                throw new InvalidOperationException(
                    $"Platform module '{PlatformModuleName}' not found in Registry. " +
                    "Run 'bmmdlc bootstrap --init-platform' first.");
            }

            _logger.LogInformation("Loaded {EntityCount} entities from Platform module", model.Entities.Count);

            // Step 2: Migrate tables
            _logger.LogInformation("Migrating platform tables...");
            await MigratePlatformTablesAsync(model, ct);

            // Step 3: Cache meta-model
            _logger.LogInformation("Caching meta-model...");
            _cache = new MetaModelCache(model);

            _initialized = true;
            _logger.LogInformation("Platform Runtime initialized successfully");
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    /// <summary>
    /// Load meta-model from Registry for the Platform module.
    /// </summary>
    private async Task<BmModel?> LoadMetaModelFromRegistryAsync(CancellationToken ct)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RegistryDbContext>();
        optionsBuilder.UseNpgsql(_registryConnectionString);
        
        await using var dbContext = new RegistryDbContext(optionsBuilder.Options);
        
        // Find Platform module for System Tenant
        var module = await dbContext.Modules.FirstOrDefaultAsync(
            m => m.TenantId == SystemTenantId && m.Name == PlatformModuleName, ct);
            
        if (module == null)
        {
            return null;
        }
        
        // Load entities from registry
        var entities = await dbContext.Entities
            .Where(e => e.TenantId == SystemTenantId && e.ModuleId == module.Id)
            .ToListAsync(ct);
            
        // Load fields for each entity
        var entityIds = entities.Select(e => e.Id).ToList();
        var allFields = await dbContext.EntityFields
            .Where(f => entityIds.Contains(f.EntityId))
            .ToListAsync(ct);
            
        var fieldsByEntity = allFields.GroupBy(f => f.EntityId)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        // Build BmModel from registry data
        var model = new BmModel();
        model.Module = new BmModuleDeclaration
        {
            Name = module.Name,
            Version = module.Version ?? "1.0.0"
        };
        
        foreach (var entityRecord in entities)
        {
            var entity = new BmEntity
            {
                Name = entityRecord.Name,
                Namespace = SchemaConstants.PlatformSchema // Set namespace so QualifiedName is computed
            };
            
            // Load fields from FieldRecords
            if (fieldsByEntity.TryGetValue(entityRecord.Id, out var fieldRecords))
            {
                foreach (var fr in fieldRecords)
                {
                    entity.Fields.Add(new BmField
                    {
                        Name = fr.Name,
                        IsKey = fr.IsKey,
                        IsNullable = fr.IsNullable,
                    });
                }
            }
            
            model.Entities.Add(entity);
        }
        
        return model;
    }
    
    /// <summary>
    /// Migrate platform tables using SchemaManager.
    /// </summary>
    private async Task MigratePlatformTablesAsync(BmModel model, CancellationToken ct)
    {
        var options = new SchemaManagerOptions
        {
            ConnectionString = _platformConnectionString,
            Verbose = true
        };
        
        var schemaManager = new PostgresSchemaManager(options);
        
        // Use InitializeSchemaAsync to create tables (force=true to recreate if exists)
        await schemaManager.InitializeSchemaAsync(model, force: true, dryRun: false, ct);
        
        _logger.LogInformation("Platform tables migrated successfully");
    }
    
    /// <summary>
    /// Create a repository for the specified entity type.
    /// </summary>
    public DynamicRepository CreateRepository(string entityName, Guid? tenantId = null)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Runtime not initialized. Call Initialize() first.");
        }
        
        var entity = _cache!.GetEntity(entityName);
        if (entity == null)
        {
            throw new ArgumentException($"Entity '{entityName}' not found in meta-model cache");
        }
        
        return new DynamicRepository(_connectionFactory, entity, _cache, tenantId);
    }
    
    /// <summary>
    /// Get typed repository for PlatformUser.
    /// </summary>
    public DynamicRepository Users => CreateRepository("PlatformUser");
    
    /// <summary>
    /// Get typed repository for PlatformRole.
    /// </summary>
    public DynamicRepository Roles => CreateRepository("PlatformRole");
    
    /// <summary>
    /// Get typed repository for Tenant.
    /// </summary>
    public DynamicRepository Tenants => CreateRepository("Tenant");
    
    /// <summary>
    /// Get typed repository for PlatformPermission.
    /// </summary>
    public DynamicRepository Permissions => CreateRepository("PlatformPermission");
}
