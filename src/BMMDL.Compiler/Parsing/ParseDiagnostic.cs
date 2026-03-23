namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Diagnostic level for parse operations.
/// </summary>
public enum ParseDiagnosticLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a diagnostic message from parsing/model building.
/// </summary>
public record ParseDiagnostic(
    ParseDiagnosticLevel Level,
    string File,
    int Line,
    string Context,
    string Message
)
{
    public override string ToString() => 
        $"[{Level}] {Path.GetFileName(File)}:{Line} - {Context}: {Message}";
}
