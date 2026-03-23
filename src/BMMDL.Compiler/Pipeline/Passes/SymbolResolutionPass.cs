using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 4: Symbol Resolution
/// Registers all symbols and resolves cross-references.
/// </summary>
public class SymbolResolutionPass : ICompilerPass
{
    public string Name => "Symbol Resolution";
    public string Description => "Resolve symbol references";
    public int Order => 40;

    private static readonly HashSet<string> s_builtInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "String", "Integer", "Decimal", "Boolean", "Date", "Time", "DateTime", "Timestamp",
        "UUID", "Binary", "Int32", "Int64", "Float", "Double", "Bool", "Byte", "Char",
        "LocalDate", "LocalTime", "Instant", "Duration", "Guid", "Void"
    };
    
    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.SYM_NO_MODEL, "No model available for symbol resolution", pass: Name);
            return false;
        }
        
        var model = context.Model;
        int resolvedRefs = 0;
        bool success = true;
        
        // Phase 1: Register all symbols
        RegisterSymbols(context, model);
        
        // Phase 2: Resolve references
        resolvedRefs += ResolveEntityReferences(context, model);
        resolvedRefs += ResolveRuleReferences(context, model);
        resolvedRefs += ResolveAccessControlReferences(context, model);
        resolvedRefs += ResolveProjectionReferences(context, model);
        resolvedRefs += ResolveTypeBaseTypes(context, model);
        resolvedRefs += ResolveServiceOperationTypes(context, model);
        resolvedRefs += ResolveEventFieldTypes(context, model);
        resolvedRefs += ResolveViewReferences(context, model);
        resolvedRefs += ResolveSeedReferences(context, model);
        
        context.ResolvedReferences = resolvedRefs;
        
        // Check for unresolved references
        if (context.Diagnostics.Any(d => d.Code.StartsWith("SYM") && d.Severity == DiagnosticSeverity.Error))
            success = false;
        
        return success;
    }
    
    private void RegisterSymbols(CompilationContext context, BmModel model)
    {
        var symbols = context.Symbols;
        
        // Register entities
        foreach (var entity in model.Entities)
        {
            var qn = entity.QualifiedName;
            
            symbols.Register(qn, SymbolKind.Entity, entity.SourceFile, entity.StartLine, entity);
            
            // Register entity fields
            foreach (var field in entity.Fields)
            {
                symbols.Register($"{qn}.{field.Name}", SymbolKind.Field, entity.SourceFile, field.StartLine, field);
            }
            
            // Register entity associations
            foreach (var assoc in entity.Associations)
            {
                symbols.Register($"{qn}.{assoc.Name}", SymbolKind.Association, entity.SourceFile, assoc.StartLine, assoc);
            }
        }
        
        // Register types
        foreach (var type in model.Types)
        {
            var qn = type.QualifiedName;
            symbols.Register(qn, SymbolKind.Type, type.SourceFile, type.StartLine, type);
        }
        
        // Register enums
        foreach (var en in model.Enums)
        {
            var qn = en.QualifiedName;
            symbols.Register(qn, SymbolKind.Enum, en.SourceFile, en.StartLine, en);
        }
        
        // Register aspects
        foreach (var aspect in model.Aspects)
        {
            var qn = aspect.QualifiedName;
            symbols.Register(qn, SymbolKind.Aspect, aspect.SourceFile, aspect.StartLine, aspect);
        }
        
        // Register services
        foreach (var service in model.Services)
        {
            var qn = service.QualifiedName;
            symbols.Register(qn, SymbolKind.Service, service.SourceFile, service.StartLine, service);
            
            foreach (var func in service.Functions)
                symbols.Register($"{qn}.{func.Name}", SymbolKind.Function, service.SourceFile, func.StartLine, func);
            
            foreach (var action in service.Actions)
                symbols.Register($"{qn}.{action.Name}", SymbolKind.Action, service.SourceFile, action.StartLine, action);
        }
        
        // Register rules
        foreach (var rule in model.Rules)
        {
            symbols.Register(rule.Name, SymbolKind.Rule, rule.SourceFile, rule.StartLine, rule);
        }
        
        // Register access controls
        foreach (var ac in model.AccessControls)
        {
            symbols.Register($"ACCESS_{ac.Name}", SymbolKind.AccessControl, ac.SourceFile, ac.StartLine, ac);
        }
        
        // Register events
        foreach (var ev in model.Events)
        {
            var qn = ev.QualifiedName;
            symbols.Register(qn, SymbolKind.Event, ev.SourceFile, ev.StartLine, ev);
        }
        
        // Register sequences
        foreach (var seq in model.Sequences)
        {
            symbols.Register(seq.QualifiedName, SymbolKind.Sequence, seq.SourceFile, seq.StartLine, seq);
        }
        
        // Register views
        foreach (var view in model.Views)
        {
            var qn = view.QualifiedName;
            symbols.Register(qn, SymbolKind.View, view.SourceFile, view.StartLine, view);
        }

        // Register seeds
        foreach (var seed in model.Seeds)
        {
            var qn = seed.QualifiedName;
            symbols.Register(qn, SymbolKind.Seed, seed.SourceFile, seed.StartLine, seed);
        }
    }
    
    private int ResolveEntityReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;
        
        foreach (var entity in model.Entities)
        {
            // Resolve aspects (BmEntity uses aspects, not inheritance)
            foreach (var aspect in entity.Aspects)
            {
                if (TryResolve(context, aspect, entity.SourceFile, entity.StartLine))
                    resolved++;
            }
            
            // Resolve field types
            foreach (var field in entity.Fields)
            {
                if (!string.IsNullOrEmpty(field.TypeString) && !IsBuiltInType(field.TypeString))
                {
                    if (TryResolve(context, ExtractTypeName(field.TypeString), entity.SourceFile, field.StartLine))
                        resolved++;
                }
            }
            
            // Resolve association targets
            foreach (var assoc in entity.Associations)
            {
                if (TryResolve(context, assoc.TargetEntity, entity.SourceFile, assoc.StartLine))
                    resolved++;
            }
            
            // Resolve composition targets
            foreach (var comp in entity.Compositions)
            {
                if (TryResolve(context, comp.TargetEntity, entity.SourceFile, comp.StartLine))
                    resolved++;
            }
        }
        
        return resolved;
    }
    
    private int ResolveRuleReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;
        
        foreach (var rule in model.Rules)
        {
            // Resolve target entity
            if (!string.IsNullOrEmpty(rule.TargetEntity))
            {
                if (TryResolve(context, rule.TargetEntity, rule.SourceFile, rule.StartLine))
                    resolved++;
            }
        }
        
        return resolved;
    }
    
    private int ResolveAccessControlReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;
        
        foreach (var ac in model.AccessControls)
        {
            // Resolve target entity
            if (!string.IsNullOrEmpty(ac.TargetEntity))
            {
                if (TryResolve(context, ac.TargetEntity, ac.SourceFile, ac.StartLine))
                    resolved++;
            }
        }
        
        return resolved;
    }
    
    private int ResolveProjectionReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;

        foreach (var view in model.Views)
        {
            if (!view.IsProjection || string.IsNullOrEmpty(view.ProjectionEntityName))
                continue;

            // Resolve the projection entity reference
            if (TryResolve(context, view.ProjectionEntityName, view.SourceFile, view.StartLine))
                resolved++;

            // For * excluding: expand fields from the source entity
            if (view.ProjectionIncludesAll && view.ExcludedFields.Count > 0)
            {
                var entity = model.FindEntity(view.ProjectionEntityName);
                if (entity != null)
                {
                    var excludeSet = new HashSet<string>(view.ExcludedFields, StringComparer.OrdinalIgnoreCase);
                    var includedFields = entity.Fields
                        .Where(f => !excludeSet.Contains(f.Name))
                        .Select(f => f.Name)
                        .ToList();

                    // Also add any explicit projection fields (mixed with *)
                    foreach (var pf in view.ProjectionFields)
                    {
                        var col = pf.Alias != null ? $"{pf.FieldName} AS {pf.Alias}" : pf.FieldName;
                        if (!includedFields.Contains(pf.FieldName))
                            includedFields.Add(col);
                    }

                    var columns = includedFields.Count > 0 ? string.Join(", ", includedFields) : "*";
                    view.SelectStatement = $"SELECT {columns} FROM {view.ProjectionEntityName}";
                }
                else
                {
                    context.AddWarning(ErrorCodes.SYM_UNRESOLVED_REF,
                        $"Cannot resolve projection entity '{view.ProjectionEntityName}' for view '{view.Name}'",
                        view.SourceFile, view.StartLine, Name);
                }
            }
        }

        return resolved;
    }

    private int ResolveTypeBaseTypes(CompilationContext context, BmModel model)
    {
        int resolved = 0;

        foreach (var type in model.Types)
        {
            if (string.IsNullOrEmpty(type.BaseType)) continue;

            var baseTypeName = ExtractTypeName(type.BaseType);
            if (!IsBuiltInType(baseTypeName))
            {
                if (TryResolve(context, baseTypeName, type.SourceFile, type.StartLine))
                    resolved++;
            }
            else
            {
                resolved++;
            }
        }

        return resolved;
    }

    private int ResolveServiceOperationTypes(CompilationContext context, BmModel model)
    {
        int resolved = 0;

        foreach (var service in model.Services)
        {
            foreach (var func in service.Functions.Concat(service.Actions.Cast<MetaModel.Service.BmFunction>()))
            {
                // Resolve return type
                if (!string.IsNullOrEmpty(func.ReturnType))
                {
                    var retTypeName = ExtractTypeName(func.ReturnType);
                    if (!IsBuiltInType(retTypeName))
                    {
                        if (TryResolve(context, retTypeName, service.SourceFile, func.StartLine))
                            resolved++;
                    }
                    else
                    {
                        resolved++;
                    }
                }

                // Resolve parameter types
                foreach (var param in func.Parameters)
                {
                    if (!string.IsNullOrEmpty(param.Type))
                    {
                        var paramTypeName = ExtractTypeName(param.Type);
                        if (!IsBuiltInType(paramTypeName))
                        {
                            if (TryResolve(context, paramTypeName, service.SourceFile, param.StartLine))
                                resolved++;
                        }
                        else
                        {
                            resolved++;
                        }
                    }
                }
            }
        }

        return resolved;
    }

    private int ResolveEventFieldTypes(CompilationContext context, BmModel model)
    {
        int resolved = 0;
        
        foreach (var ev in model.Events)
        {
            foreach (var field in ev.Fields)
            {
                if (!string.IsNullOrEmpty(field.TypeString) && !IsBuiltInType(field.TypeString))
                {
                    if (TryResolve(context, ExtractTypeName(field.TypeString), ev.SourceFile, ev.StartLine))
                        resolved++;
                }
            }
        }
        
        return resolved;
    }
    
    private int ResolveViewReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;
        
        foreach (var view in model.Views)
        {
            // Resolve projection entity reference
            if (view.IsProjection && !string.IsNullOrEmpty(view.ProjectionEntityName))
            {
                if (TryResolve(context, view.ProjectionEntityName, view.SourceFile, view.StartLine))
                    resolved++;
            }
        }
        
        return resolved;
    }

    private int ResolveSeedReferences(CompilationContext context, BmModel model)
    {
        int resolved = 0;

        foreach (var seed in model.Seeds)
        {
            // Resolve target entity reference
            if (!string.IsNullOrEmpty(seed.EntityName))
            {
                if (TryResolve(context, seed.EntityName, seed.SourceFile, seed.StartLine))
                    resolved++;
            }
        }

        return resolved;
    }

    private bool TryResolve(CompilationContext context, string name, string? file, int? line)
    {
        // Skip if clearly invalid
        if (string.IsNullOrEmpty(name)) return false;

        // Try exact match (root-level symbols or already-qualified names)
        if (context.Symbols.Contains(name))
            return true;

        // Try with model namespace
        if (context.Model?.Namespace != null)
        {
            var qualified = $"{context.Model.Namespace}.{name}";
            if (context.Symbols.Contains(qualified))
                return true;
        }

        // Try with imported namespaces (from module imports declarations)
        if (context.Model?.Module?.Imports != null)
        {
            foreach (var importedNs in context.Model.Module.Imports)
            {
                var qualified = $"{importedNs}.{name}";
                if (context.Symbols.Contains(qualified))
                    return true;
            }
        }

        // Try all known namespaces from all modules in this compilation
        if (context.Model?.AllModules != null)
        {
            foreach (var mod in context.Model.AllModules)
            {
                foreach (var pub in mod.Publishes)
                {
                    var qualified = $"{pub}.{name}";
                    if (context.Symbols.Contains(qualified))
                        return true;
                }
            }
        }

        // Built-in types don't need resolving
        if (IsBuiltInType(name))
            return true;

        // Not found - warning only (could be external reference)
        context.AddWarning(ErrorCodes.SYM_UNRESOLVED_REF, $"Unresolved reference: '{name}'", file, line, Name);
        return false;
    }
    
    private bool IsBuiltInType(string typeName)
    {
        // Handle array types: Array<ElementType> or array<ElementType>
        var baseName = typeName.Split('[', '(', '?', '<')[0].Trim();
        if (baseName.Equals("Array", StringComparison.OrdinalIgnoreCase))
        {
            // Check if the element type is built-in
            var elementType = ExtractArrayElementType(typeName);
            return elementType != null && IsBuiltInType(elementType);
        }
        return s_builtInTypes.Contains(baseName);
    }

    private string ExtractTypeName(string typeString)
    {
        // "String(200)" -> "String"
        // "array of Customer" -> "Customer"
        // "Array<Customer>" -> "Customer"
        // "UUID" -> "UUID"

        if (typeString.StartsWith("array of ", StringComparison.OrdinalIgnoreCase))
            return typeString[9..].Trim();

        // Handle Array<ElementType> — resolve the element type, not "Array"
        var baseName = typeString.Split('[', '(', '?', '<')[0].Trim();
        if (baseName.Equals("Array", StringComparison.OrdinalIgnoreCase))
        {
            var elementType = ExtractArrayElementType(typeString);
            if (elementType != null)
                return ExtractTypeName(elementType); // recurse for nested types
        }

        var idx = typeString.IndexOfAny(['(', '[', '<', '?']);
        return idx > 0 ? typeString[..idx] : typeString;
    }

    private static string? ExtractArrayElementType(string typeString)
    {
        var ltIdx = typeString.IndexOf('<');
        var gtIdx = typeString.LastIndexOf('>');
        if (ltIdx >= 0 && gtIdx > ltIdx)
            return typeString[(ltIdx + 1)..gtIdx].Trim();
        return null;
    }
}
