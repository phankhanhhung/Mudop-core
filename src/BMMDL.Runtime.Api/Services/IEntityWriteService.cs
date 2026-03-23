namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;

/// <summary>
/// Interface for entity write operations (Create, Update, Replace, Delete).
/// </summary>
public interface IEntityWriteService
{
    /// <summary>
    /// Create a new entity. Performs validation, rule execution, insert, event enqueue.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    Task<EntityOperationResult> CreateAsync(
        BmEntity entityDef, string module, string entity,
        Dictionary<string, object?> data,
        RequestContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Update an existing entity (PATCH). Performs validation, rule execution, update, event enqueue.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    Task<EntityOperationResult> UpdateAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        Dictionary<string, object?> data,
        RequestContext context,
        string? ifMatch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Full replace of an entity (PUT). Fills missing fields with defaults.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    Task<EntityOperationResult> ReplaceAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        Dictionary<string, object?> data,
        RequestContext context,
        string? ifMatch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete an entity. Performs referential integrity checks, rule execution, cascade, delete.
    /// Caller must manage UnitOfWork lifecycle.
    /// </summary>
    Task<EntityOperationResult> DeleteAsync(
        BmEntity entityDef, string module, string entity, Guid id,
        RequestContext context,
        bool soft = false,
        string? ifMatch = null,
        CancellationToken ct = default);
}
