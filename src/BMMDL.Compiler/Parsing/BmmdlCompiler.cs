using Antlr4.Runtime;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;

namespace BMMDL.Compiler.Parsing;

/// <summary>
/// Compiles BMMDL source code into a BmModel.
/// </summary>
public class BmmdlCompiler
{
    /// <summary>
    /// Compiles BMMDL source from a string.
    /// </summary>
    public BmModel Compile(string source, string? fileName = null)
    {
        var inputStream = new AntlrInputStream(source);
        return CompileInternal(inputStream, fileName);
    }

    /// <summary>
    /// Compiles BMMDL source from a file.
    /// </summary>
    public BmModel CompileFile(string filePath)
    {
        var source = File.ReadAllText(filePath);
        return Compile(source, filePath);
    }

    /// <summary>
    /// Compiles multiple BMMDL files into a single model.
    /// </summary>
    public BmModel CompileFiles(IEnumerable<string> filePaths)
    {
        var model = new BmModel();
        
        foreach (var filePath in filePaths)
        {
            var partialModel = CompileFile(filePath);
            model.Merge(partialModel);
        }
        
        return model;
    }

    private BmModel CompileInternal(ICharStream inputStream, string? fileName)
    {
        // Lexing
        var lexer = new BmmdlLexer(inputStream);
        var errorListener = new BmmdlErrorListener(fileName);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        // Parsing
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new BmmdlParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        // Parse
        var tree = parser.compilationUnit();

        if (errorListener.HasErrors)
        {
            throw new BmmdlCompilationException(errorListener.Errors);
        }

        // Build model
        var visitor = new BmmdlModelBuilder(fileName);
        return visitor.Visit(tree);
    }
}

/// <summary>
/// Represents compilation errors.
/// </summary>
public class BmmdlCompilationException : Exception
{
    public IReadOnlyList<BmmdlError> Errors { get; }

    public BmmdlCompilationException(IEnumerable<BmmdlError> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors.ToList();
    }
    
    private static string FormatMessage(IEnumerable<BmmdlError> errors)
    {
        var errorList = errors.ToList();
        var details = string.Join("\n  ", errorList.Select(e => e.ToString()));
        return $"Compilation failed with {errorList.Count} error(s):\n  {details}";
    }
}

/// <summary>
/// Represents a single compilation error.
/// </summary>
public record BmmdlError(
    string? FileName,
    int Line,
    int Column,
    string Message
)
{
    public override string ToString() =>
        $"{FileName ?? "<unknown>"}({Line},{Column}): {Message}";
}

/// <summary>
/// Custom error listener for ANTLR.
/// </summary>
public class BmmdlErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
{
    private readonly string? _fileName;
    private readonly List<BmmdlError> _errors = new();

    public IReadOnlyList<BmmdlError> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public BmmdlErrorListener(string? fileName)
    {
        _fileName = fileName;
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _errors.Add(new BmmdlError(_fileName, line, charPositionInLine, msg));
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _errors.Add(new BmmdlError(_fileName, line, charPositionInLine, msg));
    }
}
