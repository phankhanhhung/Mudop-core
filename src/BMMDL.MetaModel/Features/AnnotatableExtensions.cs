using BMMDL.MetaModel.Abstractions;

namespace BMMDL.MetaModel.Features;

/// <summary>
/// Extension methods for reading plugin annotations from annotatable model elements.
/// Supports both dotted (@Plugin.Key: value) and structured (@Plugin { key: value }) styles.
/// </summary>
public static class AnnotatableExtensions
{
    /// <summary>
    /// Collect all properties for a plugin annotation prefix, merging from both styles.
    /// For dotted style: @Sequence.Name: 'X', @Sequence.Pattern: '{seq}' → { Name: "X", Pattern: "{seq}" }
    /// For structured style: @Sequence { name: 'X', pattern: '{seq}' } → { name: "X", pattern: "{seq}" }
    /// Both styles are merged into a single case-insensitive dictionary.
    /// </summary>
    public static Dictionary<string, object?> GetPluginAnnotations(this IAnnotatable target, string prefix)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dotPrefix = prefix + ".";

        foreach (var ann in target.Annotations)
        {
            // Structured: @Prefix { key: value } or @Prefix(value) { key: value }
            if (string.Equals(ann.Name, prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (ann.Properties is not null)
                {
                    foreach (var (key, value) in ann.Properties)
                        result.TryAdd(key, value);
                }

                // Also capture the single value if present (stored under "_value" key)
                if (ann.Value is not null)
                    result.TryAdd("_value", ann.Value);
            }
            // Dotted: @Prefix.Key: value
            else if (ann.Name.StartsWith(dotPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = ann.Name[dotPrefix.Length..];
                result.TryAdd(key, ann.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Get a typed property value from a plugin annotation.
    /// Searches both dotted (@Plugin.Key: value) and structured (@Plugin { key: value }) forms.
    /// </summary>
    public static T? GetPluginAnnotationValue<T>(this IAnnotatable target, string prefix, string property)
    {
        var dotName = $"{prefix}.{property}";

        // Check dotted form first: @Prefix.Property: value
        var dotted = target.GetAnnotation(dotName);
        if (dotted?.Value is not null)
            return ConvertValue<T>(dotted.Value);

        // Check structured form: @Prefix { property: value }
        var structured = target.GetAnnotation(prefix);
        if (structured?.Properties is not null)
        {
            // Case-insensitive property lookup
            foreach (var (key, value) in structured.Properties)
            {
                if (string.Equals(key, property, StringComparison.OrdinalIgnoreCase) && value is not null)
                    return ConvertValue<T>(value);
            }
        }

        return default;
    }

    /// <summary>
    /// Check whether a plugin annotation (or any of its dotted sub-annotations) is present.
    /// </summary>
    public static bool HasPluginAnnotation(this IAnnotatable target, string prefix)
    {
        var dotPrefix = prefix + ".";
        return target.Annotations.Exists(a =>
            string.Equals(a.Name, prefix, StringComparison.OrdinalIgnoreCase) ||
            a.Name.StartsWith(dotPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static T? ConvertValue<T>(object value)
    {
        if (value is T typed)
            return typed;

        try
        {
            // Handle common conversions: int→long, string→enum, etc.
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            var converted = Convert.ChangeType(value, targetType);
            return (T)converted;
        }
        catch
        {
            return default;
        }
    }
}
