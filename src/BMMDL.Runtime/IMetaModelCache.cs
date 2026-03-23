namespace BMMDL.Runtime;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;

/// <summary>
/// Interface for the in-memory meta-model cache.
/// Provides fast lookup of entities, fields, types, and related definitions.
/// </summary>
public interface IMetaModelCache
{
    /// <summary>
    /// Gets the underlying model.
    /// </summary>
    BmModel Model { get; }

    /// <summary>
    /// Gets all cached entities.
    /// </summary>
    IReadOnlyCollection<BmEntity> Entities { get; }

    /// <summary>
    /// Gets all cached types as a dictionary (case-insensitive lookup).
    /// </summary>
    IReadOnlyDictionary<string, BmType> Types { get; }

    /// <summary>
    /// Gets all cached enums as a dictionary (case-insensitive lookup).
    /// </summary>
    IReadOnlyDictionary<string, BmEnum> Enums { get; }

    /// <summary>
    /// Gets all cached services.
    /// </summary>
    IReadOnlyCollection<BmService> Services { get; }

    /// <summary>
    /// Gets all entity names.
    /// </summary>
    IEnumerable<string> EntityNames { get; }

    /// <summary>
    /// Gets all service names.
    /// </summary>
    IEnumerable<string> ServiceNames { get; }

    /// <summary>
    /// Gets all cached views.
    /// </summary>
    IReadOnlyCollection<BmView> Views { get; }

    /// <summary>
    /// Gets all view names.
    /// </summary>
    IEnumerable<string> ViewNames { get; }

    /// <summary>
    /// Gets all cached sequences.
    /// </summary>
    IReadOnlyCollection<BmSequence> Sequences { get; }

    /// <summary>
    /// Gets all sequence names.
    /// </summary>
    IEnumerable<string> SequenceNames { get; }

    /// <summary>
    /// Gets all cached events.
    /// </summary>
    IReadOnlyCollection<BmEvent> Events { get; }

    /// <summary>
    /// Gets all event names.
    /// </summary>
    IEnumerable<string> EventNames { get; }

    /// <summary>
    /// Gets entity by name (case-insensitive).
    /// </summary>
    BmEntity? GetEntity(string name);

    /// <summary>
    /// Gets entity by qualified name.
    /// </summary>
    BmEntity? GetEntityByQualifiedName(string qualifiedName);

    /// <summary>
    /// Gets type definition by name.
    /// </summary>
    BmType? GetType(string name);

    /// <summary>
    /// Gets enum definition by name.
    /// </summary>
    BmEnum? GetEnum(string name);

    /// <summary>
    /// Checks if an entity exists.
    /// </summary>
    bool HasEntity(string name);

    /// <summary>
    /// Gets service by name (case-insensitive).
    /// </summary>
    BmService? GetService(string name);

    /// <summary>
    /// Checks if a service exists.
    /// </summary>
    bool HasService(string name);

    /// <summary>
    /// Gets view by name (case-insensitive).
    /// </summary>
    BmView? GetView(string name);

    /// <summary>
    /// Checks if a view exists.
    /// </summary>
    bool HasView(string name);

    /// <summary>
    /// Gets sequence by name (case-insensitive).
    /// </summary>
    BmSequence? GetSequence(string name);

    /// <summary>
    /// Checks if a sequence exists.
    /// </summary>
    bool HasSequence(string name);

    /// <summary>
    /// Gets event by name (case-insensitive).
    /// </summary>
    BmEvent? GetEvent(string name);

    /// <summary>
    /// Checks if an event exists.
    /// </summary>
    bool HasEvent(string name);

    /// <summary>
    /// Adds an event to the cache (for manual building).
    /// </summary>
    void AddEvent(BmEvent @event);

    /// <summary>
    /// Gets all rules for an entity.
    /// </summary>
    IReadOnlyList<BmRule> GetRulesForEntity(string entityName);

    /// <summary>
    /// Gets all access controls for an entity.
    /// </summary>
    IReadOnlyList<BmAccessControl> GetAccessControlsForEntity(string entityName);

    /// <summary>
    /// Gets all incoming references that target the given entity.
    /// </summary>
    IReadOnlyList<IncomingReference> GetIncomingReferences(string entityName);

    /// <summary>
    /// Get all derived entities for a given entity name.
    /// </summary>
    List<BmEntity> GetDerivedEntities(string entityName);

    /// <summary>
    /// Get all fields including inherited ones from parent entity chain.
    /// </summary>
    List<BmField> GetAllFields(string entityName);

    /// <summary>
    /// Check if an entity is abstract.
    /// </summary>
    bool IsAbstract(string entityName);
}
