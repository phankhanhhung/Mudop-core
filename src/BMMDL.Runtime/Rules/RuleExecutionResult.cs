namespace BMMDL.Runtime.Rules;

/// <summary>
/// Result of rule execution containing validation errors and computed values.
/// </summary>
public class RuleExecutionResult
{
    /// <summary>
    /// Whether all validations passed.
    /// </summary>
    public bool Success => Errors.Count == 0;

    /// <summary>
    /// Whether a reject statement was executed, signaling short-circuit of remaining statements.
    /// </summary>
    public bool Rejected { get; set; }
    
    /// <summary>
    /// Whether a return statement was executed, signaling the rule should stop and return a value.
    /// </summary>
    public bool ShouldReturn { get; set; }
    
    /// <summary>
    /// Value from a return statement, if any.
    /// </summary>
    public object? ReturnValue { get; set; }
    
    /// <summary>
    /// Validation errors encountered during rule execution.
    /// </summary>
    public List<ValidationError> Errors { get; } = new();
    
    /// <summary>
    /// Values computed by compute statements.
    /// Key is field name (PascalCase), value is computed result.
    /// </summary>
    public Dictionary<string, object?> ComputedValues { get; } = new();
    
    /// <summary>
    /// Event names emitted during statement execution.
    /// </summary>
    public List<string> EmittedEvents { get; } = new();

    /// <summary>
    /// Warnings (non-blocking) from validation rules.
    /// </summary>
    public List<ValidationError> Warnings { get; } = new();
    
    /// <summary>
    /// Info messages from validation rules.
    /// </summary>
    public List<ValidationError> Infos { get; } = new();
    
    /// <summary>
    /// Add a validation error.
    /// </summary>
    public void AddError(string field, string message, BmSeverity severity = BmSeverity.Error)
    {
        var error = new ValidationError(field, message, severity);
        
        switch (severity)
        {
            case BmSeverity.Error:
                Errors.Add(error);
                break;
            case BmSeverity.Warning:
                Warnings.Add(error);
                break;
            case BmSeverity.Info:
                Infos.Add(error);
                break;
        }
    }
    
    /// <summary>
    /// Add a computed value.
    /// </summary>
    public void SetComputedValue(string field, object? value)
    {
        ComputedValues[field] = value;
    }
    
    /// <summary>
    /// Merge another result into this one.
    /// </summary>
    public void Merge(RuleExecutionResult other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
        Infos.AddRange(other.Infos);
        EmittedEvents.AddRange(other.EmittedEvents);
        Rejected = Rejected || other.Rejected;
        if (other.ShouldReturn)
        {
            ShouldReturn = true;
            ReturnValue = other.ReturnValue;
        }

        foreach (var kvp in other.ComputedValues)
        {
            ComputedValues[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Create a successful result with no errors.
    /// </summary>
    public static RuleExecutionResult Ok() => new();
    
    /// <summary>
    /// Create a failed result with errors.
    /// </summary>
    public static RuleExecutionResult Fail(params ValidationError[] errors)
    {
        var result = new RuleExecutionResult();
        result.Errors.AddRange(errors);
        return result;
    }
}

/// <summary>
/// A validation error from rule execution.
/// </summary>
/// <param name="Field">Field name that failed validation, or empty for entity-level errors.</param>
/// <param name="Message">Human-readable error message.</param>
/// <param name="Severity">Error severity level.</param>
public record ValidationError(string Field, string Message, BmSeverity Severity = BmSeverity.Error);

/// <summary>
/// Severity levels for validation errors.
/// Matches BmSeverity in MetaModel but defined here to avoid circular dependency.
/// </summary>
public enum BmSeverity
{
    /// <summary>
    /// Blocking error - operation will be aborted.
    /// </summary>
    Error,
    
    /// <summary>
    /// Non-blocking warning - operation proceeds but user is notified.
    /// </summary>
    Warning,
    
    /// <summary>
    /// Informational message.
    /// </summary>
    Info
}
