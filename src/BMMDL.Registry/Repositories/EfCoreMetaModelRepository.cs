using BMMDL.Registry.Entities;
using BMMDL.Registry.Entities.Normalized;
using BMMDL.Registry.Data;
using BMMDL.Registry.Services;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel;
using BMMDL.Registry.Repositories.Persistence;
using BMMDL.Registry.Repositories.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories;

/// <summary>
/// EF Core meta-model repository with normalized schema.
/// Uses RegistryDbContext as the single source of truth.
/// Thin facade that delegates to focused persister classes.
/// </summary>
public class EfCoreMetaModelRepository : IMetaModelRepository
{
    private readonly RegistryDbContext _db;
    private readonly Guid _tenantId;
    private readonly Guid? _moduleId;

    // Shared context and persisters
    private readonly RepositoryContext _ctx;
    private readonly EntityPersister _entities;
    private readonly ServicePersister _services;
    private readonly EnumTypeAspectPersister _enumTypeAspects;
    private readonly RulePersister _rules;
    private readonly AccessControlPersister _accessControls;
    private readonly ViewSequenceEventPersister _viewSeqEvents;
    private readonly MigrationPersister _migrations;

    public EfCoreMetaModelRepository(RegistryDbContext db, Guid tenantId, Guid? moduleId = null)
    {
        _db = db;
        _tenantId = tenantId;
        _moduleId = moduleId;

        _ctx = new RepositoryContext(db, tenantId, moduleId);
        _entities = new EntityPersister(_ctx);
        _services = new ServicePersister(_ctx);
        _enumTypeAspects = new EnumTypeAspectPersister(_ctx);
        _rules = new RulePersister(_ctx);
        _accessControls = new AccessControlPersister(_ctx);
        _viewSeqEvents = new ViewSequenceEventPersister(_ctx);
        _migrations = new MigrationPersister(_ctx);
    }

    // ============================================================
    // Namespace Management
    // ============================================================

    public async Task<Namespace> GetOrCreateNamespaceAsync(string name, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(name, ct);
        return ns!;
    }

    // ============================================================
    // Entity Operations
    // ============================================================

    public Task SaveEntityAsync(BmEntity entity, CancellationToken ct = default)
        => _entities.SaveEntityAsync(entity, ct);

    public Task SaveEntitiesAsync(IEnumerable<BmEntity> entities, CancellationToken ct = default)
        => _entities.SaveEntitiesAsync(entities, ct);

    public Task<EntityRecord?> GetEntityByNameAsync(string qualifiedName, CancellationToken ct = default)
        => _entities.GetEntityByNameAsync(qualifiedName, ct);

    public Task<IReadOnlyList<EntityRecord>> GetEntitiesAsync(string? ns = null, CancellationToken ct = default)
        => _entities.GetEntitiesAsync(ns, ct);

    // ============================================================
    // Service Operations
    // ============================================================

    public Task SaveServiceAsync(BmService service, CancellationToken ct = default)
        => _services.SaveServiceAsync(service, ct);

    public Task SaveServicesAsync(IEnumerable<BmService> services, CancellationToken ct = default)
        => _services.SaveServicesAsync(services, ct);

    public Task<IReadOnlyList<ServiceRecord>> GetServicesAsync(string? ns = null, CancellationToken ct = default)
        => _services.GetServicesAsync(ns, ct);

    // ============================================================
    // Type Operations
    // ============================================================

    public Task SaveTypeAsync(BmType type, CancellationToken ct = default)
        => _enumTypeAspects.SaveTypeAsync(type, ct);

    public Task SaveTypesAsync(IEnumerable<BmType> types, CancellationToken ct = default)
        => _enumTypeAspects.SaveTypesAsync(types, ct);

    public Task<IReadOnlyList<TypeRecord>> GetTypesAsync(string? ns = null, CancellationToken ct = default)
        => _enumTypeAspects.GetTypesAsync(ns, ct);

    // ============================================================
    // Enum Operations
    // ============================================================

    public Task SaveEnumAsync(BmEnum enumType, CancellationToken ct = default)
        => _enumTypeAspects.SaveEnumAsync(enumType, ct);

    public Task SaveEnumsAsync(IEnumerable<BmEnum> enums, CancellationToken ct = default)
        => _enumTypeAspects.SaveEnumsAsync(enums, ct);

    public Task<IReadOnlyList<EnumRecord>> GetEnumsAsync(string? ns = null, CancellationToken ct = default)
        => _enumTypeAspects.GetEnumsAsync(ns, ct);

    // ============================================================
    // Aspect Operations
    // ============================================================

    public Task SaveAspectAsync(BmAspect aspect, CancellationToken ct = default)
        => _enumTypeAspects.SaveAspectAsync(aspect, ct);

    public Task SaveAspectsAsync(IEnumerable<BmAspect> aspects, CancellationToken ct = default)
        => _enumTypeAspects.SaveAspectsAsync(aspects, ct);

    public Task<IReadOnlyList<AspectRecord>> GetAspectsAsync(string? ns = null, CancellationToken ct = default)
        => _enumTypeAspects.GetAspectsAsync(ns, ct);

    // ============================================================
    // View Operations
    // ============================================================

    public Task SaveViewAsync(BmView view, CancellationToken ct = default)
        => _viewSeqEvents.SaveViewAsync(view, ct);

    public Task SaveViewsAsync(IEnumerable<BmView> views, CancellationToken ct = default)
        => _viewSeqEvents.SaveViewsAsync(views, ct);

    // ============================================================
    // Rule Operations
    // ============================================================

    public Task SaveRuleAsync(BmRule rule, CancellationToken ct = default)
        => _rules.SaveRuleAsync(rule, ct);

    public Task SaveRulesAsync(IEnumerable<BmRule> rules, CancellationToken ct = default)
        => _rules.SaveRulesAsync(rules, ct);

    // ============================================================
    // Sequence Operations
    // ============================================================

    public Task SaveSequenceAsync(BmSequence sequence, CancellationToken ct = default)
        => _viewSeqEvents.SaveSequenceAsync(sequence, ct);

    public Task SaveSequencesAsync(IEnumerable<BmSequence> sequences, CancellationToken ct = default)
        => _viewSeqEvents.SaveSequencesAsync(sequences, ct);

    // ============================================================
    // Event Operations
    // ============================================================

    public Task SaveEventAsync(BmEvent evt, CancellationToken ct = default)
        => _viewSeqEvents.SaveEventAsync(evt, ct);

    public Task SaveEventsAsync(IEnumerable<BmEvent> events, CancellationToken ct = default)
        => _viewSeqEvents.SaveEventsAsync(events, ct);

    // ============================================================
    // AccessControl Operations
    // ============================================================

    public Task SaveAccessControlAsync(BmAccessControl ac, CancellationToken ct = default)
        => _accessControls.SaveAccessControlAsync(ac, ct);

    public Task SaveAccessControlsAsync(IEnumerable<BmAccessControl> accessControls, CancellationToken ct = default)
        => _accessControls.SaveAccessControlsAsync(accessControls, ct);

    // ============================================================
    // Migration Definition Operations
    // ============================================================

    public Task SaveMigrationDefAsync(BmMigrationDef migration, CancellationToken ct = default)
        => _migrations.SaveMigrationDefAsync(migration, ct);

    public Task SaveMigrationDefsAsync(IEnumerable<BmMigrationDef> migrations, CancellationToken ct = default)
        => _migrations.SaveMigrationDefsAsync(migrations, ct);

    // ============================================================
    // Bulk Operations - Using MetaModelCache (proper cache type)
    // ============================================================

    /// <summary>
    /// Save all model elements from a MetaModelCache.
    /// Uses cache.TenantId and cache.ModuleId for tenant isolation.
    /// OPTIMIZED: Disables auto-detect changes for 30-40% faster bulk operations.
    /// </summary>
    public async Task SaveAllFromCacheAsync(MetaModelCache cache, CancellationToken ct = default)
    {
        var previousState = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Pre-create namespaces
            var namespaces = cache.Entities.Values.Select(e => e.Namespace)
                .Concat(cache.Types.Values.Select(t => t.Namespace))
                .Concat(cache.Enums.Values.Select(e => e.Namespace))
                .Concat(cache.Aspects.Values.Select(a => a.Namespace))
                .Concat(cache.Services.Values.Select(s => s.Namespace))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();

            foreach (var ns in namespaces)
            {
                if (!_ctx.NamespaceCache.ContainsKey(ns!))
                {
                    var existing = await _db.Namespaces.FirstOrDefaultAsync(n => n.TenantId == _tenantId && n.Name == ns, ct);
                    if (existing == null)
                    {
                        existing = new Namespace { Id = Guid.NewGuid(), TenantId = _tenantId, Name = ns! };
                        _db.Namespaces.Add(existing);
                    }
                    _ctx.NamespaceCache[ns!] = existing;
                }
            }
            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);

            // Batch add entities
            foreach (var entity in cache.Entities.Values)
            {
                var ns = string.IsNullOrEmpty(entity.Namespace) ? null : _ctx.NamespaceCache.GetValueOrDefault(entity.Namespace);
                var record = new EntityRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ModuleId = _moduleId,
                    Name = entity.Name,
                    QualifiedName = entity.QualifiedName,
                    NamespaceId = ns?.Id,
                    IsTenantScoped = entity.TenantScoped,
                    IsAbstract = entity.IsAbstract,
                    ParentEntityName = entity.ParentEntityName,
                    DiscriminatorValue = entity.DiscriminatorValue,
                    ExtendsFrom = entity.ExtendsFrom
                };
                _entities.MapEntityChildren(entity, record);
                _db.Entities.Add(record);
            }

            // Batch add services
            foreach (var service in cache.Services.Values)
            {
                var ns = string.IsNullOrEmpty(service.Namespace) ? null : _ctx.NamespaceCache.GetValueOrDefault(service.Namespace);
                var record = new ServiceRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ModuleId = _moduleId,
                    Name = service.Name,
                    QualifiedName = service.QualifiedName,
                    NamespaceId = ns?.Id
                };
                _services.MapServiceChildren(service, record);
                _db.Services.Add(record);
            }

            // Batch add types
            foreach (var type in cache.Types.Values)
            {
                var ns = string.IsNullOrEmpty(type.Namespace) ? null : _ctx.NamespaceCache.GetValueOrDefault(type.Namespace);
                var record = new TypeRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ModuleId = _moduleId,
                    Name = type.Name,
                    QualifiedName = type.QualifiedName,
                    NamespaceId = ns?.Id,
                    BaseType = type.BaseType,
                    Length = type.Length,
                    Precision = type.Precision,
                    Scale = type.Scale
                };
                _enumTypeAspects.MapTypeFields(type, record);
                AnnotationHelper.SaveAnnotationsForOwner(_db, type.Annotations, "type", record.Id);
                _db.Types.Add(record);
            }

            // Batch add enums
            foreach (var enumType in cache.Enums.Values)
            {
                var ns = string.IsNullOrEmpty(enumType.Namespace) ? null : _ctx.NamespaceCache.GetValueOrDefault(enumType.Namespace);
                var record = new EnumRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ModuleId = _moduleId,
                    Name = enumType.Name,
                    QualifiedName = enumType.QualifiedName,
                    NamespaceId = ns?.Id,
                    BaseType = enumType.BaseType
                };
                _enumTypeAspects.MapEnumValues(enumType, record);
                AnnotationHelper.SaveAnnotationsForOwner(_db, enumType.Annotations, "enum", record.Id);
                _db.Enums.Add(record);
            }

            // Batch add aspects
            foreach (var aspect in cache.Aspects.Values)
            {
                var ns = string.IsNullOrEmpty(aspect.Namespace) ? null : _ctx.NamespaceCache.GetValueOrDefault(aspect.Namespace);
                var record = new AspectRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ModuleId = _moduleId,
                    Name = aspect.Name,
                    QualifiedName = aspect.QualifiedName,
                    NamespaceId = ns?.Id
                };
                _enumTypeAspects.MapAspectChildren(aspect, record);
                AnnotationHelper.SaveAnnotationsForOwner(_db, aspect.Annotations, "aspect", record.Id);
                _db.Aspects.Add(record);
            }

            // OPTIMIZATION: Manual detect changes before save
            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = previousState;
        }
    }

    /// <summary>
    /// Load all model elements for this tenant into a MetaModelCache.
    /// FIXED: Sequential loading (DbContext is not thread-safe with parallel queries).
    /// Uses AsNoTracking and AsSplitQuery for performance.
    /// </summary>
    public async Task LoadAllToCacheAsync(MetaModelCache cache, CancellationToken ct = default)
    {
        cache.TenantId = _tenantId;
        cache.ModuleId = _moduleId;

        var entities = await _entities.LoadEntitiesAsync(ct);
        foreach (var record in entities)
            cache.AddEntity(_entities.MapToBmEntity(record));

        var services = await _services.LoadServicesAsync(ct);
        foreach (var record in services)
            cache.AddService(_services.MapToBmService(record));

        var types = await _enumTypeAspects.LoadTypesAsync(ct);
        foreach (var record in types)
            cache.AddType(_enumTypeAspects.MapToBmType(record));

        var enums = await _enumTypeAspects.LoadEnumsAsync(ct);
        foreach (var record in enums)
            cache.AddEnum(_enumTypeAspects.MapToBmEnum(record));

        var aspects = await _enumTypeAspects.LoadAspectsAsync(ct);
        foreach (var record in aspects)
            cache.AddAspect(_enumTypeAspects.MapToBmAspect(record));

        // Load rules with expression AST reconstruction
        var rules = await _rules.LoadRulesAsync(ct);
        var ruleNodesByOwner = await LoadExpressionNodesForRules(rules, ct);
        foreach (var record in rules)
            cache.AddRule(_rules.MapToBmRule(record, ruleNodesByOwner));

        // Load views
        var views = await _viewSeqEvents.LoadViewsAsync(ct);
        foreach (var record in views)
            cache.AddView(_viewSeqEvents.MapToBmView(record));

        // Load sequences
        var sequences = await _viewSeqEvents.LoadSequencesAsync(ct);
        foreach (var record in sequences)
            cache.AddSequence(_viewSeqEvents.MapToBmSequence(record));

        // Load events
        var events = await _viewSeqEvents.LoadEventsAsync(ct);
        foreach (var record in events)
            cache.AddEvent(_viewSeqEvents.MapToBmEvent(record));

        // Load access controls with expression AST reconstruction
        var accessControlRecords = await _accessControls.LoadAccessControlsAsync(ct);
        var (accessRuleNodesByOwner, fieldRestrictionNodesByOwner) = await LoadExpressionNodesForAccessControls(accessControlRecords, ct);
        foreach (var record in accessControlRecords)
            cache.AddAccessControl(_accessControls.MapToBmAccessControl(record, accessRuleNodesByOwner, fieldRestrictionNodesByOwner));

        cache.MarkInitialized();
    }

    /// <summary>
    /// Load all model elements for this tenant into a BmModel.
    /// Convenience method for consistency checking.
    /// FIXED: Sequential loading (DbContext is not thread-safe with parallel queries).
    /// Uses AsNoTracking and AsSplitQuery for performance.
    /// </summary>
    public async Task<BmModel> LoadModelAsync(CancellationToken ct = default)
    {
        var model = new BmModel();

        var modules = await LoadModulesAsync(ct);
        foreach (var record in modules)
            model.AllModules.Add(MapToBmModuleDeclaration(record));

        var entities = await _entities.LoadEntitiesAsync(ct);
        foreach (var record in entities)
            model.Entities.Add(_entities.MapToBmEntity(record));

        var services = await _services.LoadServicesAsync(ct);
        foreach (var record in services)
            model.Services.Add(_services.MapToBmService(record));

        var types = await _enumTypeAspects.LoadTypesAsync(ct);
        foreach (var record in types)
            model.Types.Add(_enumTypeAspects.MapToBmType(record));

        var enums = await _enumTypeAspects.LoadEnumsAsync(ct);
        foreach (var record in enums)
            model.Enums.Add(_enumTypeAspects.MapToBmEnum(record));

        var aspects = await _enumTypeAspects.LoadAspectsAsync(ct);
        foreach (var record in aspects)
            model.Aspects.Add(_enumTypeAspects.MapToBmAspect(record));

        // Load rules with expression AST reconstruction
        var rules = await _rules.LoadRulesAsync(ct);
        var ruleNodesByOwner = await LoadExpressionNodesForRules(rules, ct);
        foreach (var record in rules)
            model.Rules.Add(_rules.MapToBmRule(record, ruleNodesByOwner));

        // Load views
        var views = await _viewSeqEvents.LoadViewsAsync(ct);
        foreach (var record in views)
            model.Views.Add(_viewSeqEvents.MapToBmView(record));

        // Load sequences
        var sequences = await _viewSeqEvents.LoadSequencesAsync(ct);
        foreach (var record in sequences)
            model.Sequences.Add(_viewSeqEvents.MapToBmSequence(record));

        // Load events
        var events = await _viewSeqEvents.LoadEventsAsync(ct);
        foreach (var record in events)
            model.Events.Add(_viewSeqEvents.MapToBmEvent(record));

        // Load access controls
        var accessControlRecords = await _accessControls.LoadAccessControlsAsync(ct);
        var (accessRuleNodesByOwner, fieldRestrictionNodesByOwner) = await LoadExpressionNodesForAccessControls(accessControlRecords, ct);
        foreach (var record in accessControlRecords)
            model.AccessControls.Add(_accessControls.MapToBmAccessControl(record, accessRuleNodesByOwner, fieldRestrictionNodesByOwner));

        // Load migration definitions
        var migrationDefs = await _migrations.LoadMigrationDefsAsync(ct);
        foreach (var record in migrationDefs)
            model.Migrations.Add(_migrations.MapToBmMigrationDef(record));

        return model;
    }

    // ============================================================
    // Statement Reconstruction (exposed for tests)
    // ============================================================

    public List<BmRuleStatement> ReconstructBmStatements(IEnumerable<StatementNode> nodes)
        => _ctx.StmtSerializer.ReconstructBmStatements(nodes);

    // ============================================================
    // Stats
    // ============================================================

    public async Task<PersistenceStats> GetStatsAsync(CancellationToken ct = default)
    {
        return new PersistenceStats
        {
            EntityCount = await _db.Entities.CountAsync(ct),
            ServiceCount = await _db.Services.CountAsync(ct),
            TypeCount = await _db.Types.CountAsync(ct),
            EnumCount = await _db.Enums.CountAsync(ct),
            AspectCount = await _db.Aspects.CountAsync(ct),
            ViewCount = await _db.Views.CountAsync(ct),
            RuleCount = await _db.Rules.CountAsync(ct),
            SequenceCount = await _db.Sequences.CountAsync(ct),
            EventCount = await _db.Events.CountAsync(ct),
            AccessControlCount = await _db.AccessControls.CountAsync(ct),
            LastUpdated = await _db.Entities.MaxAsync(e => (DateTime?)e.UpdatedAt, ct)
        };
    }

    // ============================================================
    // Private Helpers
    // ============================================================

    private async Task<List<Module>> LoadModulesAsync(CancellationToken ct)
    {
        return await _db.Modules
            .AsNoTracking()
            .Where(m => m.TenantId == _tenantId)
            .Include(m => m.Dependencies)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    private static BmModuleDeclaration MapToBmModuleDeclaration(Module record)
    {
        var module = new BmModuleDeclaration
        {
            Name = record.Name,
            Version = record.Version,
            Description = record.Description,
            Author = record.Author,
            TenantAware = record.TenantAware
        };

        foreach (var dep in record.Dependencies)
        {
            module.Dependencies.Add(new BmModuleDependency
            {
                ModuleName = dep.DependsOnName,
                VersionRange = dep.VersionRange
            });
        }

        if (!string.IsNullOrEmpty(record.PublishesJson))
        {
            var publishes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(record.PublishesJson);
            if (publishes != null)
                foreach (var p in publishes) module.Publishes.Add(p);
        }
        if (!string.IsNullOrEmpty(record.ImportsJson))
        {
            var imports = System.Text.Json.JsonSerializer.Deserialize<List<string>>(record.ImportsJson);
            if (imports != null)
                foreach (var imp in imports) module.Imports.Add(imp);
        }

        return module;
    }

    /// <summary>
    /// Load expression nodes grouped by owner for rule statements.
    /// </summary>
    private async Task<Dictionary<Guid, List<ExpressionNode>>> LoadExpressionNodesForRules(
        List<RuleRecord> rules, CancellationToken ct)
    {
        var statementIds = rules.SelectMany(r => r.Statements).Select(s => s.Id).ToHashSet();
        var nodesByOwner = new Dictionary<Guid, List<ExpressionNode>>();

        if (statementIds.Count > 0)
        {
            var expressionNodes = await _db.ExpressionNodes
                .AsNoTracking()
                .Where(n => n.OwnerType == "rule_statement" && statementIds.Contains(n.OwnerId))
                .ToListAsync(ct);

            foreach (var node in expressionNodes)
            {
                if (!nodesByOwner.TryGetValue(node.OwnerId, out var list))
                {
                    list = new List<ExpressionNode>();
                    nodesByOwner[node.OwnerId] = list;
                }
                list.Add(node);
            }
        }

        return nodesByOwner;
    }

    /// <summary>
    /// Load expression nodes for access control rules and field restrictions.
    /// </summary>
    private async Task<(Dictionary<Guid, List<ExpressionNode>> ruleNodes, Dictionary<Guid, List<ExpressionNode>> frNodes)>
        LoadExpressionNodesForAccessControls(List<AccessControlRecord> accessControls, CancellationToken ct)
    {
        var acRuleIds = accessControls.SelectMany(ac => ac.Rules).Select(r => r.Id).ToHashSet();
        var accessRuleNodesByOwner = new Dictionary<Guid, List<ExpressionNode>>();
        var fieldRestrictionNodesByOwner = new Dictionary<Guid, List<ExpressionNode>>();

        if (acRuleIds.Count > 0)
        {
            var accessRuleExprNodes = await _db.ExpressionNodes
                .AsNoTracking()
                .Where(n => n.OwnerType == "access_rule" && acRuleIds.Contains(n.OwnerId))
                .ToListAsync(ct);

            foreach (var node in accessRuleExprNodes)
            {
                if (!accessRuleNodesByOwner.TryGetValue(node.OwnerId, out var list))
                {
                    list = new List<ExpressionNode>();
                    accessRuleNodesByOwner[node.OwnerId] = list;
                }
                list.Add(node);
            }

            var frIds = accessControls.SelectMany(ac => ac.Rules)
                .SelectMany(r => r.FieldRestrictions).Select(f => f.Id).ToHashSet();
            if (frIds.Count > 0)
            {
                var frExprNodes = await _db.ExpressionNodes
                    .AsNoTracking()
                    .Where(n => n.OwnerType == "field_restriction" && frIds.Contains(n.OwnerId))
                    .ToListAsync(ct);

                foreach (var node in frExprNodes)
                {
                    if (!fieldRestrictionNodesByOwner.TryGetValue(node.OwnerId, out var list))
                    {
                        list = new List<ExpressionNode>();
                        fieldRestrictionNodesByOwner[node.OwnerId] = list;
                    }
                    list.Add(node);
                }
            }
        }

        return (accessRuleNodesByOwner, fieldRestrictionNodesByOwner);
    }
}

/// <summary>
/// Persistence statistics.
/// </summary>
public class PersistenceStats
{
    public int EntityCount { get; set; }
    public int ServiceCount { get; set; }
    public int TypeCount { get; set; }
    public int EnumCount { get; set; }
    public int AspectCount { get; set; }
    public int ViewCount { get; set; }
    public int RuleCount { get; set; }
    public int SequenceCount { get; set; }
    public int EventCount { get; set; }
    public int AccessControlCount { get; set; }
    public DateTime? LastUpdated { get; set; }
}
