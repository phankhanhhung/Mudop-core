using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 5.5: Extension Merge
/// Applies "extend entity/type/aspect/service" definitions by merging fields, aspects, 
/// associations and compositions into the target entity.
/// </summary>
public class ExtensionMergePass : ICompilerPass
{
    public string Name => "ExtensionMerge";
    public string Description => "Merge extend definitions into target entities";
    public int Order => 55; // After Semantic Validation (50), before Optimization (60)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.EXT_NO_MODEL, "No model available for extension merge", pass: Name);
            return false;
        }
        
        var model = context.Model;
        int merged = 0;
        
        foreach (var ext in model.Extensions)
        {
            switch (ext.TargetKind)
            {
                case "entity":
                    if (MergeIntoEntity(ext, model, context))
                        merged++;
                    break;
                case "type":
                    if (MergeIntoType(ext, model, context))
                        merged++;
                    break;
                case "aspect":
                    if (MergeIntoAspect(ext, model, context))
                        merged++;
                    break;
                case "service":
                    if (MergeIntoService(ext, model, context))
                        merged++;
                    break;
                case "enum":
                    if (MergeIntoEnum(ext, model, context))
                        merged++;
                    break;
                default:
                    context.AddWarning(ErrorCodes.EXT_UNSUPPORTED_KIND,
                        $"Extension target kind '{ext.TargetKind}' not yet supported", Name);
                    break;
            }
        }
        
        if (merged > 0)
            context.AddInfo(ErrorCodes.EXT_INFO, $"Merged {merged} extension definitions", Name);
        
        return true;
    }
    
    private bool MergeIntoEntity(BmExtension ext, BmModel model, CompilationContext context)
    {
        var target = model.Entities.FirstOrDefault(e => 
            string.Equals(e.Name, ext.TargetName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(e.QualifiedName, ext.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (target == null)
        {
            context.AddError(ErrorCodes.EXT_TARGET_NOT_FOUND, 
                $"Extension target entity '{ext.TargetName}' not found", Name);
            return false;
        }
        
        // Merge WITH aspects
        foreach (var aspectName in ext.WithAspects)
        {
            if (!target.Aspects.Any(a => string.Equals(a, aspectName, StringComparison.OrdinalIgnoreCase)))
                target.Aspects.Add(aspectName);
        }
        
        // Merge fields
        foreach (var field in ext.Fields)
        {
            if (field.IsKey)
            {
                context.AddError(ErrorCodes.EXT_KEY_REDEFINITION,
                    $"Extension of entity '{target.Name}' cannot redefine key field '{field.Name}'", Name);
                continue;
            }
            if (target.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
            {
                context.AddWarning(ErrorCodes.EXT_DUPLICATE_FIELD,
                    $"Extension field '{field.Name}' already exists in entity '{target.Name}', skipping", Name);
                continue;
            }
            target.Fields.Add(field);
        }
        
        // Merge associations
        foreach (var assoc in ext.Associations)
        {
            if (!target.Associations.Any(a => string.Equals(a.Name, assoc.Name, StringComparison.OrdinalIgnoreCase)))
                target.Associations.Add(assoc);
        }
        
        // Merge compositions
        foreach (var comp in ext.Compositions)
        {
            if (!target.Compositions.Any(c => string.Equals(c.Name, comp.Name, StringComparison.OrdinalIgnoreCase)))
                target.Compositions.Add(comp);
        }

        // Merge bound actions
        foreach (var action in ext.Actions)
        {
            if (!target.BoundActions.Any(a => string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase)))
                target.BoundActions.Add(action);
        }

        // Merge bound functions
        foreach (var function in ext.Functions)
        {
            if (!target.BoundFunctions.Any(f => string.Equals(f.Name, function.Name, StringComparison.OrdinalIgnoreCase)))
                target.BoundFunctions.Add(function);
        }

        // Merge indexes
        foreach (var index in ext.Indexes)
        {
            if (!target.Indexes.Any(i => string.Equals(i.Name, index.Name, StringComparison.OrdinalIgnoreCase)))
                target.Indexes.Add(index);
        }

        // Merge constraints
        foreach (var constraint in ext.Constraints)
        {
            if (!target.Constraints.Any(c => string.Equals(c.Name, constraint.Name, StringComparison.OrdinalIgnoreCase)))
                target.Constraints.Add(constraint);
        }

        return true;
    }

    private bool MergeIntoType(BmExtension ext, BmModel model, CompilationContext context)
    {
        var target = model.Types.FirstOrDefault(t => 
            string.Equals(t.Name, ext.TargetName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(t.QualifiedName, ext.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (target == null)
        {
            context.AddError(ErrorCodes.EXT_TARGET_NOT_FOUND, 
                $"Extension target type '{ext.TargetName}' not found", Name);
            return false;
        }
        
        foreach (var field in ext.Fields)
        {
            if (target.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
            {
                context.AddWarning(ErrorCodes.EXT_DUPLICATE_FIELD, 
                    $"Extension field '{field.Name}' already exists in type '{target.Name}', skipping", Name);
                continue;
            }
            target.Fields.Add(field);
        }
        
        return true;
    }
    
    private bool MergeIntoAspect(BmExtension ext, BmModel model, CompilationContext context)
    {
        var target = model.Aspects.FirstOrDefault(a => 
            string.Equals(a.Name, ext.TargetName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(a.QualifiedName, ext.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (target == null)
        {
            context.AddError(ErrorCodes.EXT_TARGET_NOT_FOUND, 
                $"Extension target aspect '{ext.TargetName}' not found", Name);
            return false;
        }
        
        foreach (var field in ext.Fields)
        {
            if (target.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
            {
                context.AddWarning(ErrorCodes.EXT_DUPLICATE_FIELD, 
                    $"Extension field '{field.Name}' already exists in aspect '{target.Name}', skipping", Name);
                continue;
            }
            target.Fields.Add(field);
        }
        
        foreach (var assoc in ext.Associations)
        {
            if (!target.Associations.Any(a => string.Equals(a.Name, assoc.Name, StringComparison.OrdinalIgnoreCase)))
                target.Associations.Add(assoc);
        }
        
        return true;
    }

    
    private bool MergeIntoService(BmExtension ext, BmModel model, CompilationContext context)
    {
        var target = model.Services.FirstOrDefault(s => 
            string.Equals(s.Name, ext.TargetName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(s.QualifiedName, ext.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (target == null)
        {
            context.AddError(ErrorCodes.EXT_TARGET_NOT_FOUND, 
                $"Extension target service '{ext.TargetName}' not found", Name);
            return false;
        }
        
        // Merge annotations
        foreach (var annotation in ext.Annotations)
        {
            target.Annotations.Add(annotation);
        }

        // Merge entity exposures
        foreach (var entity in ext.ServiceEntities)
        {
            if (!target.Entities.Any(e => string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase)))
                target.Entities.Add(entity);
        }

        // Merge actions
        foreach (var action in ext.Actions)
        {
            if (!target.Actions.Any(a => string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase)))
                target.Actions.Add(action);
        }

        // Merge functions
        foreach (var function in ext.Functions)
        {
            if (!target.Functions.Any(f => string.Equals(f.Name, function.Name, StringComparison.OrdinalIgnoreCase)))
                target.Functions.Add(function);
        }

        return true;
    }

    private bool MergeIntoEnum(BmExtension ext, BmModel model, CompilationContext context)
    {
        var target = model.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, ext.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, ext.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            context.AddError(ErrorCodes.EXT_TARGET_NOT_FOUND,
                $"Extension target enum '{ext.TargetName}' not found", Name);
            return false;
        }

        // Merge enum values
        foreach (var enumValue in ext.EnumValues)
        {
            if (target.Values.Any(v => string.Equals(v.Name, enumValue.Name, StringComparison.OrdinalIgnoreCase)))
            {
                context.AddWarning(ErrorCodes.EXT_DUPLICATE_ENUM_MEMBER,
                    $"Extension enum member '{enumValue.Name}' already exists in enum '{target.Name}', skipping", Name);
                continue;
            }
            target.Values.Add(enumValue);
        }

        // Merge annotations
        foreach (var annotation in ext.Annotations)
        {
            target.Annotations.Add(annotation);
        }

        return true;
    }
}
