using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4.5: Dependency Graph
/// Builds entity dependency graph and detects circular references.
/// </summary>
public class DependencyGraphPass : ICompilerPass
{
    public string Name => "Dependency Graph";
    public string Description => "Detect circular references";
    public int Order => 45; // Between Symbol Resolution (4) and Semantic Validation (5)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.DEP_NO_MODEL, "No model available for dependency analysis", pass: Name);
            return false;
        }
        
        var graph = BuildDependencyGraph(context.Model);
        
        // Detect cycles
        var cycles = DetectCycles(graph);
        
        foreach (var cycle in cycles)
        {
            context.AddError(ErrorCodes.DEP_CIRCULAR_ENTITY, 
                $"Circular dependency detected: {string.Join(" -> ", cycle)} -> {cycle.First()}",
                pass: Name);
        }
        
        // Store metrics
        context.DependencyGraph = graph;
        
        var stats = context.PassStats.LastOrDefault();
        if (stats != null)
        {
            stats.AddMetric("Nodes", graph.Nodes.Count);
            stats.AddMetric("Edges", graph.Edges.Count);
            stats.AddMetric("Cycles", cycles.Count);
        }
        
        return cycles.Count == 0;
    }
    
    private DependencyGraph BuildDependencyGraph(BmModel model)
    {
        var graph = new DependencyGraph();
        
        // Add all entities as nodes
        foreach (var entity in model.Entities)
        {
            var qn = entity.QualifiedName;
            graph.AddNode(qn, entity);
        }
        
        // Add edges for associations and compositions
        foreach (var entity in model.Entities)
        {
            var sourceQn = entity.QualifiedName;
            
            foreach (var assoc in entity.Associations)
            {
                if (!string.IsNullOrEmpty(assoc.TargetEntity))
                {
                    graph.AddEdge(sourceQn, assoc.TargetEntity, DependencyType.Association);
                }
            }
            
            foreach (var comp in entity.Compositions)
            {
                if (!string.IsNullOrEmpty(comp.TargetEntity))
                {
                    graph.AddEdge(sourceQn, comp.TargetEntity, DependencyType.Composition);
                }
            }
            
            // Field type dependencies
            foreach (var field in entity.Fields)
            {
                if (!string.IsNullOrEmpty(field.TypeString) && !IsBuiltInType(field.TypeString))
                {
                    var typeName = ExtractTypeName(field.TypeString);
                    if (graph.Nodes.ContainsKey(typeName))
                    {
                        graph.AddEdge(sourceQn, typeName, DependencyType.FieldType);
                    }
                }
            }
        }
        
        // Add events as nodes and track their entity-type field dependencies
        foreach (var evt in model.Events)
        {
            var evtQn = evt.QualifiedName;
            graph.AddNode(evtQn, evt);
            
            foreach (var field in evt.Fields)
            {
                if (!string.IsNullOrEmpty(field.TypeString) && !IsBuiltInType(field.TypeString))
                {
                    var typeName = ExtractTypeName(field.TypeString);
                    if (graph.Nodes.ContainsKey(typeName))
                    {
                        graph.AddEdge(evtQn, typeName, DependencyType.FieldType);
                    }
                }
            }
        }
        
        // Add services as nodes and track their entity dependencies
        foreach (var svc in model.Services)
        {
            var svcQn = svc.QualifiedName;
            graph.AddNode(svcQn, svc);
            
            foreach (var svcEntity in svc.Entities)
            {
                // Service entities reference source entities via Aspects[0]
                var sourceEntityName = svcEntity.Aspects.FirstOrDefault();
                if (!string.IsNullOrEmpty(sourceEntityName))
                {
                    graph.AddEdge(svcQn, sourceEntityName, DependencyType.Association);
                }
            }
        }
        
        // Add views as nodes and track their projected entity dependencies
        foreach (var view in model.Views)
        {
            var viewQn = view.QualifiedName;
            graph.AddNode(viewQn, view);
            
            if (!string.IsNullOrEmpty(view.ProjectionEntityName))
            {
                graph.AddEdge(viewQn, view.ProjectionEntityName, DependencyType.Association);
            }
        }
        
        return graph;
    }
    
    private List<List<string>> DetectCycles(DependencyGraph graph)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();
        
        foreach (var node in graph.Nodes.Keys)
        {
            if (!visited.Contains(node))
            {
                DFS(node, graph, visited, recursionStack, path, cycles);
            }
        }
        
        return cycles;
    }
    
    private void DFS(string node, DependencyGraph graph, 
        HashSet<string> visited, HashSet<string> recursionStack,
        List<string> path, List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);
        
        if (graph.AdjacencyList.TryGetValue(node, out var neighbors))
        {
            foreach (var (neighbor, _) in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DFS(neighbor, graph, visited, recursionStack, path, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found cycle - extract it
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = path.Skip(cycleStart).ToList();
                        cycles.Add(cycle);
                    }
                }
            }
        }
        
        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
    }
    
    private bool IsBuiltInType(string typeName)
    {
        var builtIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Decimal", "Boolean", "Date", "Time", "DateTime", "Timestamp",
            "UUID", "Binary", "Int32", "Int64", "Float", "Double", "Bool", "Byte", "Char"
        };
        var baseName = typeName.Split('[', '(', '?', '<')[0].Trim();
        return builtIns.Contains(baseName);
    }
    
    private string ExtractTypeName(string typeString)
    {
        if (typeString.StartsWith("array of ", StringComparison.OrdinalIgnoreCase))
            return typeString[9..].Trim();
        var idx = typeString.IndexOfAny(['(', '[', '<', '?']);
        return idx > 0 ? typeString[..idx] : typeString;
    }
}

/// <summary>
/// Dependency types between entities.
/// </summary>
public enum DependencyType
{
    Association,
    Composition,
    FieldType,
    RuleTarget
}

/// <summary>
/// Graph of entity dependencies.
/// </summary>
public class DependencyGraph
{
    public Dictionary<string, object> Nodes { get; } = new();
    public List<(string From, string To, DependencyType Type)> Edges { get; } = new();
    public Dictionary<string, List<(string Target, DependencyType Type)>> AdjacencyList { get; } = new();
    
    public void AddNode(string name, object entity)
    {
        Nodes[name] = entity;
        if (!AdjacencyList.ContainsKey(name))
            AdjacencyList[name] = new();
    }
    
    public void AddEdge(string from, string to, DependencyType type)
    {
        Edges.Add((from, to, type));
        if (!AdjacencyList.ContainsKey(from))
            AdjacencyList[from] = new();
        AdjacencyList[from].Add((to, type));
    }
    
    /// <summary>
    /// Generate Mermaid diagram representation.
    /// </summary>
    public string ToMermaid()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("graph LR");
        
        foreach (var (from, to, type) in Edges.Take(50)) // Limit for readability
        {
            var arrow = type switch
            {
                DependencyType.Composition => "-->|*|",
                DependencyType.Association => "-->",
                DependencyType.FieldType => "-.->",
                _ => "-->"
            };
            sb.AppendLine($"    {SanitizeName(from)} {arrow} {SanitizeName(to)}");
        }
        
        return sb.ToString();
    }
    
    private string SanitizeName(string name) => name.Replace(".", "_").Replace(" ", "_");
}
