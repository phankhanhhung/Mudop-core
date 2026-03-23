namespace BMMDL.Runtime.Plugins.Staging;

/// <summary>
/// Severity level for a validation check result.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational message — does not block installation.</summary>
    Info,
    /// <summary>Warning — installation allowed but admin should review.</summary>
    Warning,
    /// <summary>Error — blocks installation until resolved.</summary>
    Error
}

/// <summary>
/// Result from a single validation check in the <see cref="PluginValidationPipeline"/>.
/// </summary>
public sealed record ValidationCheckResult
{
    /// <summary>Name of the validation check (e.g., "ManifestCheck", "AssemblyCheck").</summary>
    public required string CheckName { get; init; }

    /// <summary>Whether the check passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Severity level.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>Human-readable message describing the result.</summary>
    public required string Message { get; init; }

    /// <summary>Optional detailed information (e.g., stack trace for assembly load failures).</summary>
    public string? Details { get; init; }

    /// <summary>Create a passing check result.</summary>
    public static ValidationCheckResult Pass(string checkName, string message) => new()
    {
        CheckName = checkName,
        Passed = true,
        Severity = ValidationSeverity.Info,
        Message = message
    };

    /// <summary>Create a warning check result (non-blocking).</summary>
    public static ValidationCheckResult Warn(string checkName, string message, string? details = null) => new()
    {
        CheckName = checkName,
        Passed = true,
        Severity = ValidationSeverity.Warning,
        Message = message,
        Details = details
    };

    /// <summary>Create a failing check result (blocking).</summary>
    public static ValidationCheckResult Fail(string checkName, string message, string? details = null) => new()
    {
        CheckName = checkName,
        Passed = false,
        Severity = ValidationSeverity.Error,
        Message = message,
        Details = details
    };
}
