using BMMDL.Runtime.DataAccess;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Context for write lifecycle hooks (<see cref="IFeatureWriteHook"/>).
/// Provides access to the unit of work, tenant/user identity, and filter state.
/// </summary>
public class WriteContext
{
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>
    /// The active unit of work for the current request.
    /// Hooks can use this to enqueue events or access the transaction.
    /// </summary>
    public IUnitOfWork UnitOfWork { get; init; } = null!;

    /// <summary>
    /// The current feature filter state, allowing hooks to check or toggle filters.
    /// </summary>
    public IFeatureFilterState FilterState { get; init; } = null!;
}
