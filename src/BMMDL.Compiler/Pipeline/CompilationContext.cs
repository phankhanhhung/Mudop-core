using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Types;
using BMMDL.Compiler.Pipeline.Passes;
using BMMDL.MetaModel.Abstractions;
using System.Collections.Concurrent;

namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// Shared context passed through all compilation passes.
/// Accumulates results and diagnostics from each pass.
/// </summary>
public class CompilationContext
{
    // ========== Input ==========
    public List<string> SourceFiles { get; } = new();

    /// <summary>
    /// In-memory source content keyed by virtual filename.
    /// When set, pipeline uses this instead of reading from disk.
    /// </summary>
    public Dictionary<string, string> SourceContents { get; } = new();

    public CompilationOptions Options { get; set; } = new();

    /// <summary>
    /// Optional compilation cache for incremental compilation.
    /// When set, passes can check for cached parse results before re-parsing unchanged files.
    /// </summary>
    public CompilationCache? Cache { get; set; }
    
    // ========== Pass 1: Lexical (Thread-safe for parallel) ==========
    public ConcurrentDictionary<string, CommonTokenStream> TokenStreams { get; } = new();
    public int TotalTokens { get; set; }
    
    // ========== Pass 2: Syntactic (Thread-safe for parallel) ==========
    public ConcurrentDictionary<string, IParseTree> ParseTrees { get; } = new();
    
    // ========== Pass 3: Model Building ==========
    public BmModel? Model { get; set; }
    public int ExpressionNodeCount { get; set; }
    
    // ========== Pass 3.5: Type Inference ==========
    public int InferredTypesCount { get; set; }
    
    // ========== Pass 4: Symbol Resolution ==========
    public SymbolTable Symbols { get; } = new();
    public int ResolvedReferences { get; set; }
    
    // ========== Pass 4.5: Dependency Graph ==========
    public DependencyGraph? DependencyGraph { get; set; }
    
    // ========== Pass 4.6: Expression Dependencies ==========
    public ExpressionDependencyGraph? ExpressionDependencies { get; set; }
    
    // ========== Pass 4.7: Binding & Type Inference ==========
    public int BoundIdentifiers { get; set; }
    public int TypeInferenceCount { get; set; }
    
    // ========== Pass 5: Semantic Validation ==========
    public int ValidationsPerformed { get; set; }
    
    // ========== Pass 6: Optimization ==========
    public int InlinedAspects { get; set; }
    public int DeduplicatedTypes { get; set; }

    // ========== Pass 6.1: Feature Contribution ==========
    public int FeatureContributionsApplied { get; set; }
    public int FeatureEntitiesProcessed { get; set; }

    // ========== Pass 6.2: Plugin Annotation Validation ==========
    public int PluginAnnotationsValidated { get; set; }
    
    // ========== Diagnostics (Thread-safe) ==========
    public ConcurrentBag<CompilationDiagnostic> Diagnostics { get; } = new();
    public List<PassStatistics> PassStats { get; } = new();
    
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
    
    public void AddError(string code, string message, string? file = null, int? line = null, string? pass = null)
        => Diagnostics.Add(new(DiagnosticSeverity.Error, code, message, file, line, null, pass));
    
    public void AddWarning(string code, string message, string? file = null, int? line = null, string? pass = null)
        => Diagnostics.Add(new(DiagnosticSeverity.Warning, code, message, file, line, null, pass));
    
    public void AddInfo(string code, string message, string? pass = null)
        => Diagnostics.Add(new(DiagnosticSeverity.Info, code, message, PassName: pass));
}

/// <summary>
/// Compilation options that control pipeline behavior.
/// </summary>
public class CompilationOptions
{
    public bool Verbose { get; set; }
    public bool StopOnFirstError { get; set; }
    public bool ShowProgress { get; set; } = true;
    public bool UseColors { get; set; } = true;
}

/// <summary>
/// Symbol table for tracking defined types, entities, and references.
/// </summary>
public class SymbolTable
{
    private readonly Dictionary<string, SymbolInfo> _symbols = new();
    
    public int Count => _symbols.Count;
    
    public void Register(string qualifiedName, SymbolKind kind, string? sourceFile = null, int? line = null, INamedElement? element = null)
    {
        _symbols[qualifiedName] = new SymbolInfo(qualifiedName, kind, sourceFile, line, element);
    }
    
    public bool TryResolve(string name, out SymbolInfo? symbol)
        => _symbols.TryGetValue(name, out symbol);
    
    public bool Contains(string name) => _symbols.ContainsKey(name);
    
    public IEnumerable<SymbolInfo> GetAll() => _symbols.Values;
    
    public IEnumerable<SymbolInfo> GetByKind(SymbolKind kind) 
        => _symbols.Values.Where(s => s.Kind == kind);
}

public record SymbolInfo(string QualifiedName, SymbolKind Kind, string? SourceFile, int? Line, INamedElement? Element = null);

public enum SymbolKind
{
    Entity,
    Field,
    Association,
    Type,
    Enum,
    Aspect,
    Service,
    Function,
    Action,
    Rule,
    AccessControl,
    Namespace,
    Event,
    Sequence,
    View,
    Seed
}
