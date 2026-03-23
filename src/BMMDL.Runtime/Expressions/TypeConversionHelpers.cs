namespace BMMDL.Runtime.Expressions;

/// <summary>
/// Provides shared type conversion utilities for runtime expression evaluation,
/// rule execution, authorization checks, and action execution.
/// </summary>
public static class TypeConversionHelpers
{
    /// <summary>
    /// Converts an arbitrary object value to a boolean using consistent semantics.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// - null → false
    /// - bool → as-is
    /// - string → false if null/empty/"false"/"0", true otherwise
    /// - numeric (int/long/decimal/double/float) → false if zero, true otherwise
    /// - other → true (non-null objects are truthy)
    /// </returns>
    public static bool ConvertToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0",
        int i => i != 0,
        long l => l != 0,
        decimal d => d != 0m,
        double d => d != 0.0,
        float f => f != 0f,
        _ => true
    };
}
