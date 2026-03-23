namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// Formats diagnostics with rich source code context.
/// </summary>
public class RichDiagnosticFormatter
{
    private readonly Dictionary<string, string[]> _sourceCache = new();
    private readonly bool _useColors;
    
    public RichDiagnosticFormatter(bool useColors = true)
    {
        _useColors = useColors;
    }
    
    /// <summary>
    /// Format a diagnostic with source code context.
    /// </summary>
    public string Format(CompilationDiagnostic diag)
    {
        var sb = new System.Text.StringBuilder();
        
        // Header line with severity icon
        var icon = diag.Severity switch
        {
            DiagnosticSeverity.Error => "❌",
            DiagnosticSeverity.Warning => "⚠️",
            _ => "ℹ️"
        };
        
        var color = diag.Severity switch
        {
            DiagnosticSeverity.Error => ConsoleColor.Red,
            DiagnosticSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Cyan
        };
        
        sb.AppendLine($"{icon} {diag.Code}: {diag.Message}");
        
        // Source location
        if (!string.IsNullOrEmpty(diag.SourceFile) && diag.Line.HasValue)
        {
            var fileName = Path.GetFileName(diag.SourceFile);
            sb.AppendLine($"   ╭─ {fileName}:{diag.Line}");
            
            // Get source lines
            var lines = GetSourceLines(diag.SourceFile);
            if (lines != null && diag.Line.Value > 0 && diag.Line.Value <= lines.Length)
            {
                int lineNum = diag.Line.Value;
                int startLine = Math.Max(1, lineNum - 1);
                int endLine = Math.Min(lines.Length, lineNum + 1);
                
                for (int i = startLine; i <= endLine; i++)
                {
                    var lineContent = lines[i - 1];
                    var prefix = i == lineNum ? " → " : "   ";
                    var lineStr = $"   │ {i,4} │{prefix}{lineContent}";
                    sb.AppendLine(lineStr);
                    
                    // Add underline for the error line
                    if (i == lineNum && diag.Column.HasValue)
                    {
                        var underline = new string(' ', 12 + prefix.Length + Math.Max(0, diag.Column.Value - 1));
                        var highlightLength = Math.Min(10, lineContent.Length - diag.Column.Value + 1);
                        underline += new string('^', Math.Max(1, highlightLength));
                        sb.AppendLine($"   │      │   {underline}");
                    }
                }
            }
            
            sb.AppendLine("   ╰───────────────────────────────────────────────");
        }
        
        // Suggestion if available
        var suggestion = GetSuggestion(diag);
        if (!string.IsNullOrEmpty(suggestion))
        {
            sb.AppendLine($"   💡 {suggestion}");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Format all diagnostics grouped by severity.
    /// </summary>
    public string FormatAll(IEnumerable<CompilationDiagnostic> diagnostics)
    {
        var sb = new System.Text.StringBuilder();
        
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        var infos = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Info).ToList();
        
        if (errors.Any())
        {
            sb.AppendLine();
            sb.AppendLine($"─── Errors ({errors.Count}) ───");
            foreach (var error in errors.Take(10))
            {
                sb.AppendLine(Format(error));
            }
            if (errors.Count > 10)
            {
                sb.AppendLine($"   ... and {errors.Count - 10} more errors");
            }
        }
        
        if (warnings.Any())
        {
            sb.AppendLine();
            sb.AppendLine($"─── Warnings ({warnings.Count}) ───");
            foreach (var warning in warnings.Take(5))
            {
                sb.AppendLine(Format(warning));
            }
            if (warnings.Count > 5)
            {
                sb.AppendLine($"   ... and {warnings.Count - 5} more warnings");
            }
        }
        
        return sb.ToString();
    }
    
    private string[]? GetSourceLines(string filePath)
    {
        if (_sourceCache.TryGetValue(filePath, out var cached))
            return cached;
        
        try
        {
            if (File.Exists(filePath))
            {
                var lines = File.ReadAllLines(filePath);
                _sourceCache[filePath] = lines;
                return lines;
            }
        }
        catch (IOException)
        {
            // Intentionally silent - source context is optional for diagnostics formatting.
            // If the file can't be read, we just skip showing the source snippet.
        }
        
        return null;
    }
    
    private string? GetSuggestion(CompilationDiagnostic diag)
    {
        return diag.Code switch
        {
            ErrorCodes.SEM_DUPLICATE_FIELD => "Rename one of the duplicate fields",
            ErrorCodes.SEM_ENTITY_NO_KEY => "Add a 'key' modifier to one field, e.g., 'key ID: UUID;'",
            ErrorCodes.SEM_ENTITY_NO_FIELDS => "Add at least one field to the entity",
            ErrorCodes.SYM_UNRESOLVED_REF => "Check spelling or add the missing type definition",
            ErrorCodes.DEP_CIRCULAR_ENTITY => "Refactor to break the circular dependency",
            "TYP010" => "Update the field type to match the expression result",
            _ => null
        };
    }
}
