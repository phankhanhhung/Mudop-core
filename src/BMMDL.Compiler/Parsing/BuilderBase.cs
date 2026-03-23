using BMMDL.Compiler.Services;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Abstract base class for all parsing builders. Provides shared diagnostic fields
/// and AddParseWarning helper methods so each builder doesn't duplicate them.
/// Note: BmmdlModelBuilder cannot extend this because it already extends BmmdlParserBaseVisitor.
/// </summary>
public abstract class BuilderBase
{
    protected readonly string? _sourceFile;
    protected readonly List<ParseDiagnostic> _diagnostics;
    protected readonly ILogger _logger;

    protected BuilderBase(string? sourceFile, List<ParseDiagnostic> diagnostics, string loggerCategory)
    {
        _sourceFile = sourceFile;
        _diagnostics = diagnostics;
        _logger = CompilerLoggerFactory.CreateLogger(loggerCategory);
    }

    protected void AddParseWarning(int line, string context, string message)
    {
        ParseDiagnosticHelper.AddParseWarning(_diagnostics, _logger, line, context, message, _sourceFile);
    }

    protected void AddParseWarning(int line, string context, string message, Exception ex)
    {
        ParseDiagnosticHelper.AddParseWarning(_diagnostics, _logger, line, context, message, ex, _sourceFile);
    }
}
