using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Features;
using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Plugins;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 6.2: Plugin Annotation Validation
/// Validates annotations on entities, fields, etc. against schemas declared
/// by <see cref="IAnnotationSchemaProvider"/> plugins.
///
/// Runs after FeatureContributionPass (Order 61) so all plugins are registered.
///
/// Checks:
/// - Required properties are present (PANN002)
/// - Property value types match the schema (PANN003)
/// - No unknown properties (PANN004, warning)
/// - Annotation is on a valid target (PANN005)
/// - Property values are in the allowed set (PANN006)
/// </summary>
public class PluginAnnotationValidationPass : ICompilerPass
{
    public string Name => "Plugin Annotation Validation";
    public string Description => "Validate plugin annotations against declared schemas";
    public int Order => 62; // After FeatureContributionPass (61)

    private readonly PlatformFeatureRegistry? _registry;

    public PluginAnnotationValidationPass() => _registry = null;

    public PluginAnnotationValidationPass(PlatformFeatureRegistry registry)
        => _registry = registry;

    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.PANN_NO_MODEL,
                "No model available for plugin annotation validation", pass: Name);
            return false;
        }

        var registry = _registry ?? FeatureContributionPass.CreateDefaultRegistry();
        var schemas = CollectSchemas(registry);

        if (schemas.Count == 0)
            return true;

        int validated = 0;
        int errors = 0;

        // Validate entity-level annotations
        foreach (var entity in context.Model.Entities)
        {
            errors += ValidateAnnotations(entity, AnnotationTarget.Entity, schemas, context,
                $"entity '{entity.QualifiedName}'");
            validated++;

            // Validate field-level annotations
            foreach (var field in entity.Fields)
            {
                errors += ValidateAnnotations(field, AnnotationTarget.Field, schemas, context,
                    $"field '{entity.QualifiedName}.{field.Name}'");
                validated++;
            }

            // Validate association-level annotations
            foreach (var assoc in entity.Associations)
            {
                errors += ValidateAnnotations(assoc, AnnotationTarget.Association, schemas, context,
                    $"association '{entity.QualifiedName}.{assoc.Name}'");
                validated++;
            }
        }

        context.PluginAnnotationsValidated = validated;

        context.AddInfo(ErrorCodes.PANN_SUMMARY,
            $"Validated plugin annotations on {validated} elements ({errors} errors)", Name);

        return errors == 0;
    }

    /// <summary>
    /// Collect all annotation schemas from providers in the registry.
    /// Returns schemas indexed by annotation name (case-insensitive).
    /// </summary>
    private static Dictionary<string, PluginAnnotationSchema> CollectSchemas(PlatformFeatureRegistry registry)
    {
        var result = new Dictionary<string, PluginAnnotationSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in registry.AnnotationSchemaProviders)
        {
            foreach (var schema in provider.AnnotationSchemas)
                result.TryAdd(schema.Name, schema);
        }
        return result;
    }

    /// <summary>
    /// Validate all annotations on a single model element against known schemas.
    /// Returns the number of errors found.
    /// </summary>
    private int ValidateAnnotations(
        IAnnotatable element,
        AnnotationTarget elementTarget,
        Dictionary<string, PluginAnnotationSchema> schemas,
        CompilationContext context,
        string location)
    {
        int errorCount = 0;

        // Group annotations by plugin prefix for dotted style
        var processedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var annotation in element.Annotations)
        {
            // Determine which schema this annotation belongs to
            var (schema, prefix) = ResolveSchema(annotation.Name, schemas);
            if (schema == null)
                continue; // Not a plugin annotation — skip (built-in annotations have no schema)

            // Avoid re-validating the same prefix (dotted style produces multiple annotations)
            if (!processedPrefixes.Add(prefix))
                continue;

            // Check target validity
            if (!schema.Target.HasFlag(elementTarget))
            {
                context.AddError(ErrorCodes.PANN_WRONG_TARGET,
                    $"Annotation @{prefix} cannot be applied to {elementTarget.ToString().ToLowerInvariant()} " +
                    $"(allowed on: {schema.Target}) at {location}",
                    pass: Name);
                errorCount++;
                continue;
            }

            // Collect actual properties from the element (merging dotted + structured)
            var actualProps = CollectAnnotationProperties(element, schema, prefix);
            var isBareMarker = actualProps.Count == 0 && GetAnnotationValue(element, prefix) == null;

            // Bare marker check
            if (isBareMarker && !schema.AllowBareMarker && schema.Properties.Count > 0)
            {
                var required = schema.Properties.Where(p => p.Required).Select(p => p.Name);
                var requiredList = string.Join(", ", required);
                if (requiredList.Length > 0)
                {
                    context.AddError(ErrorCodes.PANN_MISSING_REQUIRED,
                        $"Annotation @{prefix} at {location} is missing required properties: {requiredList}",
                        pass: Name);
                    errorCount++;
                }
                continue;
            }

            // Validate properties against schema
            errorCount += ValidateProperties(schema, prefix, actualProps, context, location);
        }

        return errorCount;
    }

    /// <summary>
    /// Resolve an annotation name to its schema and canonical prefix.
    /// E.g., "Sequence.Name" → (SequenceSchema, "Sequence")
    /// E.g., "CDC" → (CdcSchema, "CDC")
    /// </summary>
    private static (PluginAnnotationSchema? schema, string prefix) ResolveSchema(
        string annotationName,
        Dictionary<string, PluginAnnotationSchema> schemas)
    {
        // Direct match: @CDC, @Workflow
        if (schemas.TryGetValue(annotationName, out var directSchema))
            return (directSchema, annotationName);

        // Dotted match: @Sequence.Name → find schema for "Sequence"
        var dotIndex = annotationName.IndexOf('.');
        if (dotIndex > 0)
        {
            var prefix = annotationName[..dotIndex];
            if (schemas.TryGetValue(prefix, out var dottedSchema))
                return (dottedSchema, prefix);
        }

        return (null, annotationName);
    }

    /// <summary>
    /// Collect all property values for a plugin annotation, merging from both styles.
    /// </summary>
    private static Dictionary<string, object?> CollectAnnotationProperties(
        IAnnotatable element,
        PluginAnnotationSchema schema,
        string prefix)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dotPrefix = prefix + ".";

        foreach (var ann in element.Annotations)
        {
            if (string.Equals(ann.Name, prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Structured: @Prefix { key: value }
                if (ann.Properties is not null)
                {
                    foreach (var (key, value) in ann.Properties)
                        result.TryAdd(key, value);
                }
            }
            else if (ann.Name.StartsWith(dotPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Dotted: @Prefix.Key: value
                var key = ann.Name[dotPrefix.Length..];
                result.TryAdd(key, ann.Value);
            }
        }

        return result;
    }

    private static object? GetAnnotationValue(IAnnotatable element, string prefix)
    {
        return element.GetAnnotation(prefix)?.Value;
    }

    /// <summary>
    /// Validate collected properties against the schema.
    /// Returns number of errors.
    /// </summary>
    private int ValidateProperties(
        PluginAnnotationSchema schema,
        string prefix,
        Dictionary<string, object?> actualProps,
        CompilationContext context,
        string location)
    {
        int errorCount = 0;

        // Check required properties
        foreach (var propSchema in schema.Properties)
        {
            if (!propSchema.Required)
                continue;

            var hasValue = actualProps.TryGetValue(propSchema.Name, out var val) && val is not null;
            if (!hasValue)
            {
                context.AddError(ErrorCodes.PANN_MISSING_REQUIRED,
                    $"Annotation @{prefix} at {location} is missing required property '{propSchema.Name}'",
                    pass: Name);
                errorCount++;
            }
        }

        // Validate each provided property
        var knownProps = new HashSet<string>(
            schema.Properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var (propName, propValue) in actualProps)
        {
            var propSchema = schema.Properties.Find(p =>
                string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));

            if (propSchema == null)
            {
                // Unknown property — warn, don't error (forward compatibility)
                context.AddWarning(ErrorCodes.PANN_UNKNOWN_PROPERTY,
                    $"Annotation @{prefix} at {location} has unknown property '{propName}'. " +
                    $"Known properties: {string.Join(", ", knownProps)}",
                    pass: Name);
                continue;
            }

            if (propValue is null)
                continue;

            // Type check
            if (!IsValueTypeCompatible(propValue, propSchema.Type))
            {
                context.AddError(ErrorCodes.PANN_TYPE_MISMATCH,
                    $"Annotation @{prefix}.{propSchema.Name} at {location}: " +
                    $"expected {propSchema.Type} but got {propValue.GetType().Name} ('{propValue}')",
                    pass: Name);
                errorCount++;
                continue;
            }

            // Allowed values check
            if (propSchema.AllowedValues is { Count: > 0 })
            {
                if (!propSchema.AllowedValues.Any(av => ValuesEqual(av, propValue)))
                {
                    context.AddError(ErrorCodes.PANN_VALUE_NOT_ALLOWED,
                        $"Annotation @{prefix}.{propSchema.Name} at {location}: " +
                        $"value '{propValue}' is not allowed. Allowed: {string.Join(", ", propSchema.AllowedValues)}",
                        pass: Name);
                    errorCount++;
                }
            }
        }

        return errorCount;
    }

    /// <summary>
    /// Check whether a runtime value is compatible with the declared schema type.
    /// </summary>
    private static bool IsValueTypeCompatible(object value, AnnotationValueType expectedType)
    {
        return expectedType switch
        {
            AnnotationValueType.Any => true,
            AnnotationValueType.String => value is string,
            AnnotationValueType.Integer => value is int or long,
            AnnotationValueType.Decimal => value is decimal or double or float or int,
            AnnotationValueType.Boolean => value is bool,
            AnnotationValueType.Enum => value is string s && s.StartsWith('#'),
            AnnotationValueType.Array => value is IList<object?>,
            AnnotationValueType.Object => value is IDictionary<string, object?>,
            _ => true
        };
    }

    private static bool ValuesEqual(object allowed, object actual)
    {
        if (allowed is string sa && actual is string sb)
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        return Equals(allowed, actual);
    }
}
