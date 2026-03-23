using BMMDL.MetaModel.Structure;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// A platform feature that participates across compile, DDL, DML, and API layers.
/// Each feature implements one or more capability interfaces.
///
/// Ordering: DependsOn (topological) -> Stage (numeric tiebreaker within tier).
/// Activation: AppliesTo() checked per-entity — annotation-driven.
/// </summary>
public interface IPlatformFeature
{
    /// <summary>
    /// Unique identifier. E.g., "TenantIsolation", "Temporal", "SoftDelete".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Features this one must run after. Resolved via topological sort.
    /// Circular dependencies rejected at startup.
    /// </summary>
    IReadOnlyList<string> DependsOn => [];

    /// <summary>
    /// Within the same dependency tier, lower stage runs first.
    /// </summary>
    int Stage => 0;

    /// <summary>
    /// Does this feature apply to the given entity?
    /// Typically checks annotations (e.g., @TenantScoped, @Temporal).
    /// </summary>
    bool AppliesTo(BmEntity entity);
}
