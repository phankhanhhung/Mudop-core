using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler;

/// <summary>
/// Unified compiler facade providing a single entry point for all BMMDL compilation needs.
/// Internally delegates to BmmdlCompiler (quick parse) or CompilerPipeline (full compile).
/// </summary>
public class BmmdlUnifiedCompiler
{
    private readonly CompilationOptions _options;
    
    public BmmdlUnifiedCompiler(CompilationOptions? options = null)
    {
        _options = options ?? new CompilationOptions();
    }
    
    #region Quick Parse (Single file/string, syntax only)
    
    /// <summary>
    /// Quick parse a BMMDL source string. No semantic validation, no cross-file resolution.
    /// Use for: unit tests, quick syntax checks, single-file operations.
    /// </summary>
    public BmModel QuickParse(string source)
    {
        var parser = new BmmdlCompiler();
        return parser.Compile(source);
    }
    
    /// <summary>
    /// Quick parse a BMMDL file. No semantic validation, no cross-file resolution.
    /// Use for: unit tests, quick syntax checks, single-file operations.
    /// </summary>
    public BmModel QuickParseFile(string filePath)
    {
        var parser = new BmmdlCompiler();
        return parser.CompileFile(filePath);
    }
    
    #endregion
    
    #region Full Compile (Multi-pass, semantic validation)
    
    /// <summary>
    /// Full compilation with 8-pass pipeline including semantic validation.
    /// Use for: production builds, multi-file projects, module compilation.
    /// </summary>
    public CompilationResult Compile(IEnumerable<string> files)
    {
        var pipeline = new CompilerPipeline(_options);
        return pipeline.Compile(files);
    }
    
    /// <summary>
    /// Full compilation of a single file with full validation.
    /// </summary>
    public CompilationResult Compile(string filePath)
    {
        return Compile(new[] { filePath });
    }
    
    /// <summary>
    /// Full compilation with module dependency resolution.
    /// Automatically discovers and compiles dependent modules.
    /// </summary>
    public CompilationResult CompileWithDependencies(string modulePath, string? modulesDir = null)
    {
        var resolver = new ModuleDependencyResolver();
        var orderedFiles = resolver.ResolveDependencies(modulePath, modulesDir);
        return Compile(orderedFiles);
    }
    
    #endregion
    
    #region Static Convenience Methods
    
    /// <summary>
    /// Static shortcut for quick parse.
    /// </summary>
    public static BmModel Parse(string source) => new BmmdlUnifiedCompiler().QuickParse(source);
    
    /// <summary>
    /// Static shortcut for quick file parse.
    /// </summary>
    public static BmModel ParseFile(string filePath) => new BmmdlUnifiedCompiler().QuickParseFile(filePath);
    
    /// <summary>
    /// Static shortcut for full compilation.
    /// </summary>
    public static CompilationResult Build(params string[] files) => new BmmdlUnifiedCompiler().Compile(files);
    
    /// <summary>
    /// Static shortcut for module compilation with dependency resolution.
    /// </summary>
    public static CompilationResult BuildModule(string modulePath, string? modulesDir = null) 
        => new BmmdlUnifiedCompiler().CompileWithDependencies(modulePath, modulesDir);
    
    #endregion
}
