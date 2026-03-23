using System.Collections.Concurrent;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Runtime;

/// <summary>
/// Represents an association or composition from another entity that references a target entity.
/// Used for reverse-lookup of "who points to me?" for referential integrity checks.
/// </summary>
public record IncomingReference(
    BmEntity SourceEntity,
    string NavigationName,
    string FkColumnName,
    bool IsComposition,
    BmCardinality Cardinality,
    DeleteAction? OnDelete = null
);

/// <summary>
/// In-memory cache for meta-model definitions.
/// Provides fast lookup of entities, fields, and types.
/// </summary>
public class MetaModelCache : IMetaModelCache
{
    private readonly BmModel _model;
    private readonly Dictionary<string, BmEntity> _entitiesByName;
    private readonly Dictionary<string, BmEntity> _entitiesByQualifiedName;
    private readonly Dictionary<string, BmType> _typesByName;
    private readonly Dictionary<string, BmEnum> _enumsByName;
    private readonly Dictionary<string, List<BmRule>> _rulesByEntity;
    private readonly Dictionary<string, List<BmAccessControl>> _accessControlsByEntity;
    private readonly Dictionary<string, BmService> _servicesByName;
    private readonly Dictionary<string, BmView> _viewsByName;
    private readonly Dictionary<string, BmSequence> _sequencesByName;
    private readonly ConcurrentDictionary<string, BmEvent> _eventsByName;
    private readonly Dictionary<string, List<IncomingReference>> _incomingReferences;
    
    /// <summary>
    /// Creates an empty cache for manual building (e.g., tests, codegen).
    /// </summary>
    public MetaModelCache()
    {
        _model = new BmModel();
        _entitiesByName = new Dictionary<string, BmEntity>(StringComparer.OrdinalIgnoreCase);
        _entitiesByQualifiedName = new Dictionary<string, BmEntity>(StringComparer.OrdinalIgnoreCase);
        _typesByName = new Dictionary<string, BmType>(StringComparer.OrdinalIgnoreCase);
        _enumsByName = new Dictionary<string, BmEnum>(StringComparer.OrdinalIgnoreCase);
        _rulesByEntity = new Dictionary<string, List<BmRule>>(StringComparer.OrdinalIgnoreCase);
        _accessControlsByEntity = new Dictionary<string, List<BmAccessControl>>(StringComparer.OrdinalIgnoreCase);
        _servicesByName = new Dictionary<string, BmService>(StringComparer.OrdinalIgnoreCase);
        _viewsByName = new Dictionary<string, BmView>(StringComparer.OrdinalIgnoreCase);
        _sequencesByName = new Dictionary<string, BmSequence>(StringComparer.OrdinalIgnoreCase);
        _eventsByName = new ConcurrentDictionary<string, BmEvent>(StringComparer.OrdinalIgnoreCase);
        _incomingReferences = new Dictionary<string, List<IncomingReference>>(StringComparer.OrdinalIgnoreCase);
    }

    public MetaModelCache(BmModel model)
    {
        _model = model;
        
        // Index entities by name and qualified name (last-wins on duplicate simple names)
        _entitiesByName = new Dictionary<string, BmEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.Entities)
        {
            if (_entitiesByName.ContainsKey(e.Name))
                System.Diagnostics.Debug.WriteLine($"WARNING: Duplicate entity name '{e.Name}' — last definition wins");
            _entitiesByName[e.Name] = e;
        }

        _entitiesByQualifiedName = new Dictionary<string, BmEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.Entities)
        {
            if (!string.IsNullOrEmpty(e.QualifiedName))
                _entitiesByQualifiedName[e.QualifiedName!] = e;
        }

        // Index types (last-wins on duplicate simple names across modules)
        _typesByName = new Dictionary<string, BmType>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in model.Types)
            _typesByName[t.Name] = t;

        // Index enums (last-wins on duplicate simple names across modules)
        _enumsByName = new Dictionary<string, BmEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.Enums)
            _enumsByName[e.Name] = e;

        // Index rules by target entity (both simple and qualified name for reliable lookup)
        _rulesByEntity = new Dictionary<string, List<BmRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in model.Rules)
        {
            // Index by the raw TargetEntity value
            if (!_rulesByEntity.TryGetValue(rule.TargetEntity, out var ruleList))
            {
                ruleList = new List<BmRule>();
                _rulesByEntity[rule.TargetEntity] = ruleList;
            }
            ruleList.Add(rule);

            // Also index by the resolved entity's qualified name (if different from TargetEntity)
            var targetEntity = _entitiesByName.GetValueOrDefault(rule.TargetEntity)
                            ?? _entitiesByQualifiedName.GetValueOrDefault(rule.TargetEntity);
            if (targetEntity != null && !string.IsNullOrEmpty(targetEntity.QualifiedName)
                && !targetEntity.QualifiedName.Equals(rule.TargetEntity, StringComparison.OrdinalIgnoreCase))
            {
                if (!_rulesByEntity.TryGetValue(targetEntity.QualifiedName, out var qualifiedList))
                {
                    qualifiedList = new List<BmRule>();
                    _rulesByEntity[targetEntity.QualifiedName] = qualifiedList;
                }
                qualifiedList.Add(rule);
            }
        }

        // Index access controls by target entity
        _accessControlsByEntity = new Dictionary<string, List<BmAccessControl>>(StringComparer.OrdinalIgnoreCase);
        foreach (var acl in model.AccessControls)
        {
            if (!_accessControlsByEntity.TryGetValue(acl.TargetEntity, out var aclList))
            {
                aclList = new List<BmAccessControl>();
                _accessControlsByEntity[acl.TargetEntity] = aclList;
            }
            aclList.Add(acl);
        }

        // Index services by name (last-wins on duplicates)
        _servicesByName = new Dictionary<string, BmService>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in model.Services)
            _servicesByName[s.Name] = s;

        // Index views by name (last-wins on duplicates)
        _viewsByName = new Dictionary<string, BmView>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in model.Views)
            _viewsByName[v.Name] = v;

        // Index sequences by name (last-wins on duplicates)
        _sequencesByName = new Dictionary<string, BmSequence>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in model.Sequences)
            _sequencesByName[s.Name] = s;

        // Index events by name (last-wins on duplicates, thread-safe)
        _eventsByName = new ConcurrentDictionary<string, BmEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in model.Events)
            _eventsByName[ev.Name] = ev;

        // Resolve inheritance: link ParentEntity ↔ DerivedEntities navigation properties
        foreach (var entity in model.Entities)
        {
            if (!string.IsNullOrEmpty(entity.ParentEntityName) && entity.ParentEntity == null)
            {
                var parent = _entitiesByName.GetValueOrDefault(entity.ParentEntityName)
                    ?? _entitiesByQualifiedName.GetValueOrDefault(entity.ParentEntityName);
                if (parent != null)
                {
                    entity.ParentEntity = parent;
                    if (!parent.DerivedEntities.Contains(entity))
                        parent.DerivedEntities.Add(entity);
                }
            }
        }

        // Build reverse association index: target entity → list of incoming references
        _incomingReferences = new Dictionary<string, List<IncomingReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            foreach (var assoc in entity.Associations)
            {
                var fkCol = assoc.Cardinality == BmCardinality.ManyToMany
                    ? NamingConvention.GetFkColumnName(entity.Name) // junction table FK
                    : NamingConvention.GetFkColumnName(assoc.Name);
                AddIncomingReference(assoc.TargetEntity, entity, assoc.Name, fkCol, false, assoc.Cardinality, assoc.OnDelete);
            }
            foreach (var comp in entity.Compositions)
            {
                // Composition FK is on the child: child has parentEntity_id column
                var fkCol = NamingConvention.GetFkColumnName(entity.Name);
                AddIncomingReference(comp.TargetEntity, entity, comp.Name, fkCol, true, comp.Cardinality, comp.OnDelete);
            }
        }
    }

    private void AddIncomingReference(
        string targetEntity, BmEntity sourceEntity, string navName,
        string fkCol, bool isComposition, BmCardinality cardinality, DeleteAction? onDelete = null)
    {
        if (!_incomingReferences.TryGetValue(targetEntity, out var list))
        {
            list = new List<IncomingReference>();
            _incomingReferences[targetEntity] = list;
        }
        list.Add(new IncomingReference(sourceEntity, navName, fkCol, isComposition, cardinality, onDelete));
    }
    
    /// <summary>
    /// Gets the underlying model.
    /// </summary>
    public BmModel Model => _model;
    
    /// <summary>
    /// Gets all cached entities.
    /// </summary>
    public IReadOnlyCollection<BmEntity> Entities => _entitiesByName.Values;
    
    /// <summary>
    /// Gets all cached types as a dictionary (case-insensitive lookup).
    /// </summary>
    public IReadOnlyDictionary<string, BmType> Types => _typesByName;

    /// <summary>
    /// Gets all cached enums as a dictionary (case-insensitive lookup).
    /// </summary>
    public IReadOnlyDictionary<string, BmEnum> Enums => _enumsByName;

    /// <summary>
    /// Gets all cached services.
    /// </summary>
    public IReadOnlyCollection<BmService> Services => _servicesByName.Values;
    
    /// <summary>
    /// Gets entity by name (case-insensitive).
    /// </summary>
    public BmEntity? GetEntity(string name)
    {
        if (_entitiesByName.TryGetValue(name, out var entity))
            return entity;
            
        if (_entitiesByQualifiedName.TryGetValue(name, out entity))
            return entity;
            
        return null;
    }
    
    /// <summary>
    /// Gets entity by qualified name.
    /// </summary>
    public BmEntity? GetEntityByQualifiedName(string qualifiedName)
    {
        _entitiesByQualifiedName.TryGetValue(qualifiedName, out var entity);
        return entity;
    }
    
    /// <summary>
    /// Gets type definition by name.
    /// </summary>
    public BmType? GetType(string name)
    {
        _typesByName.TryGetValue(name, out var type);
        return type;
    }
    
    /// <summary>
    /// Gets enum definition by name.
    /// </summary>
    public BmEnum? GetEnum(string name)
    {
        _enumsByName.TryGetValue(name, out var @enum);
        return @enum;
    }
    
    /// <summary>
    /// Checks if an entity exists.
    /// </summary>
    public bool HasEntity(string name)
    {
        return _entitiesByName.ContainsKey(name) || _entitiesByQualifiedName.ContainsKey(name);
    }
    
    /// <summary>
    /// Gets all entity names.
    /// </summary>
    public IEnumerable<string> EntityNames => _entitiesByName.Keys;
    
    /// <summary>
    /// Gets all service names.
    /// </summary>
    public IEnumerable<string> ServiceNames => _servicesByName.Keys;

    /// <summary>
    /// Gets service by name (case-insensitive).
    /// </summary>
    public BmService? GetService(string name)
    {
        _servicesByName.TryGetValue(name, out var service);
        return service;
    }
    
    /// <summary>
    /// Checks if a service exists.
    /// </summary>
    public bool HasService(string name) => _servicesByName.ContainsKey(name);

    /// <summary>
    /// Gets all cached views.
    /// </summary>
    public IReadOnlyCollection<BmView> Views => _viewsByName.Values;

    /// <summary>
    /// Gets all view names.
    /// </summary>
    public IEnumerable<string> ViewNames => _viewsByName.Keys;

    /// <summary>
    /// Gets view by name (case-insensitive).
    /// </summary>
    public BmView? GetView(string name)
    {
        _viewsByName.TryGetValue(name, out var view);
        return view;
    }

    /// <summary>
    /// Checks if a view exists.
    /// </summary>
    public bool HasView(string name) => _viewsByName.ContainsKey(name);

    /// <summary>
    /// Gets all cached sequences.
    /// </summary>
    public IReadOnlyCollection<BmSequence> Sequences => _sequencesByName.Values;

    /// <summary>
    /// Gets all sequence names.
    /// </summary>
    public IEnumerable<string> SequenceNames => _sequencesByName.Keys;

    /// <summary>
    /// Gets sequence by name (case-insensitive).
    /// </summary>
    public BmSequence? GetSequence(string name)
    {
        _sequencesByName.TryGetValue(name, out var sequence);
        return sequence;
    }

    /// <summary>
    /// Checks if a sequence exists.
    /// </summary>
    public bool HasSequence(string name) => _sequencesByName.ContainsKey(name);

    /// <summary>
    /// Gets all cached events.
    /// </summary>
    public IReadOnlyCollection<BmEvent> Events => _eventsByName.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets all event names.
    /// </summary>
    public IEnumerable<string> EventNames => _eventsByName.Keys;

    /// <summary>
    /// Gets event by name (case-insensitive).
    /// </summary>
    public BmEvent? GetEvent(string name)
    {
        _eventsByName.TryGetValue(name, out var ev);
        return ev;
    }

    /// <summary>
    /// Checks if an event exists.
    /// </summary>
    public bool HasEvent(string name) => _eventsByName.ContainsKey(name);

    /// <summary>
    /// Adds an event to the cache (for manual building).
    /// </summary>
    public void AddEvent(BmEvent @event)
    {
        _eventsByName[@event.Name] = @event;
    }

    /// <summary>
    /// Gets all rules for an entity.
    /// </summary>
    /// <param name="entityName">Entity name (simple or qualified).</param>
    /// <returns>List of rules targeting the entity.</returns>
    public IReadOnlyList<BmRule> GetRulesForEntity(string entityName)
    {
        // Try exact match first (works for both qualified and simple names)
        if (_rulesByEntity.TryGetValue(entityName, out var rules))
            return rules;

        // If caller passed qualified name, try matching by simple name
        // but ONLY return rules whose target entity resolves to the same qualified name
        var simpleName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
        if (_rulesByEntity.TryGetValue(simpleName, out var simpleRules))
        {
            // Filter to only rules that actually belong to this entity
            // by checking the target entity resolves to the same qualified name
            var entity = _entitiesByQualifiedName.GetValueOrDefault(entityName)
                      ?? _entitiesByName.GetValueOrDefault(entityName);
            if (entity != null)
            {
                var filtered = simpleRules
                    .Where(r =>
                    {
                        // Rule target matches entity by simple name, qualified name, or direct name
                        var targetEntity = _entitiesByName.GetValueOrDefault(r.TargetEntity)
                                        ?? _entitiesByQualifiedName.GetValueOrDefault(r.TargetEntity);
                        return targetEntity == entity;
                    })
                    .ToList();
                if (filtered.Count > 0)
                    return filtered;
            }
            // Fallback: return all rules under the simple name (backwards compatible)
            return simpleRules;
        }

        return Array.Empty<BmRule>();
    }

    /// <summary>
    /// Gets all access controls for an entity.
    /// </summary>
    public IReadOnlyList<BmAccessControl> GetAccessControlsForEntity(string entityName)
    {
        if (_accessControlsByEntity.TryGetValue(entityName, out var acls))
            return acls;

        var simpleName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
        if (_accessControlsByEntity.TryGetValue(simpleName, out acls))
            return acls;

        return Array.Empty<BmAccessControl>();
    }

    /// <summary>
    /// Gets all incoming references (associations/compositions from other entities) that target the given entity.
    /// Used for referential integrity checks: pre-delete constraint validation and cascade operations.
    /// </summary>
    public IReadOnlyList<IncomingReference> GetIncomingReferences(string entityName)
    {
        if (_incomingReferences.TryGetValue(entityName, out var refs))
            return refs;

        var simpleName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
        if (_incomingReferences.TryGetValue(simpleName, out refs))
            return refs;

        return Array.Empty<IncomingReference>();
    }

    // ============================================================
    // Inheritance Helpers
    // ============================================================
    
    /// <summary>
    /// Get all derived entities for a given entity name.
    /// </summary>
    public List<BmEntity> GetDerivedEntities(string entityName)
    {
        var entity = GetEntity(entityName);
        if (entity == null) return new List<BmEntity>();
        
        var all = new List<BmEntity>();
        CollectAllDerivedEntities(entity, all);
        return all;
    }

    private void CollectAllDerivedEntities(BmEntity entity, List<BmEntity> result)
    {
        foreach (var child in entity.DerivedEntities)
        {
            result.Add(child);
            CollectAllDerivedEntities(child, result);
        }
    }
    
    /// <summary>
    /// Get all fields including inherited ones from parent entity chain.
    /// </summary>
    public List<BmField> GetAllFields(string entityName)
    {
        var entity = GetEntity(entityName);
        if (entity == null) return new List<BmField>();
        
        var allFields = new List<BmField>();
        CollectAllFields(entity, allFields, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return allFields;
    }
    
    private void CollectAllFields(BmEntity entity, List<BmField> fields, HashSet<string> seen)
    {
        // Add parent fields first
        if (entity.ParentEntity != null)
        {
            CollectAllFields(entity.ParentEntity, fields, seen);
        }
        
        // Add own fields
        foreach (var field in entity.Fields)
        {
            if (seen.Add(field.Name))
                fields.Add(field);
        }
    }
    
    /// <summary>
    /// Check if an entity is abstract.
    /// </summary>
    public bool IsAbstract(string entityName)
    {
        var entity = GetEntity(entityName);
        return entity?.IsAbstract ?? false;
    }
}
