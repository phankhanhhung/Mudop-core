using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4.4: Inheritance Resolution
/// Resolves entity inheritance chains (table-per-type), detecting circular inheritance
/// and propagating parent fields/aspects/associations to children for runtime use.
/// </summary>
public class InheritanceResolutionPass : ICompilerPass
{
    public string Name => "InheritanceResolution";
    public string Description => "Resolve entity inheritance hierarchy";
    public int Order => 44; // After SymbolResolution (40), before DependencyGraph (45)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.INH_NO_MODEL, "No model available for inheritance resolution", pass: Name);
            return false;
        }
        
        var model = context.Model;
        bool hasErrors = false;
        
        // Build entity lookup
        var entityLookup = new Dictionary<string, BmEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            var fullName = string.IsNullOrEmpty(entity.Namespace) ? entity.Name : $"{entity.Namespace}.{entity.Name}";
            entityLookup[fullName] = entity;
            if (!entityLookup.ContainsKey(entity.Name))
                entityLookup[entity.Name] = entity;
        }
        
        // Resolve parent references
        foreach (var entity in model.Entities)
        {
            if (string.IsNullOrEmpty(entity.ParentEntityName))
                continue;
            
            if (!entityLookup.TryGetValue(entity.ParentEntityName, out var parent))
            {
                context.AddError(ErrorCodes.INH_PARENT_NOT_FOUND,
                    $"Parent entity '{entity.ParentEntityName}' not found for entity '{entity.Name}'", Name);
                hasErrors = true;
                continue;
            }
            
            entity.ParentEntity = parent;
            parent.DerivedEntities.Add(entity);
            
            // Set default discriminator value
            if (string.IsNullOrEmpty(entity.DiscriminatorValue))
                entity.DiscriminatorValue = entity.Name;
        }
        
        // Detect circular inheritance via DFS
        foreach (var entity in model.Entities.Where(e => e.ParentEntity != null))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = entity;
            while (current != null)
            {
                var key = current.QualifiedName;
                if (!visited.Add(key))
                {
                    context.AddError(ErrorCodes.INH_CIRCULAR,
                        $"Circular inheritance detected: entity '{entity.Name}' has a cycle involving '{current.Name}'", Name);
                    hasErrors = true;
                    break;
                }
                current = current.ParentEntity;
            }
        }
        
        if (hasErrors)
            return false;

        // Detect diamond inheritance: if an entity appears multiple times
        // in the inheritance chain (e.g., C extends B extends A, and C also extends A)
        foreach (var entity in model.Entities.Where(e => e.ParentEntity != null))
        {
            var ancestorVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = entity;
            while (current?.ParentEntity != null)
            {
                var parentKey = current.ParentEntity.QualifiedName;
                if (!ancestorVisited.Add(parentKey))
                {
                    context.AddWarning(ErrorCodes.INH_DIAMOND,
                        $"Diamond inheritance detected: entity '{entity.Name}' has ancestor '{current.ParentEntity.Name}' reachable through multiple paths", Name);
                    break;
                }
                current = current.ParentEntity;
            }
        }

        // Propagate parent fields/aspects to children (for runtime convenience)
        // Process in topological order (parents before children)
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            PropagateInheritedMembers(entity, processed, context);
        }
        
        var inheritedCount = model.Entities.Count(e => e.ParentEntity != null);
        if (inheritedCount > 0)
            context.AddInfo(ErrorCodes.INH_SUMMARY, $"Resolved {inheritedCount} entity inheritance relationships", Name);
        
        return true;
    }
    
    private void PropagateInheritedMembers(BmEntity entity, HashSet<string> processed, CompilationContext context)
    {
        if (processed.Contains(entity.QualifiedName))
            return;

        // Process parent first (ensures grandparent -> parent -> child order)
        if (entity.ParentEntity != null)
        {
            PropagateInheritedMembers(entity.ParentEntity, processed, context);

            var parent = entity.ParentEntity;

            // Propagate parent's aspects (deduplicate by name)
            foreach (var aspect in parent.Aspects)
            {
                if (!entity.Aspects.Any(a => string.Equals(a, aspect, StringComparison.OrdinalIgnoreCase)))
                    entity.Aspects.Add(aspect);
            }

            // Propagate parent's fields (skip fields already defined by the child = override)
            // Use a set to track existing field names and avoid duplicates from diamond inheritance
            var existingFieldNames = new HashSet<string>(
                entity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var parentField in parent.Fields)
            {
                if (!existingFieldNames.Contains(parentField.Name))
                {
                    entity.Fields.Add(parentField);
                    existingFieldNames.Add(parentField.Name);
                }
            }

            // Propagate parent's associations (deduplicate by name)
            var existingAssocNames = new HashSet<string>(
                entity.Associations.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var parentAssoc in parent.Associations)
            {
                if (!existingAssocNames.Contains(parentAssoc.Name))
                {
                    entity.Associations.Add(parentAssoc);
                    existingAssocNames.Add(parentAssoc.Name);
                }
            }

            // Propagate parent's compositions (deduplicate by name)
            var existingCompNames = new HashSet<string>(
                entity.Compositions.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var parentComp in parent.Compositions)
            {
                if (!existingCompNames.Contains(parentComp.Name))
                {
                    entity.Compositions.Add(parentComp);
                    existingCompNames.Add(parentComp.Name);
                }
            }
        }

        // Final deduplication pass: remove any duplicate fields that may have accumulated
        // from multiple inheritance paths (diamond pattern)
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = entity.Fields.Count - 1; i >= 0; i--)
        {
            if (!seenFields.Add(entity.Fields[i].Name))
            {
                entity.Fields.RemoveAt(i);
            }
        }

        processed.Add(entity.QualifiedName);
    }
}
