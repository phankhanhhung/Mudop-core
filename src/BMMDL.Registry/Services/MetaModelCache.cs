using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel;

namespace BMMDL.Registry.Services;

/// <summary>
/// In-memory cache for compiled BMMDL meta models.
/// This is the canonical cache type used for persistence operations.
/// </summary>
public class MetaModelCache
{
    private readonly Dictionary<string, BmEntity> _entities = new();
    private readonly Dictionary<string, BmService> _services = new();
    private readonly Dictionary<string, BmType> _types = new();
    private readonly Dictionary<string, BmEnum> _enums = new();
    private readonly Dictionary<string, BmAspect> _aspects = new();
    private readonly Dictionary<string, BmView> _views = new();
    private readonly List<BmRule> _rules = new();
    private readonly List<BmAccessControl> _accessControls = new();
    private readonly List<BmSequence> _sequences = new();
    private readonly List<BmEvent> _events = new();
    private readonly List<string> _sourceFiles = new();

    /// <summary>
    /// TenantId for multi-tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional ModuleId for module ownership tracking.
    /// </summary>
    public Guid? ModuleId { get; set; }

    public bool IsInitialized { get; private set; }
    public DateTime InitializedAt { get; private set; }

    // Read-only accessors
    public IReadOnlyDictionary<string, BmEntity> Entities => _entities;
    public IReadOnlyDictionary<string, BmService> Services => _services;
    public IReadOnlyDictionary<string, BmType> Types => _types;
    public IReadOnlyDictionary<string, BmEnum> Enums => _enums;
    public IReadOnlyDictionary<string, BmAspect> Aspects => _aspects;
    public IReadOnlyDictionary<string, BmView> Views => _views;
    public IReadOnlyList<BmRule> Rules => _rules;
    public IReadOnlyList<BmAccessControl> AccessControls => _accessControls;
    public IReadOnlyList<BmSequence> Sequences => _sequences;
    public IReadOnlyList<BmEvent> Events => _events;
    public IReadOnlyList<string> SourceFiles => _sourceFiles;

    // Counts
    public int EntityCount => _entities.Count;
    public int ServiceCount => _services.Count;
    public int TypeCount => _types.Count;
    public int EnumCount => _enums.Count;
    public int AspectCount => _aspects.Count;
    public int ViewCount => _views.Count;
    public int RuleCount => _rules.Count;
    public int AccessControlCount => _accessControls.Count;
    public int SequenceCount => _sequences.Count;
    public int EventCount => _events.Count;
    
    /// <summary>
    /// Adds an entity to the cache.
    /// </summary>
    public void AddEntity(BmEntity entity)
    {
        var key = entity.QualifiedName;
        _entities[key] = entity;
    }
    
    /// <summary>
    /// Adds a service to the cache.
    /// </summary>
    public void AddService(BmService service)
    {
        var key = service.QualifiedName;
        _services[key] = service;
    }
    
    /// <summary>
    /// Adds a type definition to the cache.
    /// </summary>
    public void AddType(BmType type)
    {
        var key = type.QualifiedName;
        _types[key] = type;
    }
    
    /// <summary>
    /// Adds an enum to the cache.
    /// </summary>
    public void AddEnum(BmEnum enumType)
    {
        var key = enumType.QualifiedName;
        _enums[key] = enumType;
    }
    
    /// <summary>
    /// Adds an aspect to the cache.
    /// </summary>
    public void AddAspect(BmAspect aspect)
    {
        var key = aspect.QualifiedName;
        _aspects[key] = aspect;
    }
    
    /// <summary>
    /// Adds a view to the cache.
    /// </summary>
    public void AddView(BmView view)
    {
        var key = view.QualifiedName;
        _views[key] = view;
    }

    /// <summary>
    /// Adds a rule to the cache.
    /// </summary>
    public void AddRule(BmRule rule)
    {
        _rules.Add(rule);
    }

    /// <summary>
    /// Adds an access control to the cache.
    /// </summary>
    public void AddAccessControl(BmAccessControl accessControl)
    {
        _accessControls.Add(accessControl);
    }

    /// <summary>
    /// Adds a sequence to the cache.
    /// </summary>
    public void AddSequence(BmSequence sequence)
    {
        _sequences.Add(sequence);
    }

    /// <summary>
    /// Adds an event to the cache.
    /// </summary>
    public void AddEvent(BmEvent evt)
    {
        _events.Add(evt);
    }

    /// <summary>
    /// Registers a source file that was compiled.
    /// </summary>
    public void AddSourceFile(string filePath)
    {
        _sourceFiles.Add(filePath);
    }
    
    /// <summary>
    /// Marks the cache as initialized.
    /// </summary>
    public void MarkInitialized()
    {
        IsInitialized = true;
        InitializedAt = DateTime.UtcNow;
    }
    
    // Query helpers
    public BmEntity? FindEntity(string qualifiedName) 
        => _entities.TryGetValue(qualifiedName, out var entity) ? entity : null;
    
    public BmService? FindService(string qualifiedName) 
        => _services.TryGetValue(qualifiedName, out var service) ? service : null;

    public BmView? FindView(string qualifiedName) 
        => _views.TryGetValue(qualifiedName, out var view) ? view : null;
    
    public IEnumerable<BmEntity> QueryEntities(Func<BmEntity, bool>? predicate = null)
        => predicate == null ? _entities.Values : _entities.Values.Where(predicate);
    
    public IEnumerable<BmService> QueryServices(Func<BmService, bool>? predicate = null)
        => predicate == null ? _services.Values : _services.Values.Where(predicate);

    public IEnumerable<BmView> QueryViews(Func<BmView, bool>? predicate = null)
        => predicate == null ? _views.Values : _views.Values.Where(predicate);
}
