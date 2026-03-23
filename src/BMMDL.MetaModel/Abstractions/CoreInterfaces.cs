namespace BMMDL.MetaModel.Abstractions;

/// <summary>
/// Base interface for all BMMDL model elements.
/// </summary>
public interface IModelElement
{
    // Location in source file (for error reporting)
    string? SourceFile { get; }
    int StartLine { get; }
    int EndLine { get; }
}

/// <summary>
/// Interface for elements that have a name.
/// </summary>
public interface INamedElement : IModelElement
{
    string Name { get; }
    string QualifiedName { get; }
}

/// <summary>
/// Interface for elements that can be annotated (decorated).
/// </summary>
public interface IAnnotatable : IModelElement
{
    List<BmAnnotation> Annotations { get; }
    
    // Helper to get annotation by name
    BmAnnotation? GetAnnotation(string name);
    bool HasAnnotation(string name);
}

public class BmAnnotation : INamedElement
{
    public string Name { get; }
    public string QualifiedName => Name; // Simplified
    public object? Value { get; }
    
    // Properties for object-like annotations
    public IReadOnlyDictionary<string, object?>? Properties { get; }

    public string? SourceFile { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public BmAnnotation(string name, object? value = null, IDictionary<string, object?>? properties = null)
    {
        Name = name;
        Value = value;
        Properties = properties != null 
            ? new Dictionary<string, object?>(properties).AsReadOnly() 
            : null;
    }
    
    /// <summary>
    /// Get a property value by name from this annotation.
    /// Returns null if the property doesn't exist or Properties is null.
    /// </summary>
    public object? GetValue(string propertyName)
    {
        if (Properties is null) return null;
        return Properties.TryGetValue(propertyName, out var value) ? value : null;
    }
}
