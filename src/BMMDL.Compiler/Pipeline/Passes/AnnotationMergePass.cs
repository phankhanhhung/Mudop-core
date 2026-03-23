using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 5.4: Annotation Merge
/// Merges external annotate directives (annotate Entity with { ... }) into their target entities/fields.
/// </summary>
public class AnnotationMergePass : ICompilerPass
{
    public string Name => "AnnotationMerge";
    public string Description => "Merge annotate directives into target entities";
    public int Order => 54; // After SymbolResolution (40), before Optimization (60)
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.ANN_NO_MODEL, "No model available for annotation merge", pass: Name);
            return false;
        }
        
        var model = context.Model;
        int merged = 0;
        int removed = 0;
        int warnings = 0;
        
        foreach (var directive in model.AnnotateDirectives)
        {
            // Try to find the target by name across all annotatable element kinds:
            // entities, services, types, enums
            var entity = FindTargetEntity(model, directive);
            
            if (entity != null)
            {
                if (directive.TargetField == null)
                {
                    // Entity-level annotation
                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(entity.Annotations, annotation.Name);
                        }
                        else
                        {
                            entity.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                else
                {
                    // Field-level annotation
                    var field = entity.Fields.FirstOrDefault(f =>
                        string.Equals(f.Name, directive.TargetField, StringComparison.OrdinalIgnoreCase));

                    if (field == null)
                    {
                        context.AddWarning(ErrorCodes.ANN_FIELD_NOT_FOUND,
                            $"Field '{directive.TargetField}' not found on entity '{directive.TargetName}'",
                            file: directive.SourceFile,
                            line: directive.Line,
                            pass: Name);
                        warnings++;
                        continue;
                    }

                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(field.Annotations, annotation.Name);
                        }
                        else
                        {
                            field.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                continue;
            }
            
            // Try service
            var service = FindTargetService(model, directive);
            if (service != null)
            {
                foreach (var annotation in directive.Annotations)
                {
                    if (IsAnnotationRemoval(annotation))
                    {
                        removed += RemoveAnnotation(service.Annotations, annotation.Name);
                    }
                    else
                    {
                        service.Annotations.Add(annotation);
                        merged++;
                    }
                }
                continue;
            }
            
            // Try type
            var type = FindTargetType(model, directive);
            if (type != null)
            {
                if (directive.TargetField == null)
                {
                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(type.Annotations, annotation.Name);
                        }
                        else
                        {
                            type.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                else
                {
                    var field = type.Fields.FirstOrDefault(f =>
                        string.Equals(f.Name, directive.TargetField, StringComparison.OrdinalIgnoreCase));

                    if (field == null)
                    {
                        context.AddWarning(ErrorCodes.ANN_FIELD_NOT_FOUND,
                            $"Field '{directive.TargetField}' not found on type '{directive.TargetName}'",
                            file: directive.SourceFile,
                            line: directive.Line,
                            pass: Name);
                        warnings++;
                        continue;
                    }

                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(field.Annotations, annotation.Name);
                        }
                        else
                        {
                            field.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                continue;
            }
            
            // Try enum
            var enumDef = FindTargetEnum(model, directive);
            if (enumDef != null)
            {
                foreach (var annotation in directive.Annotations)
                {
                    if (IsAnnotationRemoval(annotation))
                    {
                        removed += RemoveAnnotation(enumDef.Annotations, annotation.Name);
                    }
                    else
                    {
                        enumDef.Annotations.Add(annotation);
                        merged++;
                    }
                }
                continue;
            }

            // Try aspect
            var aspect = FindTargetAspect(model, directive);
            if (aspect != null)
            {
                if (directive.TargetField == null)
                {
                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(aspect.Annotations, annotation.Name);
                        }
                        else
                        {
                            aspect.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                else
                {
                    var field = aspect.Fields.FirstOrDefault(f =>
                        string.Equals(f.Name, directive.TargetField, StringComparison.OrdinalIgnoreCase));

                    if (field == null)
                    {
                        context.AddWarning(ErrorCodes.ANN_FIELD_NOT_FOUND,
                            $"Field '{directive.TargetField}' not found on aspect '{directive.TargetName}'",
                            file: directive.SourceFile,
                            line: directive.Line,
                            pass: Name);
                        warnings++;
                        continue;
                    }

                    foreach (var annotation in directive.Annotations)
                    {
                        if (IsAnnotationRemoval(annotation))
                        {
                            removed += RemoveAnnotation(field.Annotations, annotation.Name);
                        }
                        else
                        {
                            field.Annotations.Add(annotation);
                            merged++;
                        }
                    }
                }
                continue;
            }

            // None found
            context.AddWarning(ErrorCodes.ANN_TARGET_NOT_FOUND,
                $"Annotate target '{directive.TargetName}' not found",
                file: directive.SourceFile,
                line: directive.Line,
                pass: Name);
            warnings++;
        }
        
        if (merged > 0 || removed > 0 || warnings > 0)
        {
            context.AddInfo(ErrorCodes.ANN_SUMMARY,
                $"Merged {merged} annotations, removed {removed} annotations from annotate directives ({warnings} warnings)", Name);
        }
        
        return true;
    }
    
    private static BmEntity? FindTargetEntity(BmModel model, BmAnnotateDirective directive)
    {
        // Try exact match first
        var entity = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, directive.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, directive.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (entity != null)
            return entity;
        
        // Try namespace-qualified match if the directive has a namespace
        if (!string.IsNullOrEmpty(directive.Namespace))
        {
            var qualifiedTarget = $"{directive.Namespace}.{directive.TargetName}";
            entity = model.Entities.FirstOrDefault(e =>
                string.Equals(e.QualifiedName, qualifiedTarget, StringComparison.OrdinalIgnoreCase));
        }
        
        return entity;
    }

    
    private static BmService? FindTargetService(BmModel model, BmAnnotateDirective directive)
    {
        var service = model.Services.FirstOrDefault(s =>
            string.Equals(s.Name, directive.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.QualifiedName, directive.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (service != null)
            return service;
        
        if (!string.IsNullOrEmpty(directive.Namespace))
        {
            var qualifiedTarget = $"{directive.Namespace}.{directive.TargetName}";
            service = model.Services.FirstOrDefault(s =>
                string.Equals(s.QualifiedName, qualifiedTarget, StringComparison.OrdinalIgnoreCase));
        }
        
        return service;
    }
    
    private static BmType? FindTargetType(BmModel model, BmAnnotateDirective directive)
    {
        var type = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, directive.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.QualifiedName, directive.TargetName, StringComparison.OrdinalIgnoreCase));
        
        if (type != null)
            return type;
        
        if (!string.IsNullOrEmpty(directive.Namespace))
        {
            var qualifiedTarget = $"{directive.Namespace}.{directive.TargetName}";
            type = model.Types.FirstOrDefault(t =>
                string.Equals(t.QualifiedName, qualifiedTarget, StringComparison.OrdinalIgnoreCase));
        }
        
        return type;
    }
    
    private static BmEnum? FindTargetEnum(BmModel model, BmAnnotateDirective directive)
    {
        var enumDef = model.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, directive.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.QualifiedName, directive.TargetName, StringComparison.OrdinalIgnoreCase));

        if (enumDef != null)
            return enumDef;

        if (!string.IsNullOrEmpty(directive.Namespace))
        {
            var qualifiedTarget = $"{directive.Namespace}.{directive.TargetName}";
            enumDef = model.Enums.FirstOrDefault(e =>
                string.Equals(e.QualifiedName, qualifiedTarget, StringComparison.OrdinalIgnoreCase));
        }

        return enumDef;
    }

    private static BmAspect? FindTargetAspect(BmModel model, BmAnnotateDirective directive)
    {
        var aspect = model.Aspects.FirstOrDefault(a =>
            string.Equals(a.Name, directive.TargetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.QualifiedName, directive.TargetName, StringComparison.OrdinalIgnoreCase));

        if (aspect != null)
            return aspect;

        if (!string.IsNullOrEmpty(directive.Namespace))
        {
            var qualifiedTarget = $"{directive.Namespace}.{directive.TargetName}";
            aspect = model.Aspects.FirstOrDefault(a =>
                string.Equals(a.QualifiedName, qualifiedTarget, StringComparison.OrdinalIgnoreCase));
        }

        return aspect;
    }

    /// <summary>
    /// Check if an annotation represents a removal marker.
    /// Convention: if the annotation value is the literal null, it signals removal.
    /// </summary>
    private static bool IsAnnotationRemoval(BmAnnotation annotation)
    {
        // An annotation with a null Value and no Properties is a removal marker.
        // However, annotations without explicit values also have null Value, so we need
        // a more specific check: the annotation value is explicitly set to the null literal.
        // We use the convention that Value == null AND Properties == null means removal
        // when the annotation is part of an annotate directive (the context where removal makes sense).
        // A more robust approach: check if the annotation's string value is "null" literal.
        return annotation.Value is string strVal &&
               string.Equals(strVal, "null", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Remove all annotations with the given name from the list.
    /// Returns the count of removed annotations.
    /// </summary>
    private static int RemoveAnnotation(List<BmAnnotation> annotations, string name)
    {
        return annotations.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
