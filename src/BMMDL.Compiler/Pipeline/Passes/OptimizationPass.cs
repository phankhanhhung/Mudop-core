using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Service;
using BMMDL.Compiler.Parsing;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 6: Optimization
/// Optimizes the model by inlining aspects, deduplicating types, and merging namespaces.
/// </summary>
public class OptimizationPass : ICompilerPass
{
    public string Name => "Optimization";
    public string Description => "Inline aspects, deduplicate types";
    public int Order => 60; // After Semantic Validation (5)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.OPT_NO_MODEL, "No model available for optimization", pass: Name);
            return false;
        }

        int inlinedAspects = 0;
        int deduplicatedTypes = 0;
        int inlinedBehaviors = 0;
        int crossAspectViews = 0;

        // Build aspect lookup once (shared by both inline methods)
        var aspectLookup = BuildAspectLookup(context.Model);

        // Phase 1: Inline aspect fields/associations into entities
        inlinedAspects = InlineAspects(context.Model, context, aspectLookup);

        // Phase 2: Inline aspect behavioral rules and access controls
        inlinedBehaviors = InlineAspectBehaviors(context.Model, context, aspectLookup);

        // Phase 3: Deduplicate identical type definitions
        deduplicatedTypes = DeduplicateTypes(context.Model, context);

        // Phase 4: Generate synthetic views for @Query.CrossAspect aspects
        crossAspectViews = GenerateCrossAspectViews(context.Model, context);

        // Store metrics
        var stats = context.PassStats.LastOrDefault();
        if (stats != null)
        {
            stats.AddMetric("InlinedAspects", inlinedAspects);
            stats.AddMetric("InlinedBehaviors", inlinedBehaviors);
            stats.AddMetric("DeduplicatedTypes", deduplicatedTypes);
            stats.AddMetric("CrossAspectViews", crossAspectViews);
        }

        return true;
    }
    
    private int InlineAspects(BmModel model, CompilationContext context, Dictionary<string, BmAspect> aspectLookup)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            foreach (var aspectName in entity.Aspects.ToList())
            {
                // Resolve the full aspect chain (DFS with cycle detection)
                var chain = new List<BmAspect>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!ResolveAspectChain(aspectName, aspectLookup, visited, chain, context))
                    continue; // Cycle detected — error already recorded via context.AddError(OPT_CIRCULAR_ASPECT).
                              // Compilation will fail because CompilationResult.Success checks context.HasErrors.
                
                // Inline all fields and associations from the chain
                foreach (var aspect in chain)
                {
                    foreach (var field in aspect.Fields)
                    {
                        if (!entity.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            var inlinedField = CloneField(field);
                            entity.Fields.Add(inlinedField);
                            count++;
                        }
                    }
                    foreach (var assoc in aspect.Associations)
                    {
                        if (!entity.Associations.Any(a => string.Equals(a.Name, assoc.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.Associations.Add(assoc);
                        }
                    }
                    foreach (var idx in aspect.Indexes)
                    {
                        if (!entity.Indexes.Any(i => string.Equals(i.Name, idx.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.Indexes.Add(idx);
                        }
                    }
                    foreach (var constraint in aspect.Constraints)
                    {
                        if (!entity.Constraints.Any(c => string.Equals(c.Name, constraint.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.Constraints.Add(constraint);
                        }
                    }
                }
            }
        }
        
        context.InlinedAspects = count;
        context.AddInfo(ErrorCodes.OPT_INLINED_FIELDS, $"Inlined {count} fields from aspects", Name);
        return count;
    }

    /// <summary>
    /// Recursively resolves an aspect chain via DFS, collecting all transitive aspects.
    /// Returns false if a circular dependency is detected.
    /// </summary>
    private bool ResolveAspectChain(string aspectName, Dictionary<string, BmAspect> lookup,
        HashSet<string> visited, List<BmAspect> chain, CompilationContext context)
    {
        if (!lookup.TryGetValue(aspectName, out var aspect))
            return true; // unknown aspect, skip silently (symbol resolution already handles this)
        
        var key = aspect.QualifiedName;
        if (visited.Contains(key))
        {
            context.AddError(ErrorCodes.OPT_CIRCULAR_ASPECT,
                $"Circular aspect inclusion detected: '{aspectName}' is already in the chain", Name);
            return false;
        }
        
        visited.Add(key);
        
        // First resolve included aspects (depth-first)
        foreach (var includeName in aspect.Includes)
        {
            if (!ResolveAspectChain(includeName, lookup, visited, chain, context))
                return false;
        }
        
        // Then add this aspect (post-order ensures parents come first)
        chain.Add(aspect);
        return true;
    }

    /// <summary>
    /// Build lookup dictionary for aspects, keyed by both qualified and short names.
    /// </summary>
    private static Dictionary<string, BmAspect> BuildAspectLookup(BmModel model)
    {
        var lookup = new Dictionary<string, BmAspect>(StringComparer.OrdinalIgnoreCase);
        foreach (var aspect in model.Aspects)
        {
            var fullName = string.IsNullOrEmpty(aspect.Namespace) ? aspect.Name : $"{aspect.Namespace}.{aspect.Name}";
            lookup[fullName] = aspect;
            // Also add short name for lookup (e.g., "TenantAware" -> Core.TenantAware)
            if (!lookup.ContainsKey(aspect.Name))
                lookup[aspect.Name] = aspect;
        }
        return lookup;
    }

    /// <summary>
    /// Inlines behavioral rules and access controls from aspects into the model.
    /// </summary>
    private int InlineAspectBehaviors(BmModel model, CompilationContext context, Dictionary<string, BmAspect> aspectLookup)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            foreach (var aspectName in entity.Aspects.ToList())
            {
                // Resolve the full aspect chain
                var chain = new List<BmAspect>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!ResolveAspectChain(aspectName, aspectLookup, visited, chain, context))
                    continue;
                
                foreach (var aspect in chain)
                {
                    // Inline rules — clone with TargetEntity set to this entity
                    foreach (var rule in aspect.Rules)
                    {
                        var clonedRule = CloneRule(rule, entity.QualifiedName);
                        model.Rules.Add(clonedRule);
                        count++;
                    }
                    
                    // Inline access controls
                    foreach (var acl in aspect.AccessControls)
                    {
                        var clonedAcl = CloneAccessControl(acl, entity.QualifiedName);
                        model.AccessControls.Add(clonedAcl);
                        count++;
                    }
                    
                    // Inline compositions
                    foreach (var comp in aspect.Compositions)
                    {
                        if (!entity.Compositions.Any(c => string.Equals(c.Name, comp.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.Compositions.Add(comp);
                        }
                    }

                    // Inline bound actions from aspects
                    foreach (var action in aspect.Actions)
                    {
                        if (!entity.BoundActions.Any(a => string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.BoundActions.Add(action);
                            count++;
                        }
                    }

                    // Inline bound functions from aspects
                    foreach (var function in aspect.Functions)
                    {
                        if (!entity.BoundFunctions.Any(f => string.Equals(f.Name, function.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            entity.BoundFunctions.Add(function);
                            count++;
                        }
                    }
                }
            }
        }

        if (count > 0)
            context.AddInfo(ErrorCodes.OPT_INLINED_BEHAVIORS, $"Inlined {count} behavioral rules/access controls/actions/functions from aspects", Name);
        return count;
    }
    
    private BmRule CloneRule(BmRule source, string targetEntity)
    {
        var cloned = new BmRule
        {
            Name = source.Name,
            TargetEntity = targetEntity,
            SourceFile = source.SourceFile,
            StartLine = source.StartLine,
            EndLine = source.EndLine
        };
        // Share trigger/statement objects (they are immutable after compilation)
        // but create new list instances to avoid cross-entity list mutations
        foreach (var trigger in source.Triggers)
            cloned.Triggers.Add(trigger);
        foreach (var stmt in source.Statements)
            cloned.Statements.Add(stmt);
        foreach (var ann in source.Annotations)
            cloned.Annotations.Add(ann);
        return cloned;
    }
    
    private BmAccessControl CloneAccessControl(BmAccessControl source, string targetEntity)
    {
        var cloned = new BmAccessControl
        {
            Name = targetEntity,
            TargetEntity = targetEntity,
            ExtendsFrom = source.ExtendsFrom,
            SourceFile = source.SourceFile,
            StartLine = source.StartLine,
            EndLine = source.EndLine
        };
        cloned.Rules.AddRange(source.Rules);
        return cloned;
    }
    
    /// <summary>
    /// Generates synthetic BmView entries for aspects annotated with @Query.CrossAspect.
    /// These views allow runtime querying across all entities that share a common aspect.
    /// The naming convention matches PostgresDdlGenerator.GenerateCrossAspectViews().
    /// </summary>
    private int GenerateCrossAspectViews(BmModel model, CompilationContext context)
    {
        int count = 0;

        foreach (var aspect in model.Aspects)
        {
            if (!aspect.HasAnnotation("Query.CrossAspect"))
                continue;

            // Find all entities using this aspect (match by short or qualified name)
            var entitiesWithAspect = model.Entities
                .Where(e => e.Aspects.Any(a =>
                    string.Equals(a, aspect.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, aspect.QualifiedName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (entitiesWithAspect.Count == 0)
                continue;

            // Only include non-virtual aspect fields (matching DDL generator)
            var aspectFields = aspect.Fields.Where(f => !f.IsVirtual).ToList();
            if (aspectFields.Count == 0)
                continue;

            // Build view name matching DDL generator convention
            var viewName = NamingConvention.GetColumnName(aspect.Name) + "_cross_view";

            var view = new BmView
            {
                Name = viewName,
                Namespace = aspect.Namespace
            };

            // Mark as auto-generated cross-aspect view
            view.Annotations.Add(new BmAnnotation("CrossAspect.Source", aspect.QualifiedName));

            // Store the list of source entities as a comma-separated annotation
            var entityNames = string.Join(",", entitiesWithAspect.Select(e => e.QualifiedName));
            view.Annotations.Add(new BmAnnotation("CrossAspect.Entities", entityNames));

            model.Views.Add(view);
            count++;
        }

        if (count > 0)
            context.AddInfo(ErrorCodes.OPT_CROSS_ASPECT_VIEWS, $"Generated {count} cross-aspect view(s)", Name);

        return count;
    }

    private int DeduplicateTypes(BmModel model, CompilationContext context)
    {
        int count = 0;

        // Group types by structure signature
        var typeGroups = model.Types
            .GroupBy(t => GetTypeSignature(t))
            .Where(g => g.Count() > 1);

        foreach (var group in typeGroups)
        {
            var types = group.ToList();
            var canonical = types.First();

            // Mark and remove duplicates
            foreach (var duplicate in types.Skip(1))
            {
                context.AddInfo(ErrorCodes.OPT_DUPLICATE_TYPE,
                    $"Type '{duplicate.Name}' is duplicate of '{canonical.Name}'", Name);
                model.Types.Remove(duplicate);
                count++;
            }
        }

        return count;
    }
    
    private string GetTypeSignature(BmType type)
    {
        // For primitive type aliases (e.g., type Amount : Decimal(15, 2))
        // they have no fields but have a BaseType - use BaseType as signature
        if (type.Fields.Count == 0)
        {
            // Include BaseType in signature to distinguish type aliases
            return $"alias:{type.BaseType ?? "unknown"}";
        }
        
        // For structured types, create a signature based on field names and types
        var fields = type.Fields
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}:{f.TypeString}")
            .ToList();
        
        return string.Join("|", fields);
    }
    
    private BmField CloneField(BmField source)
    {
        var cloned = new BmField
        {
            Name = source.Name,
            TypeString = source.TypeString,
            IsKey = source.IsKey,
            IsNullable = source.IsNullable,
            IsVirtual = source.IsVirtual,
            IsReadonly = source.IsReadonly,
            IsImmutable = source.IsImmutable,
            IsComputed = source.IsComputed,
            IsStored = source.IsStored,
            ComputedStrategy = source.ComputedStrategy,
            DefaultValueString = source.DefaultValueString,
            DefaultExpr = source.DefaultExpr,
            ComputedExprString = source.ComputedExprString,
            ComputedExpr = source.ComputedExpr
        };
        // Copy annotations
        foreach (var ann in source.Annotations)
            cloned.Annotations.Add(ann);
        return cloned;
    }
}
