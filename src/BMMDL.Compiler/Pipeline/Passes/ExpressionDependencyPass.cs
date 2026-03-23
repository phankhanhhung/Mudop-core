using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Parsing;
using BMMDL.Compiler.Utilities;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4.6: Expression Dependency Graph
/// Analyzes dependencies within computed field expressions to detect cycles.
/// </summary>
public class ExpressionDependencyPass : ICompilerPass
{
    public string Name => "Expression Dependency";
    public string Description => "Detect circular dependencies in computed fields";
    public int Order => 46; // After Entity Dependency (4.5)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
            return false;
            
        var graph = BuildGraph(context.Model);
        var cycles = DetectCycles(graph);
        
        foreach (var cycle in cycles)
        {
            context.AddError(ErrorCodes.DEP_CIRCULAR_EXPRESSION, 
                $"Circular dependency in computed fields: {string.Join(" -> ", cycle)} -> {cycle.First()}",
                pass: Name);
        }
        
        context.ExpressionDependencies = graph;
        
        return cycles.Count == 0;
    }
    
    private ExpressionDependencyGraph BuildGraph(BmModel model)
    {
        var graph = new ExpressionDependencyGraph();

        foreach (var entity in model.Entities)
        {
            var entityQn = entity.QualifiedName;

            foreach (var field in entity.Fields)
            {
                if (field.ComputedExpr != null)
                {
                    var sourceNode = $"{entityQn}.{field.Name}";
                    graph.Nodes.Add(sourceNode);

                    var visitor = new DependencyVisitor(sourceNode, graph, entityQn, model);
                    AnalyzeExpression(field.ComputedExpr, visitor);
                }
            }
        }

        return graph;
    }
    
    private void AnalyzeExpression(BmExpression expression, DependencyVisitor visitor)
    {
        ExpressionTraversalUtility.Traverse(expression, node =>
        {
            if (node is BmIdentifierExpression id)
                visitor.VisitIdentifier(id);
        });
    }
    
    private List<List<string>> DetectCycles(ExpressionDependencyGraph graph)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();
        
        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node))
            {
                DFS(node, graph, visited, recursionStack, path, cycles);
            }
        }
        
        return cycles;
    }
    
    private void DFS(string node, ExpressionDependencyGraph graph, 
        HashSet<string> visited, HashSet<string> recursionStack,
        List<string> path, List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);
        
        if (graph.AdjacencyList.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DFS(neighbor, graph, visited, recursionStack, path, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        cycles.Add(path.Skip(cycleStart).ToList());
                    }
                }
            }
        }
        
        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
    }
}

public class DependencyVisitor
{
    private readonly string _sourceNode;
    private readonly ExpressionDependencyGraph _graph;
    private readonly string _currentEntityQn;
    private readonly BmModel? _model;

    public DependencyVisitor(string sourceNode, ExpressionDependencyGraph graph, string currentEntityQn, BmModel? model = null)
    {
        _sourceNode = sourceNode;
        _graph = graph;
        _currentEntityQn = currentEntityQn;
        _model = model;
    }

    public void VisitIdentifier(BmIdentifierExpression id)
    {
        if (id.IsSimple)
        {
            // Simple field reference in current entity
            var target = $"{_currentEntityQn}.{id.Root}";
            _graph.AddEdge(_sourceNode, target);
        }
        else if (id.Path.Count > 1 && _model != null)
        {
            // Multi-part path: walk association chain to resolve cross-entity dependency
            var resolvedTarget = ResolvePathDependency(id.Path);
            if (resolvedTarget != null)
            {
                _graph.AddEdge(_sourceNode, resolvedTarget);
            }
        }
    }

    /// <summary>
    /// Walk an association/composition chain to resolve the final target entity and field.
    /// Returns a dependency node like "TargetEntity.fieldName" or null if resolution fails.
    /// </summary>
    private string? ResolvePathDependency(List<string> path)
    {
        // Find the current entity from the model
        var currentEntity = FindEntityByQualifiedName(_currentEntityQn);
        if (currentEntity == null) return null;

        // Walk intermediate segments (associations/compositions)
        for (int i = 0; i < path.Count - 1; i++)
        {
            var segmentName = path[i];

            // Find association or composition matching segment name
            var assoc = currentEntity.Associations.FirstOrDefault(a =>
                a.Name.Equals(segmentName, StringComparison.OrdinalIgnoreCase));

            if (assoc == null)
            {
                assoc = currentEntity.Compositions.FirstOrDefault(c =>
                    c.Name.Equals(segmentName, StringComparison.OrdinalIgnoreCase));
            }

            if (assoc == null) return null;

            // Resolve target entity
            var targetEntity = _model!.FindEntity(assoc.TargetEntity);
            if (targetEntity == null) return null;

            currentEntity = targetEntity;
        }

        // Final segment is the field in the resolved target entity
        var finalSegment = path[path.Count - 1];
        return $"{currentEntity.QualifiedName}.{finalSegment}";
    }

    private BmEntity? FindEntityByQualifiedName(string qualifiedName)
    {
        return _model?.Entities.FirstOrDefault(e =>
            e.QualifiedName == qualifiedName || e.Name == qualifiedName);
    }
}

public class ExpressionDependencyGraph
{
    public HashSet<string> Nodes { get; } = new();
    public Dictionary<string, HashSet<string>> AdjacencyList { get; } = new();
    
    public void AddEdge(string from, string to)
    {
        Nodes.Add(from);
        Nodes.Add(to);
        
        if (!AdjacencyList.ContainsKey(from))
            AdjacencyList[from] = new();
            
        AdjacencyList[from].Add(to);
    }
}
