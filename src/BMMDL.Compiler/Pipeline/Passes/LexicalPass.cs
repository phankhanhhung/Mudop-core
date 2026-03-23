using Antlr4.Runtime;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 1: Lexical Analysis
/// Tokenizes source files using ANTLR lexer (parallel).
/// </summary>
public class LexicalPass : ICompilerPass
{
    public string Name => "Lexical Analysis";
    public string Description => "Tokenize source files";
    public int Order => 1;
    
    public bool Execute(CompilationContext context)
    {
        int totalTokens = 0;
        bool success = true;
        object lockObj = new();

        // Parallel tokenization
        Parallel.ForEach(context.SourceFiles, file =>
        {
            try
            {
                // Check for in-memory source first, otherwise read from disk
                string source;
                if (context.SourceContents.TryGetValue(file, out var inMemorySource))
                {
                    source = inMemorySource;
                }
                else
                {
                    source = File.ReadAllText(file);
                }

                // Check compilation cache for unchanged files
                if (context.Cache != null)
                {
                    var sourceHash = CompilationCache.ComputeHash(source);
                    if (context.Cache.TryGetParseResult(file, sourceHash, out var cached) && cached != null)
                    {
                        // Cache hit: reuse cached token stream
                        context.TokenStreams[file] = cached.TokenStream;
                        lock (lockObj)
                        {
                            totalTokens += cached.TokenStream.GetTokens().Count;
                        }
                        return; // Skip lexing for this file
                    }
                }

                var inputStream = new AntlrInputStream(source);
                var lexer = new BmmdlLexer(inputStream);

                // Custom error listener
                var errorListener = new LexerErrorListener(file, context);
                lexer.RemoveErrorListeners();
                lexer.AddErrorListener(errorListener);

                var tokenStream = new CommonTokenStream(lexer);
                tokenStream.Fill(); // Force tokenization

                context.TokenStreams[file] = tokenStream;

                lock (lockObj)
                {
                    totalTokens += tokenStream.GetTokens().Count;
                    if (errorListener.HasErrors)
                        success = false;
                }
            }
            catch (Exception ex)
            {
                context.AddError(ErrorCodes.LEX_FILE_ERROR, $"Failed to read file: {ex.Message}", file, pass: Name);
                lock (lockObj) { success = false; }
            }
        });

        context.TotalTokens = totalTokens;
        return success;
    }
}

internal class LexerErrorListener : IAntlrErrorListener<int>
{
    private readonly string _file;
    private readonly CompilationContext _context;
    
    public bool HasErrors { get; private set; }
    
    public LexerErrorListener(string file, CompilationContext context)
    {
        _file = file;
        _context = context;
    }
    
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, 
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        _context.AddError(ErrorCodes.LEX_ERROR, msg, _file, line, "Lexical Analysis");
        HasErrors = true;
    }
}
