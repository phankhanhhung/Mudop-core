using BMMDL.MetaModel;
using BMMDL.Runtime;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Plugins.Contexts;
using BMMDL.Runtime.Plugins.Loading;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 6.1: Feature Contribution
/// Invokes all <see cref="IFeatureMetadataContributor"/> plugins to populate
/// entity.Features[] during compilation.
///
/// Runs after OptimizationPass (Order 60) because feature contributors read
/// inlined aspect fields (e.g., TenantIsolation checks entity.TenantScoped,
/// Temporal reads entity.IsTemporal — both set via aspect annotations that
/// are already resolved after optimization).
///
/// The pass uses a <see cref="PlatformFeatureRegistry"/> (injected or auto-discovered),
/// iterates entities in the model, and for each applicable contributor calls
/// <see cref="IFeatureMetadataContributor.ContributeMetadata"/>.
///
/// Contributors run in dependency-resolved order (topological sort with Stage tiebreaker),
/// ensuring cross-plugin metadata access works correctly.
///
/// Feature discovery: when no registry is provided, features are discovered via
/// <see cref="FeatureDiscovery"/> by scanning the BMMDL.Runtime assembly for
/// <see cref="IPlatformFeature"/> implementations. Built-in and external features
/// use the same discovery mechanism.
/// </summary>
public class FeatureContributionPass : ICompilerPass
{
    public string Name => "Feature Contribution";
    public string Description => "Platform features contribute metadata to entities";
    public int Order => 61; // After OptimizationPass (60)

    private readonly PlatformFeatureRegistry? _registry;

    /// <summary>
    /// Creates a pass that auto-discovers features from the BMMDL.Runtime assembly.
    /// </summary>
    public FeatureContributionPass()
    {
        _registry = null; // Will auto-discover
    }

    /// <summary>
    /// Creates a pass using a pre-configured feature registry.
    /// Use this when the registry is already available from DI (includes external plugins).
    /// </summary>
    public FeatureContributionPass(PlatformFeatureRegistry registry)
    {
        _registry = registry;
    }

    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.FEAT_NO_MODEL,
                "No model available for feature contribution", pass: Name);
            return false;
        }

        var registry = _registry ?? CreateDiscoveredRegistry();
        var contributors = registry.MetadataContributors;

        if (contributors.Count == 0)
        {
            context.AddInfo(ErrorCodes.FEAT_NO_CONTRIBUTORS,
                "No feature metadata contributors registered", Name);
            return true;
        }

        // Build a temporary MetaModelCache from the compiled model
        // so contributors can use efficient lookups
        var cache = new MetaModelCache(context.Model);
        var featureCtx = new FeatureContributionContext(context.Model, cache);

        int entitiesProcessed = 0;
        int contributionsApplied = 0;

        foreach (var entity in context.Model.Entities)
        {
            bool entityContributed = false;

            foreach (var contributor in contributors)
            {
                // Check if feature applies to this entity — either via entity-level
                // annotation or via global feature activation
                if (!contributor.AppliesTo(entity))
                {
                    // Global feature: applies to ALL entities when activated
                    if (!registry.IsFeatureGloballyActive(contributor.Name))
                        continue;

                    // Even in global mode, some entities may be excluded
                    if (contributor is IGlobalFeature global && global.ShouldExcludeFromGlobal(entity))
                        continue;
                }

                try
                {
                    contributor.ContributeMetadata(entity, featureCtx);
                    contributionsApplied++;
                    entityContributed = true;
                }
                catch (Exception ex)
                {
                    context.AddError(ErrorCodes.FEAT_CONTRIBUTOR_ERROR,
                        $"Feature '{contributor.Name}' failed for entity '{entity.QualifiedName}': {ex.Message}",
                        pass: Name);
                }
            }

            if (entityContributed)
                entitiesProcessed++;
        }

        // Propagate feature diagnostics to compilation context
        foreach (var diag in featureCtx.Diagnostics)
        {
            switch (diag.Severity)
            {
                case FeatureDiagnosticSeverity.Error:
                    context.AddError(diag.Code, diag.Message, pass: Name);
                    break;
                case FeatureDiagnosticSeverity.Warning:
                    context.AddWarning(diag.Code, diag.Message, pass: Name);
                    break;
                case FeatureDiagnosticSeverity.Info:
                    context.AddInfo(diag.Code, diag.Message, Name);
                    break;
            }
        }

        // Store metrics
        context.FeatureContributionsApplied = contributionsApplied;
        context.FeatureEntitiesProcessed = entitiesProcessed;

        context.AddInfo(ErrorCodes.FEAT_SUMMARY,
            $"Applied {contributionsApplied} feature contributions across {entitiesProcessed} entities " +
            $"({contributors.Count} contributors registered)", Name);

        return !featureCtx.HasErrors;
    }

    /// <summary>
    /// Creates a PlatformFeatureRegistry by discovering all IPlatformFeature implementations
    /// in the BMMDL.Runtime assembly. No hardcoded list — features register themselves
    /// by implementing IPlatformFeature with a parameterless constructor.
    ///
    /// Public for testing and extension scenarios.
    /// </summary>
    public static PlatformFeatureRegistry CreateDiscoveredRegistry()
    {
        return FeatureDiscovery.CreateRegistry();
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="CreateDiscoveredRegistry"/>.
    /// </summary>
    public static PlatformFeatureRegistry CreateDefaultRegistry()
    {
        return CreateDiscoveredRegistry();
    }
}
