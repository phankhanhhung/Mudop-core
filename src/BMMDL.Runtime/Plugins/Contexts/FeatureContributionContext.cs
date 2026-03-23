using BMMDL.MetaModel;
using BMMDL.MetaModel.Features;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// Severity level for feature contribution diagnostics.
/// </summary>
public enum FeatureDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A diagnostic message emitted during feature metadata contribution.
/// </summary>
public record FeatureDiagnostic(
    FeatureDiagnosticSeverity Severity,
    string Code,
    string Message);

/// <summary>
/// Context passed to <see cref="IFeatureMetadataContributor"/> during compilation.
/// Provides access to the model, cache, and diagnostic reporting.
/// </summary>
public class FeatureContributionContext
{
    public FeatureContributionContext(BmModel model, IMetaModelCache cache)
    {
        Model = model;
        Cache = cache;
    }

    /// <summary>
    /// The complete model being compiled.
    /// </summary>
    public BmModel Model { get; }

    /// <summary>
    /// Cached model lookups for efficient entity/type resolution.
    /// </summary>
    public IMetaModelCache Cache { get; }

    /// <summary>
    /// Diagnostics collected during feature contribution.
    /// </summary>
    public List<FeatureDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// Whether any error-level diagnostics have been reported.
    /// </summary>
    public bool HasErrors => Diagnostics.Exists(d => d.Severity == FeatureDiagnosticSeverity.Error);

    /// <summary>
    /// Access metadata already contributed by dependency features.
    /// Enables cross-plugin interaction without direct coupling.
    /// </summary>
    public BmFeatureMetadata? GetFeatureMetadata(BmEntity entity, string featureName)
        => entity.Features.TryGetValue(featureName, out var meta) ? meta : null;

    /// <summary>
    /// Report an error diagnostic.
    /// </summary>
    public void ReportError(string code, string message)
        => Diagnostics.Add(new FeatureDiagnostic(FeatureDiagnosticSeverity.Error, code, message));

    /// <summary>
    /// Report a warning diagnostic.
    /// </summary>
    public void ReportWarning(string code, string message)
        => Diagnostics.Add(new FeatureDiagnostic(FeatureDiagnosticSeverity.Warning, code, message));
}
