using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Authorization;
using Microsoft.AspNetCore.Http;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Context for an entity CRUD operation flowing through the behavior pipeline.
/// Created by the controller and passed through all <see cref="IEntityOperationBehavior"/> behaviors.
/// </summary>
public class EntityOperationContext
{
    /// <summary>
    /// The entity definition being operated on.
    /// </summary>
    public required BmEntity Entity { get; init; }

    /// <summary>
    /// The CRUD operation being performed.
    /// </summary>
    public required CrudOperation Operation { get; init; }

    /// <summary>
    /// The entity data for create/update operations. May be modified by behaviors.
    /// </summary>
    public Dictionary<string, object?>? Data { get; set; }

    /// <summary>
    /// The existing entity data before update/delete. May be populated by behaviors.
    /// </summary>
    public Dictionary<string, object?>? OldData { get; set; }

    /// <summary>
    /// The ASP.NET Core HTTP context for the current request.
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Tenant identifier from the JWT claims.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// User identifier from the JWT claims.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The If-Match ETag header value for optimistic concurrency.
    /// </summary>
    public string? IfMatchETag { get; init; }

    /// <summary>
    /// The request locale (from Accept-Language header or query parameter).
    /// </summary>
    public string? Locale { get; init; }
}
