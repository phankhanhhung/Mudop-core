namespace BMMDL.Runtime.Extensions;

/// <summary>
/// Extension methods for dictionary operations common across the runtime.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// Retrieves the entity ID value from a result dictionary, checking common casing variants
    /// ("Id", "id", "ID") in order.
    /// </summary>
    public static object? GetIdValue(this IDictionary<string, object?> dict)
    {
        if (dict.TryGetValue("Id", out var val)) return val;
        if (dict.TryGetValue("id", out val)) return val;
        if (dict.TryGetValue("ID", out val)) return val;
        return null;
    }
}
