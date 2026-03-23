using System.Diagnostics;
using BMMDL.Compiler.Services;
using BMMDL.Runtime.Plugins;

namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// Main orchestrator for the multi-pass compilation pipeline.
/// Executes passes in sequence, collecting diagnostics and timing.
/// </summary>
public class CompilerPipeline
{
    private readonly List<ICompilerPass> _passes = new();
    private readonly CompilationOptions _options;
    private readonly ICompilerOutput _output;
    private readonly CompilationCache? _cache;

    public CompilerPipeline(CompilationOptions? options = null, ICompilerOutput? output = null)
        : this(options, output, null, null, null)
    {
    }

    /// <summary>
    /// Creates a compiler pipeline with an optional compilation cache for incremental compilation.
    /// </summary>
    public CompilerPipeline(CompilationOptions? options, CompilationCache? cache)
        : this(options, null, null, cache, null)
    {
    }

    /// <summary>
    /// Creates a compiler pipeline with an injected feature registry.
    /// When provided, the registry is passed to FeatureContributionPass and
    /// PluginAnnotationValidationPass — ensuring external plugins contribute
    /// metadata during compilation.
    /// </summary>
    public CompilerPipeline(CompilationOptions? options, PlatformFeatureRegistry registry)
        : this(options, null, null, null, registry)
    {
    }

    /// <summary>
    /// Creates a compiler pipeline with custom passes via dependency injection.
    /// </summary>
    /// <param name="options">Compilation options</param>
    /// <param name="output">Output handler for progress messages</param>
    /// <param name="passes">Custom passes to use. If null, uses default passes.</param>
    /// <param name="cache">Optional compilation cache for incremental compilation.</param>
    /// <param name="registry">Optional feature registry. When provided, plugin passes use this
    /// registry (which may include external DLL plugins) instead of auto-discovering.</param>
    public CompilerPipeline(
        CompilationOptions? options,
        ICompilerOutput? output,
        IEnumerable<ICompilerPass>? passes,
        CompilationCache? cache = null,
        PlatformFeatureRegistry? registry = null)
    {
        _options = options ?? new CompilationOptions();
        _output = output ?? new ConsoleCompilerOutput(_options.UseColors);
        _cache = cache;

        if (passes != null)
        {
            _passes.AddRange(passes);
        }
        else
        {
            InitializeDefaultPasses(registry);
        }
    }
    
    private void InitializeDefaultPasses(PlatformFeatureRegistry? registry = null)
    {
        _passes.Add(new Passes.LexicalPass());           // 1
        _passes.Add(new Passes.SyntacticPass());         // 2
        _passes.Add(new Passes.ModelBuildPass());        // 3
        _passes.Add(new Passes.SymbolResolutionPass());  // 4 (40)
        _passes.Add(new Passes.InheritanceResolutionPass()); // 4.4 (44)
        _passes.Add(new Passes.DependencyGraphPass());   // 4.5 (45)
        _passes.Add(new Passes.ExpressionDependencyPass()); // 4.6 (46)
        _passes.Add(new Passes.BindingPass());           // 4.7 (47)
        _passes.Add(new Passes.TenantIsolationPass());   // 4.8 (48)
        _passes.Add(new Passes.FileStorageValidationPass()); // 4.9 (49)
        _passes.Add(new Passes.TemporalValidationPass());    // 4.95 (50)
        _passes.Add(new Passes.SemanticValidationPass()); // 5 (51)
        _passes.Add(new Passes.AnnotationMergePass());   // 5.4 (54)
        _passes.Add(new Passes.ExtensionMergePass());    // 5.5 (55)
        _passes.Add(new Passes.ModificationPass());      // 5.6 (56)
        _passes.Add(new Passes.OptimizationPass());      // 6 (60)

        // Plugin passes: use injected registry if available, otherwise auto-discover
        if (registry is not null)
        {
            _passes.Add(new Passes.FeatureContributionPass(registry));     // 6.1 (61)
            _passes.Add(new Passes.PluginAnnotationValidationPass(registry)); // 6.2 (62)
        }
        else
        {
            _passes.Add(new Passes.FeatureContributionPass());     // 6.1 (61) — auto-discover
            _passes.Add(new Passes.PluginAnnotationValidationPass()); // 6.2 (62) — auto-discover
        }
    }


    /// <summary>
    /// Gets the default compiler passes for DI registration or testing.
    /// Plugin passes auto-discover features from the runtime assembly.
    /// </summary>
    public static IEnumerable<ICompilerPass> GetDefaultPasses(PlatformFeatureRegistry? registry = null)
    {
        return new ICompilerPass[]
        {
            new Passes.LexicalPass(),           // 1
            new Passes.SyntacticPass(),         // 2
            new Passes.ModelBuildPass(),        // 3
            new Passes.SymbolResolutionPass(),  // 4 (40)
            new Passes.InheritanceResolutionPass(), // 4.4 (44)
            new Passes.DependencyGraphPass(),   // 4.5 (45)
            new Passes.ExpressionDependencyPass(), // 4.6 (46)
            new Passes.BindingPass(),           // 4.7 (47)
            new Passes.TenantIsolationPass(),   // 4.8 (48)
            new Passes.FileStorageValidationPass(), // 4.9 (49)
            new Passes.TemporalValidationPass(),    // 4.95 (50)
            new Passes.SemanticValidationPass(), // 5 (51)
            new Passes.AnnotationMergePass(),   // 5.4 (54)
            new Passes.ExtensionMergePass(),    // 5.5 (55)
            new Passes.ModificationPass(),      // 5.6 (56)
            new Passes.OptimizationPass(),      // 6 (60)
            registry is not null
                ? new Passes.FeatureContributionPass(registry)      // 6.1 (61)
                : new Passes.FeatureContributionPass(),
            registry is not null
                ? new Passes.PluginAnnotationValidationPass(registry) // 6.2 (62)
                : new Passes.PluginAnnotationValidationPass(),
        };
    }

    
    /// <summary>
    /// Execute the full compilation pipeline.
    /// </summary>
    public CompilationResult Compile(IEnumerable<string> sourceFiles)
    {
        var context = new CompilationContext { Options = _options };
        context.SourceFiles.AddRange(sourceFiles);
        return CompileWithContext(context);
    }
    
    /// <summary>
    /// Execute the full compilation pipeline from in-memory source content.
    /// </summary>
    /// <param name="sourceContents">Dictionary mapping virtual filenames to BMMDL source content</param>
    public CompilationResult CompileFromString(Dictionary<string, string> sourceContents)
    {
        var context = new CompilationContext { Options = _options };
        
        // Add virtual filenames as source files
        foreach (var kvp in sourceContents)
        {
            context.SourceFiles.Add(kvp.Key);
            context.SourceContents[kvp.Key] = kvp.Value;
        }
        
        return CompileWithContext(context);
    }
    
    /// <summary>
    /// Execute the full compilation pipeline from a single in-memory source.
    /// </summary>
    /// <param name="source">BMMDL source content</param>
    /// <param name="virtualFileName">Virtual filename for error reporting</param>
    public CompilationResult CompileFromString(string source, string virtualFileName = "<input>.bmmdl")
    {
        return CompileFromString(new Dictionary<string, string> { { virtualFileName, source } });
    }
    
    /// <summary>
    /// Internal: Execute compilation passes on prepared context.
    /// </summary>
    private CompilationResult CompileWithContext(CompilationContext context)
    {
        // Attach the compilation cache to the context so passes can use it
        context.Cache = _cache;

        var totalStopwatch = Stopwatch.StartNew();

        PrintHeader(context);
        
        foreach (var pass in _passes.OrderBy(p => p.Order))
        {
            var passStopwatch = Stopwatch.StartNew();
            
            PrintPassStart(pass);
            
            bool success;
            try
            {
                success = pass.Execute(context);
            }
            catch (Exception ex)
            {
                context.AddError(ErrorCodes.PASS_EXCEPTION, $"Pass failed: {ex.Message}", pass: pass.Name);
                success = false;
            }
            
            passStopwatch.Stop();
            
            var stats = new PassStatistics
            {
                PassName = pass.Name,
                Duration = passStopwatch.Elapsed
            };
            context.PassStats.Add(stats);
            
            PrintPassComplete(pass, stats, success, context);
            
            if (!success && _options.StopOnFirstError)
            {
                break;
            }
        }
        
        totalStopwatch.Stop();
        
        PrintSummary(context, totalStopwatch.Elapsed);
        
        return new CompilationResult(context, totalStopwatch.Elapsed);
    }
    
    private void PrintHeader(CompilationContext context)
    {
        if (!_options.ShowProgress) return;
        
        _output.WriteLine();
        _output.WriteColored("🔄 ", ConsoleColor.Cyan);
        _output.WriteColored("BMMDL Compiler", ConsoleColor.White);
        _output.WriteLine(" v0.9.5");
        _output.WriteLine();
        
        var totalSize = context.SourceContents.Count > 0
            ? context.SourceContents.Values.Sum(s => (long)s.Length)
            : context.SourceFiles.Where(File.Exists).Sum(f => new FileInfo(f).Length);
        _output.WriteLine($"📁 Source Files: {context.SourceFiles.Count} files ({totalSize / 1024:N0} KB)");
        _output.WriteLine();
    }
    
    private void PrintPassStart(ICompilerPass pass)
    {
        if (!_options.ShowProgress) return;
        
        // Map order to display index
        var displayIdx = pass.Order switch
        {
            1 => "1", 2 => "2", 3 => "3",
            40 => "4", 45 => "4.5", 46 => "4.6", 47 => "4.7",
            51 => "5", 60 => "6",
            _ => pass.Order.ToString()
        };
        var passLabel = $"Pass {displayIdx}/6: {pass.Name}";
        _output.Write(passLabel.PadRight(40));
    }
    
    private void PrintPassComplete(ICompilerPass pass, PassStatistics stats, bool success, CompilationContext context)
    {
        if (!_options.ShowProgress) return;
        
        var ms = stats.Duration.TotalMilliseconds;
        var dots = new string('.', Math.Max(1, 20 - (int)(ms / 10)));
        
        _output.Write(dots + " ");
        
        if (success)
        {
            _output.WriteColored("✅", ConsoleColor.Green);
            _output.WriteLine($" ({ms:N0}ms)");
        }
        else
        {
            _output.WriteColored("❌", ConsoleColor.Red);
            _output.WriteLine($" ({ms:N0}ms) - {context.ErrorCount} errors");
        }
        
        // Print pass-specific metrics
        PrintPassMetrics(pass.Order, context);
    }
    
    private void PrintPassMetrics(int passOrder, CompilationContext context)
    {
        if (!_options.ShowProgress) return;
        
        _output.WriteColored("", ConsoleColor.DarkGray); // Set color context
        switch (passOrder)
        {
            case 1: // Lexical
                _output.WriteLine($"  └─ {context.SourceFiles.Count} files → {context.TotalTokens:N0} tokens");
                break;
            case 2: // Syntactic
                _output.WriteLine($"  └─ {context.ParseTrees.Count} parse trees generated");
                break;
            case 3: // Model Build
                if (context.Model != null)
                {
                    _output.WriteLine($"  └─ {context.Model.Entities.Count} entities, {context.Model.Services.Count} services, {context.Model.Types.Count + context.Model.Enums.Count} types");
                    _output.WriteLine($"  └─ {context.ExpressionNodeCount:N0} expression AST nodes");
                }
                break;
            case 35: // Type Inference
                _output.WriteLine($"  └─ {context.InferredTypesCount} types inferred from expressions");
                break;
            case 40: // Symbol Resolution
                _output.WriteLine($"  └─ {context.Symbols.Count:N0} symbols registered");
                _output.WriteLine($"  └─ {context.ResolvedReferences:N0} references resolved");
                break;
            case 45: // Dependency Graph
                if (context.DependencyGraph != null)
                {
                    _output.WriteLine($"  └─ {context.DependencyGraph.Nodes.Count} nodes, {context.DependencyGraph.Edges.Count} edges");
                }
                break;
            case 46: // Expression Dependencies
                if (context.ExpressionDependencies != null)
                {
                    _output.WriteLine($"  └─ {context.ExpressionDependencies.Nodes.Count} nodes tracked");
                }
                break;
            case 47: // Binding & Inference
                _output.WriteLine($"  └─ {context.BoundIdentifiers} identifiers bound");
                _output.WriteLine($"  └─ {context.TypeInferenceCount} expressions typed");
                break;
            case 51: // Semantic Validation
                _output.WriteLine($"  └─ {context.ValidationsPerformed} validations performed");
                _output.WriteLine($"  └─ {context.ErrorCount} errors, {context.WarningCount} warnings");
                break;
            case 60: // Optimization
                _output.WriteLine($"  └─ {context.InlinedAspects} aspect fields inlined, {context.DeduplicatedTypes} duplicates found");
                break;
            case 61: // Feature Contribution
                _output.WriteLine($"  └─ {context.FeatureContributionsApplied} contributions applied to {context.FeatureEntitiesProcessed} entities");
                break;
            case 62: // Plugin Annotation Validation
                _output.WriteLine($"  └─ {context.PluginAnnotationsValidated} elements validated");
                break;
        }
    }
    
    private void PrintSummary(CompilationContext context, TimeSpan totalTime)
    {
        if (!_options.ShowProgress) return;
        
        _output.WriteLine();
        _output.WriteSeparator();
        
        if (context.HasErrors)
        {
            _output.WriteColored("❌ Compilation failed!", ConsoleColor.Red);
            _output.WriteLine($" ({totalTime.TotalMilliseconds:N0}ms total)");
        }
        else
        {
            _output.WriteColored("✅ Compilation successful!", ConsoleColor.Green);
            _output.WriteLine($" ({totalTime.TotalMilliseconds:N0}ms total)");
        }
        
        _output.WriteLine();
        
        if (context.Model != null)
        {
            _output.WriteLine($"  Entities:  {context.Model.Entities.Count,5}");
            _output.WriteLine($"  Services:  {context.Model.Services.Count,5}");
            _output.WriteLine($"  Types:     {context.Model.Types.Count + context.Model.Enums.Count,5}");
            _output.WriteLine($"  AST Nodes: {context.ExpressionNodeCount,5}");
            _output.WriteLine($"  Symbols:   {context.Symbols.Count,5}");
        }
        
        // Print errors if any
        if (context.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            _output.WriteLine();
            _output.WriteColored("Errors:", ConsoleColor.Red);
            _output.WriteLine();
            foreach (var diag in context.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10))
            {
                _output.WriteLine($"  {diag}");
            }
            if (context.ErrorCount > 10)
            {
                _output.WriteLine($"  ... and {context.ErrorCount - 10} more errors");
            }
        }
        
        _output.WriteLine();
    }
}

/// <summary>
/// Result of a compilation run.
/// </summary>
public record CompilationResult(CompilationContext Context, TimeSpan TotalTime)
{
    public bool Success => !Context.HasErrors;
    public int ErrorCount => Context.ErrorCount;
    public int WarningCount => Context.WarningCount;
}
