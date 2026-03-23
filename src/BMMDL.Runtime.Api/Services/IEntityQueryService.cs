namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.DataAccess;

/// <summary>
/// Interface for entity query operations: List, GetById, expand helpers, computed field evaluation.
/// </summary>
public interface IEntityQueryService
{
    /// <summary>
    /// Execute the main query for a list of entities. Handles $expand, $apply, polymorphic, inheritance.
    /// </summary>
    Task<(List<Dictionary<string, object?>> Items, List<string> ExpandedNavs)> ExecuteListQueryAsync(
        BmEntity entityDef, QueryOptions options,
        Dictionary<string, ExpandOptions>? expandOptions,
        string? apply,
        CancellationToken ct);

    /// <summary>
    /// Expand OneToMany/ManyToMany and recursive navigations for a list of items.
    /// </summary>
    Task ExpandNavigationsAsync(
        BmEntity entityDef,
        List<Dictionary<string, object?>> items,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct);

    /// <summary>
    /// Expand OneToMany navigations for a single item.
    /// </summary>
    Task ExpandNavigationsForSingleAsync(
        BmEntity entityDef,
        Dictionary<string, object?> result,
        Dictionary<string, ExpandOptions> expandOptions,
        Guid? tenantId,
        CancellationToken ct);

    /// <summary>
    /// Get total count for pagination.
    /// </summary>
    Task<int> GetCountAsync(BmEntity entityDef, QueryOptions options, CancellationToken ct);

    /// <summary>
    /// Execute GetById query with optional $expand.
    /// </summary>
    Task<Dictionary<string, object?>?> GetByIdAsync(
        BmEntity entityDef, Guid id, QueryOptions options,
        Dictionary<string, ExpandOptions>? expandOptions,
        CancellationToken ct);

    /// <summary>
    /// Evaluate application-level computed fields (Virtual/Application strategy) post-query.
    /// </summary>
    void EvaluateComputedFields(BmEntity entityDef, List<Dictionary<string, object?>> results);

    /// <summary>
    /// Apply field-level restrictions (hidden/masked fields) to a list of items.
    /// </summary>
    void ApplyFieldRestrictions(
        BmEntity entityDef,
        List<Dictionary<string, object?>> items,
        RequestContext context);

    /// <summary>
    /// Apply field-level restrictions to a single result.
    /// </summary>
    Dictionary<string, object?> ApplyFieldRestrictions(
        BmEntity entityDef,
        Dictionary<string, object?> result,
        RequestContext context);

    /// <summary>
    /// Find a OneToMany composition or association by navigation name.
    /// </summary>
    Task<BmEntity?> ResolveChildEntityAsync(BmAssociation assoc);
}
