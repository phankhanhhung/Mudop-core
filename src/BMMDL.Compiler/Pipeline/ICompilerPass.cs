namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// Represents a single pass in the multi-pass compilation pipeline.
/// </summary>
public interface ICompilerPass
{
    /// <summary>
    /// Display name of this pass (e.g., "Lexical Analysis")
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Short description of what this pass does
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Order of this pass in the pipeline (1-based)
    /// </summary>
    int Order { get; }
    
    /// <summary>
    /// Execute this pass against the compilation context.
    /// Returns true if the pass succeeded without critical errors.
    /// </summary>
    bool Execute(CompilationContext context);
}

/// <summary>
/// Represents the severity of a compilation diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single diagnostic (error/warning/info) from compilation.
/// </summary>
public record CompilationDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? SourceFile = null,
    int? Line = null,
    int? Column = null,
    string? PassName = null
)
{
    public override string ToString()
    {
        var location = SourceFile != null 
            ? $"{Path.GetFileName(SourceFile)}({Line},{Column})" 
            : "";
        var prefix = Severity switch
        {
            DiagnosticSeverity.Error => "❌",
            DiagnosticSeverity.Warning => "⚠️",
            _ => "ℹ️"
        };
        return $"{prefix} {Code}: {Message} {location}".Trim();
    }
}

/// <summary>
/// Statistics collected during a single pass.
/// </summary>
public class PassStatistics
{
    public string PassName { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int ItemsProcessed { get; set; }
    public Dictionary<string, object> Metrics { get; } = new();
    
    public void AddMetric(string key, object value) => Metrics[key] = value;
}
