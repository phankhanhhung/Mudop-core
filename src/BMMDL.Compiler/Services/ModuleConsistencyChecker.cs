using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Services;

/// <summary>
/// Validates a new module against the existing meta model.
/// Ensures no conflicts or type compatibility issues.
/// </summary>
public class ModuleConsistencyChecker
{
    /// <summary>
    /// Check if the new module is consistent with the existing model.
    /// </summary>
    /// <param name="newModule">The new module being installed</param>
    /// <param name="existingModel">The current tenant's meta model (null for empty DB)</param>
    /// <param name="moduleNamespace">The namespace of the module being published (to exclude its own symbols from conflict check)</param>
    public ConsistencyResult Check(BmModel newModule, BmModel? existingModel, string? moduleNamespace = null)
    {
        var result = new ConsistencyResult();

        if (existingModel == null)
        {
            // Empty database - no conflicts possible
            return result;
        }

        // Build set of namespaces owned by the module being published.
        // Symbols in these namespaces are expected to already exist (re-publish/update).
        var ownedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(moduleNamespace))
        {
            ownedNamespaces.Add(moduleNamespace);
        }
        if (newModule.Module != null)
        {
            foreach (var ns in newModule.Module.Publishes)
                ownedNamespaces.Add(ns);
            // Module name itself is often the namespace
            ownedNamespaces.Add(newModule.Module.Name);
        }

        // 1. Symbol Conflict Check - No duplicate qualified names (excluding own module's symbols)
        CheckSymbolConflicts(newModule, existingModel, result, ownedNamespaces);

        // 2. Type Resolution Check - All referenced types exist
        CheckTypeResolution(newModule, existingModel, result);

        // 3. Extension Validity Check - Extended elements must exist
        CheckExtensionValidity(newModule, existingModel, result);

        return result;
    }
    
    private void CheckSymbolConflicts(BmModel newModel, BmModel existing, ConsistencyResult result, HashSet<string> ownedNamespaces)
    {
        // Helper: check if a symbol belongs to the module being published
        // (and therefore is expected to already exist in the registry)
        bool IsOwnedByModule(string? ns)
        {
            if (string.IsNullOrEmpty(ns)) return ownedNamespaces.Count > 0;
            return ownedNamespaces.Contains(ns);
        }

        // Check entities (skip extensions and skip re-published entities)
        foreach (var entity in newModel.Entities)
        {
            if (entity.ExtendsFrom != null) continue;
            if (IsOwnedByModule(entity.Namespace)) continue;

            if (existing.Entities.Any(e => e.QualifiedName == entity.QualifiedName))
            {
                result.Errors.Add(new ConsistencyError(
                    "SYMBOL_CONFLICT",
                    $"Entity '{entity.QualifiedName}' already exists in the installed meta model. " +
                    "Use 'extend entity' if you want to add fields to an existing entity.",
                    entity.SourceFile,
                    entity.StartLine));
            }
        }

        // Check types
        foreach (var type in newModel.Types)
        {
            if (IsOwnedByModule(type.Namespace)) continue;

            if (existing.Types.Any(t => t.QualifiedName == type.QualifiedName))
            {
                result.Errors.Add(new ConsistencyError(
                    "SYMBOL_CONFLICT",
                    $"Type '{type.QualifiedName}' already exists in the installed meta model",
                    type.SourceFile,
                    type.StartLine));
            }
        }

        // Check enums
        foreach (var enumDef in newModel.Enums)
        {
            if (IsOwnedByModule(enumDef.Namespace)) continue;

            if (existing.Enums.Any(e => e.QualifiedName == enumDef.QualifiedName))
            {
                result.Errors.Add(new ConsistencyError(
                    "SYMBOL_CONFLICT",
                    $"Enum '{enumDef.QualifiedName}' already exists in the installed meta model",
                    enumDef.SourceFile,
                    enumDef.StartLine));
            }
        }

        // Check services
        foreach (var service in newModel.Services)
        {
            if (IsOwnedByModule(service.Namespace)) continue;

            if (existing.Services.Any(s => s.QualifiedName == service.QualifiedName))
            {
                result.Errors.Add(new ConsistencyError(
                    "SYMBOL_CONFLICT",
                    $"Service '{service.QualifiedName}' already exists in the installed meta model",
                    service.SourceFile,
                    service.StartLine));
            }
        }
    }
    
    private void CheckTypeResolution(BmModel newModel, BmModel existing, ConsistencyResult result)
    {
        // Collect all known types (from both models)
        var allTypes = new HashSet<string>();
        
        // From existing model
        foreach (var e in existing.Entities) allTypes.Add(e.QualifiedName);
        foreach (var t in existing.Types) allTypes.Add(t.QualifiedName);
        foreach (var e in existing.Enums) allTypes.Add(e.QualifiedName);
        
        // From new module
        foreach (var e in newModel.Entities) allTypes.Add(e.QualifiedName);
        foreach (var t in newModel.Types) allTypes.Add(t.QualifiedName);
        foreach (var e in newModel.Enums) allTypes.Add(e.QualifiedName);
        
        // Check associations reference valid targets
        foreach (var entity in newModel.Entities)
        {
            foreach (var assoc in entity.Associations)
            {
                var targetType = assoc.TargetEntity;
                
                // Try with namespace prefix if not qualified
                if (!targetType.Contains('.') && !string.IsNullOrEmpty(entity.Namespace))
                {
                    targetType = $"{entity.Namespace}.{assoc.TargetEntity}";
                }
                
                if (!allTypes.Contains(targetType) && 
                    !allTypes.Contains(assoc.TargetEntity) &&
                    !IsBuiltInType(assoc.TargetEntity))
                {
                    result.Warnings.Add(new ConsistencyWarning(
                        "UNRESOLVED_ASSOCIATION",
                        $"Association target '{assoc.TargetEntity}' may not be found. " +
                        "Ensure the target entity is defined in this module or in an installed dependency.",
                        entity.SourceFile,
                        entity.StartLine));
                }
            }
        }
    }
    
    private void CheckExtensionValidity(BmModel newModel, BmModel existing, ConsistencyResult result)
    {
        // Find all extend definitions and verify target exists
        foreach (var entity in newModel.Entities.Where(e => e.ExtendsFrom != null))
        {
            var targetName = entity.ExtendsFrom!;
            
            // Try different qualified name combinations
            var found = existing.Entities.Any(e => 
                e.QualifiedName == targetName || 
                e.Name == targetName);
            
            if (!found)
            {
                result.Errors.Add(new ConsistencyError(
                    "INVALID_EXTENSION",
                    $"Cannot extend entity '{targetName}': not found in installed modules. " +
                    "Make sure the required module is installed first, or check the dependency declaration.",
                    entity.SourceFile,
                    entity.StartLine));
            }
        }
    }
    
    private static bool IsBuiltInType(string typeName)
    {
        var builtIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Decimal", "Boolean", 
            "Date", "Time", "Timestamp", "UUID", "Binary",
            "DateTime", "Int", "Int32", "Int64", "Float", "Double"
        };
        
        // Handle parameterized types like String(255)
        var baseName = typeName.Split('(')[0];
        return builtIns.Contains(baseName);
    }
}

/// <summary>
/// Result of consistency check.
/// </summary>
public class ConsistencyResult
{
    public bool IsConsistent => Errors.Count == 0;
    public List<ConsistencyError> Errors { get; } = new();
    public List<ConsistencyWarning> Warnings { get; } = new();
    
    /// <summary>
    /// Get a summary of all issues.
    /// </summary>
    public string GetSummary()
    {
        if (IsConsistent && Warnings.Count == 0)
        {
            return "Module is consistent with existing meta model.";
        }
        
        var lines = new List<string>();
        
        if (Errors.Count > 0)
        {
            lines.Add($"Found {Errors.Count} error(s):");
            foreach (var error in Errors)
            {
                lines.Add($"  âŒ [{error.Code}] {error.Message}");
            }
        }
        
        if (Warnings.Count > 0)
        {
            lines.Add($"Found {Warnings.Count} warning(s):");
            foreach (var warning in Warnings)
            {
                lines.Add($"  âš ï¸ [{warning.Code}] {warning.Message}");
            }
        }
        
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A consistency error that blocks installation.
/// </summary>
public record ConsistencyError(
    string Code, 
    string Message, 
    string? File, 
    int? Line);

/// <summary>
/// A consistency warning that should be reviewed but doesn't block installation.
/// </summary>
public record ConsistencyWarning(
    string Code, 
    string Message, 
    string? File, 
    int? Line);
