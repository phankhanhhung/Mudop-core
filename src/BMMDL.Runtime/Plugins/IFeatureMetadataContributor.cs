using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins.Contexts;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Contributes metadata to entities during compilation.
/// This is the PRIMARY extension point for DDL/DML behavior.
///
/// Pattern from: Hibernate MetadataContributor + Envers AuditMetadataGenerator.
/// Plugins enrich the model — generators read enriched model uniformly.
/// </summary>
public interface IFeatureMetadataContributor : IPlatformFeature
{
    /// <summary>
    /// Enrich the entity with feature-specific metadata (columns, constraints, indexes, etc.).
    /// Called once per entity during the FeatureContributionPass.
    /// </summary>
    void ContributeMetadata(BmEntity entity, FeatureContributionContext ctx);
}
