namespace BMMDL.Runtime.Validation;

/// <summary>
/// Validates that parameter values match their declared types.
/// Used by both ODataServiceController and EntityActionController.
/// </summary>
public static class ParameterTypeValidator
{
    /// <summary>
    /// Validate that a parameter value matches the declared type.
    /// Returns an error message if invalid, null if valid.
    /// </summary>
    public static string? ValidateParameterType(string paramName, string declaredType, object value)
    {
        var valueStr = value.ToString() ?? "";
        var upperType = declaredType.ToUpperInvariant();

        // Strip length/precision info (e.g., "String(100)" → "STRING")
        var parenIdx = upperType.IndexOf('(');
        if (parenIdx > 0) upperType = upperType[..parenIdx];

        var isValid = upperType switch
        {
            "UUID" or "GUID" => value is Guid || Guid.TryParse(valueStr, out _),
            "INTEGER" or "INT" => value is int or long or short or byte || int.TryParse(valueStr, out _),
            "LONG" or "BIGINT" or "INT64" => value is long or int || long.TryParse(valueStr, out _),
            "DECIMAL" or "MONEY" or "DOUBLE" or "FLOAT" =>
                value is decimal or double or float or int or long || decimal.TryParse(valueStr, out _),
            "BOOLEAN" or "BOOL" => value is bool || bool.TryParse(valueStr, out _),
            "DATE" => value is DateTime or DateOnly || DateTime.TryParse(valueStr, out _),
            "DATETIME" or "TIMESTAMP" or "TIMESTAMPTZ" =>
                value is DateTime or DateTimeOffset || DateTime.TryParse(valueStr, out _),
            "STRING" or "VARCHAR" or "TEXT" => true, // Any value is valid for string
            _ => true // Unknown types pass through
        };

        if (!isValid)
        {
            return $"Parameter '{paramName}' expects type '{declaredType}' but received '{valueStr}'";
        }

        return null;
    }
}
