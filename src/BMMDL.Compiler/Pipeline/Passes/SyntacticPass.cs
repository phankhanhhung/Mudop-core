using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 2: Syntactic Analysis
/// Parses token streams into parse trees using ANTLR parser.
/// </summary>
public class SyntacticPass : ICompilerPass
{
    public string Name => "Syntactic Analysis";
    public string Description => "Build parse trees from tokens";
    public int Order => 2;
    
    public bool Execute(CompilationContext context)
    {
        bool success = true;

        foreach (var (file, tokenStream) in context.TokenStreams)
        {
            try
            {
                // Check compilation cache for unchanged files
                if (context.Cache != null)
                {
                    // Compute source hash
                    string? source = null;
                    if (context.SourceContents.TryGetValue(file, out var inMemorySource))
                        source = inMemorySource;
                    else if (File.Exists(file))
                        source = File.ReadAllText(file);

                    if (source != null)
                    {
                        var sourceHash = CompilationCache.ComputeHash(source);
                        if (context.Cache.TryGetParseResult(file, sourceHash, out var cached) && cached != null)
                        {
                            // Cache hit: reuse cached parse tree
                            context.ParseTrees[file] = cached.ParseTree;
                            continue; // Skip parsing for this file
                        }
                    }
                }

                tokenStream.Reset(); // Reset to beginning

                var parser = new Parsing.BmmdlParser(tokenStream);

                // Custom error listener
                var errorListener = new ParserErrorListener(file, context);
                parser.RemoveErrorListeners();
                parser.AddErrorListener(errorListener);

                var tree = parser.compilationUnit();
                context.ParseTrees[file] = tree;

                // Store result in cache for future compilations
                if (context.Cache != null)
                {
                    string? sourceForCache = null;
                    if (context.SourceContents.TryGetValue(file, out var src))
                        sourceForCache = src;
                    else if (File.Exists(file))
                        sourceForCache = File.ReadAllText(file);

                    if (sourceForCache != null && !errorListener.HasErrors)
                    {
                        var hash = CompilationCache.ComputeHash(sourceForCache);
                        context.Cache.StoreParseResult(file, hash, tokenStream, tree);
                    }
                }

                if (errorListener.HasErrors)
                    success = false;
            }
            catch (Exception ex)
            {
                context.AddError(ErrorCodes.SYN_PARSE_ERROR, $"Parse error: {ex.Message}", file, pass: Name);
                success = false;
            }
        }

        return success;
    }
}

internal class ParserErrorListener : BaseErrorListener
{
    private readonly string _file;
    private readonly CompilationContext _context;
    
    public bool HasErrors { get; private set; }
    
    public ParserErrorListener(string file, CompilationContext context)
    {
        _file = file;
        _context = context;
    }
    
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        _context.AddError(ErrorCodes.SYN_ERROR, msg, _file, line, "Syntactic Analysis");
        HasErrors = true;
    }
}
