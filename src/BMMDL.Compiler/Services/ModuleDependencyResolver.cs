using System.Text.RegularExpressions;
using Antlr4.Runtime;
using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Services;

/// <summary>
/// Resolves module dependencies by scanning module files and building
/// a dependency graph for topological ordering.
/// </summary>
public class ModuleDependencyResolver
{
    private readonly bool _verbose;
    private readonly ICompilerOutput _output;
    private readonly ILogger _logger;
    
    public ModuleDependencyResolver(bool verbose = false, ICompilerOutput? output = null)
    {
        _verbose = verbose;
        _output = output ?? new ConsoleCompilerOutput();
        _logger = CompilerLoggerFactory.CreateLogger("DependencyResolver");
    }
    
    /// <summary>
    /// Discover all modules in a base directory.
    /// Looks for module.bmmdl files in subdirectories.
    /// </summary>
    public Dictionary<string, ModuleInfo> DiscoverModules(string baseDir)
    {
        var modules = new Dictionary<string, ModuleInfo>(StringComparer.OrdinalIgnoreCase);
        
        if (!Directory.Exists(baseDir))
        {
            if (_verbose)
            {
                _output.WriteWarning($"Modules directory not found: {baseDir}");
                _logger.LogWarning("Modules directory not found: {BaseDir}", baseDir);
            }
            return modules;
        }
        
        // Look for module.bmmdl in subdirectories
        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var moduleFile = Path.Combine(dir, "module.bmmdl");
            if (File.Exists(moduleFile))
            {
                var moduleDecl = QuickParseModuleDeclaration(moduleFile);
                if (moduleDecl != null)
                {
                    modules[moduleDecl.Name] = new ModuleInfo(
                        moduleDecl.Name,
                        moduleDecl.Version,
                        moduleFile,
                        moduleDecl.Dependencies.Select(d => d.ModuleName).ToList()
                    );
                    
                    if (_verbose)
                    {
                        _output.WriteLine($"  Found module: {moduleDecl.Name} v{moduleDecl.Version}");
                        _logger.LogDebug("Found module {ModuleName} v{Version}", moduleDecl.Name, moduleDecl.Version);
                    }
                }
            }
        }
        
        return modules;
    }
    
    /// <summary>
    /// Quick-parse a module file to extract just the module declaration.
    /// Uses regex for speed - doesn't need full ANTLR parse.
    /// </summary>
    public BmModuleDeclaration? QuickParseModuleDeclaration(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            
            // Pattern: module ModuleName version 'x.y.z' { ... }
            var moduleMatch = Regex.Match(content, 
                @"module\s+(\w+)\s+version\s+'([^']+)'\s*\{([^}]*)\}",
                RegexOptions.Singleline);
            
            if (!moduleMatch.Success)
                return null;
            
            var decl = new BmModuleDeclaration
            {
                Name = moduleMatch.Groups[1].Value,
                Version = moduleMatch.Groups[2].Value,
                SourceFile = filePath
            };
            
            var body = moduleMatch.Groups[3].Value;
            
            // Parse author
            var authorMatch = Regex.Match(body, @"author:\s*'([^']+)'");
            if (authorMatch.Success)
                decl.Author = authorMatch.Groups[1].Value;
            
            // Parse description
            var descMatch = Regex.Match(body, @"description:\s*'([^']+)'");
            if (descMatch.Success)
                decl.Description = descMatch.Groups[1].Value;
            
            // Parse dependencies: depends on ModuleName version 'x.y.z';
            var depMatches = Regex.Matches(body, 
                @"depends\s+on\s+(\w+)\s+version\s+'([^']+)'");
            foreach (Match dep in depMatches)
            {
                decl.Dependencies.Add(new BmModuleDependency
                {
                    ModuleName = dep.Groups[1].Value,
                    VersionRange = dep.Groups[2].Value
                });
            }
            
            // Parse publishes
            var pubMatch = Regex.Match(body, @"publishes\s+([^;]+);");
            if (pubMatch.Success)
            {
                var namespaces = pubMatch.Groups[1].Value
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                decl.Publishes.AddRange(namespaces);
            }
            
            // Parse imports
            var impMatch = Regex.Match(body, @"imports\s+([^;]+);");
            if (impMatch.Success)
            {
                var namespaces = impMatch.Groups[1].Value
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                decl.Imports.AddRange(namespaces);
            }
            
            return decl;
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                _output.WriteWarning($"Failed to parse {filePath}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to parse module file {FilePath}", filePath);
            }
            return null;
        }
    }
    
    /// <summary>
    /// Resolve all dependencies for a target module and return files in topological order.
    /// </summary>
    public List<string> ResolveDependencies(string targetModulePath, string? modulesDir = null)
    {
        // Auto-detect modules directory from target path
        if (string.IsNullOrEmpty(modulesDir))
        {
            var dir = Path.GetDirectoryName(targetModulePath);
            modulesDir = dir != null ? Path.GetDirectoryName(dir) : "erp_modules";
        }
        
        if (_verbose)
        {
            _output.WriteLine($"🔍 Scanning modules in: {modulesDir}");
            _logger.LogDebug("Scanning modules in {ModulesDir}", modulesDir);
        }
        
        // Discover all available modules
        var availableModules = DiscoverModules(modulesDir!);
        
        // Parse target module
        var targetDecl = QuickParseModuleDeclaration(targetModulePath);
        if (targetDecl == null)
        {
            throw new InvalidOperationException($"Could not parse module declaration from: {targetModulePath}");
        }
        
        // Add target to available modules if not already there
        if (!availableModules.ContainsKey(targetDecl.Name))
        {
            availableModules[targetDecl.Name] = new ModuleInfo(
                targetDecl.Name,
                targetDecl.Version,
                targetModulePath,
                targetDecl.Dependencies.Select(d => d.ModuleName).ToList()
            );
        }
        
        // Build dependency order using topological sort
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void Visit(string moduleName)
        {
            if (visited.Contains(moduleName))
                return;
            
            if (inProgress.Contains(moduleName))
                throw new InvalidOperationException($"Circular dependency detected involving: {moduleName}");
            
            if (!availableModules.TryGetValue(moduleName, out var module))
            {
                throw new InvalidOperationException($"Module not found: {moduleName}");
            }
            
            inProgress.Add(moduleName);
            
            // Visit dependencies first
            foreach (var dep in module.DependencyNames)
            {
                Visit(dep);
            }
            
            inProgress.Remove(moduleName);
            visited.Add(moduleName);
            result.Add(module.FilePath);
        }
        
        // Start from target module
        Visit(targetDecl.Name);
        
        return result;
    }
    
    /// <summary>
    /// Print the dependency tree.
    /// </summary>
    public void PrintDependencyTree(string targetModulePath, string? modulesDir = null)
    {
        if (string.IsNullOrEmpty(modulesDir))
        {
            var dir = Path.GetDirectoryName(targetModulePath);
            modulesDir = dir != null ? Path.GetDirectoryName(dir) : "erp_modules";
        }
        
        var availableModules = DiscoverModules(modulesDir!);
        var targetDecl = QuickParseModuleDeclaration(targetModulePath);
        
        if (targetDecl == null)
        {
            _output.WriteWarning($"Could not parse module: {targetModulePath}");
            _logger.LogWarning("Could not parse module: {Path}", targetModulePath);
            return;
        }
        
        _output.WriteLine($"🔄 Resolving dependencies for '{targetDecl.Name}'...");
        _logger.LogInformation("Resolving dependencies for {ModuleName}", targetDecl.Name);
        
        var printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void PrintModule(string name, int depth, bool isLast)
        {
            if (!availableModules.TryGetValue(name, out var module))
            {
                var prefix = depth == 0 ? "" : new string(' ', (depth - 1) * 3) + (isLast ? "└─ " : "├─ ");
                _output.WriteLine($"{prefix}{name} (NOT FOUND!)");
                return;
            }
            
            var prefix2 = depth == 0 ? "" : new string(' ', (depth - 1) * 3) + (isLast ? "└─ " : "├─ ");
            var deps = module.DependencyNames.Any() 
                ? $" ← {string.Join(", ", module.DependencyNames)}" 
                : "";
            _output.WriteLine($"{prefix2}{module.Name} ({module.Version}){deps}");
            
            if (printed.Contains(name))
                return;
            printed.Add(name);
        }
        
        // Print in dependency order
        try
        {
            var ordered = ResolveDependencies(targetModulePath, modulesDir);
            for (int i = 0; i < ordered.Count; i++)
            {
                var decl = QuickParseModuleDeclaration(ordered[i]);
                if (decl != null)
                {
                    var isLast = i == ordered.Count - 1;
                    PrintModule(decl.Name, 1, isLast);
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteError(ex.Message);
            _logger.LogError(ex, "Dependency resolution failed");
        }
        
        _output.WriteLine();
    }
}

/// <summary>
/// Information about a discovered module.
/// </summary>
public record ModuleInfo(
    string Name,
    string Version,
    string FilePath,
    List<string> DependencyNames
);
