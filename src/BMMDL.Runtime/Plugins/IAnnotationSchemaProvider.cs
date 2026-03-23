using BMMDL.MetaModel.Features;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Implemented by platform features that declare custom annotations.
/// The compiler validates annotations against these schemas at compile time.
/// </summary>
public interface IAnnotationSchemaProvider : IPlatformFeature
{
    /// <summary>
    /// The annotation schemas this plugin declares.
    /// Each schema defines a valid annotation name, its target, and its properties.
    /// </summary>
    IReadOnlyList<PluginAnnotationSchema> AnnotationSchemas { get; }
}
