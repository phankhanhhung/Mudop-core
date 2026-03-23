namespace BMMDL.Compiler.Parsing;

using BMMDL.Compiler;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared helper for adding parse diagnostics across builder classes.
/// </summary>
internal static class ParseDiagnosticHelper
{
    /// <summary>
    /// Add a parse warning. The model continues building even when AST parsing fails,
    /// because the string representation is preserved.
    /// </summary>
    public static void AddParseWarning(
        List<ParseDiagnostic> diagnostics,
        ILogger logger,
        int line,
        string context,
        string message,
        string? sourceFile = null)
    {
        var diagnostic = new ParseDiagnostic(
            ParseDiagnosticLevel.Warning,
            sourceFile ?? "unknown",
            line,
            context,
            message
        );
        diagnostics.Add(diagnostic);
        logger.LogWarning("[{File}:{Line}] {Context}: {Message}",
            Path.GetFileName(sourceFile ?? "unknown"), line, context, message);
    }

    /// <summary>
    /// Add a parse warning with exception details.
    /// </summary>
    public static void AddParseWarning(
        List<ParseDiagnostic> diagnostics,
        ILogger logger,
        int line,
        string context,
        string message,
        Exception ex,
        string? sourceFile = null)
    {
        // Include full exception details for debugging
        var fullMessage = $"{message}\nException: {ex}";
        var diagnostic = new ParseDiagnostic(
            ParseDiagnosticLevel.Warning,
            sourceFile ?? "unknown",
            line,
            context,
            fullMessage
        );
        diagnostics.Add(diagnostic);
        logger.LogWarning(ex, "[{File}:{Line}] {Context}: {Message}",
            Path.GetFileName(sourceFile ?? "unknown"), line, context, message);
    }
}
