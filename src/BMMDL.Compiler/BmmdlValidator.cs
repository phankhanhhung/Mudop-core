using System.Diagnostics;
using BMMDL.MetaModel;
using BMMDL.Compiler.Parsing;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler;

/// <summary>
/// Validates BMMDL source files for syntax and semantic errors.
/// </summary>
public class BmmdlValidator
{
    private readonly bool _verbose;
    private readonly ICompilerOutput _output;
    private readonly List<ValidationError> _errors = new();
    private readonly List<string> _warnings = new();

    public BmmdlValidator(bool verbose = false, ICompilerOutput? output = null)
    {
        _verbose = verbose;
        _output = output ?? new ConsoleCompilerOutput();
    }

    /// <summary>
    /// Validate multiple BMMDL files.
    /// </summary>
    public bool ValidateFiles(IEnumerable<string> filePaths)
    {
        var files = filePaths.ToList();
        var sw = Stopwatch.StartNew();

        Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        Console.WriteLine("â•‘          BMMDL Compiler - Validation Mode                   â•‘");
        Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        Console.WriteLine();

        int totalFiles = files.Count;
        int passedFiles = 0;
        int failedFiles = 0;
        BmModel? combinedModel = null;

        foreach (var filePath in files)
        {
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  âœ— File not found: {filePath}");
                Console.ResetColor();
                _errors.Add(new ValidationError(filePath, 0, 0, "File not found"));
                failedFiles++;
                continue;
            }

            var result = ValidateFile(filePath);
            
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  âœ“ {Path.GetFileName(filePath)}");
                Console.ResetColor();

                if (_verbose)
                {
                    PrintModelSummary(result.Model!, "    ");
                }

                // Merge models
                if (combinedModel == null)
                    combinedModel = result.Model;
                else
                    combinedModel.Merge(result.Model!);

                passedFiles++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  âœ— {Path.GetFileName(filePath)}");
                Console.ResetColor();

                foreach (var error in result.Errors)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"      Line {error.Line}:{error.Column} - {error.Message}");
                    Console.ResetColor();
                    _errors.Add(error);
                }

                failedFiles++;
            }
        }

        sw.Stop();

        // Semantic validation on combined model
        if (combinedModel != null && passedFiles > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Performing semantic validation...");
            var semanticErrors = PerformSemanticValidation(combinedModel);
            
            if (semanticErrors.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var error in semanticErrors)
                {
                    Console.WriteLine($"  âš  {error}");
                    _warnings.Add(error);
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  âœ“ Semantic validation passed");
                Console.ResetColor();
            }
            
            // Dump model to text file
            var dumpPath = "metamodel_dump.txt";
            DumpModelToFile(combinedModel, dumpPath);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  ðŸ“„ Model dumped to: {dumpPath}");
            Console.ResetColor();
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        Console.WriteLine($"  Files: {totalFiles} total, {passedFiles} passed, {failedFiles} failed");
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms");
        
        if (_errors.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Result: âœ“ VALIDATION PASSED");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Result: âœ— VALIDATION FAILED ({_errors.Count} errors)");
            Console.ResetColor();
        }
        Console.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        return _errors.Count == 0;
    }

    /// <summary>
    /// Validate a single file.
    /// </summary>
    public ValidationResult ValidateFile(string filePath)
    {
        try
        {
            var source = File.ReadAllText(filePath);
            var compiler = new BmmdlCompiler();
            var model = compiler.Compile(source, filePath);

            return new ValidationResult
            {
                Success = true,
                Model = model,
                Errors = Array.Empty<ValidationError>()
            };
        }
        catch (BmmdlCompilationException ex)
        {
            return new ValidationResult
            {
                Success = false,
                Model = null,
                Errors = ex.Errors.Select(e => new ValidationError(
                    e.FileName ?? filePath,
                    e.Line,
                    e.Column,
                    e.Message
                )).ToArray()
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                Success = false,
                Model = null,
                Errors = new[] { new ValidationError(filePath, 0, 0, ex.Message) }
            };
        }
    }

    /// <summary>
    /// Parse files and display model structure.
    /// </summary>
    public void ParseAndDisplay(IEnumerable<string> filePaths)
    {
        Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        Console.WriteLine("â•‘          BMMDL Compiler - Parse Mode                        â•‘");
        Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        Console.WriteLine();

        var combinedModel = new BmModel();

        foreach (var filePath in filePaths)
        {
            var result = ValidateFile(filePath);
            if (result.Success && result.Model != null)
            {
                combinedModel.Merge(result.Model);
            }
        }

        PrintModelDetails(combinedModel);
    }

    private List<string> PerformSemanticValidation(BmModel model)
    {
        var errors = new List<string>();

        // Check for duplicate entity names
        var entityNames = model.Entities.GroupBy(e => e.QualifiedName)
            .Where(g => g.Count() > 1);
        foreach (var dup in entityNames)
        {
            errors.Add($"Duplicate entity definition: {dup.Key}");
        }

        // Check for duplicate type names
        var typeNames = model.Types.GroupBy(t => t.QualifiedName)
            .Where(g => g.Count() > 1);
        foreach (var dup in typeNames)
        {
            errors.Add($"Duplicate type definition: {dup.Key}");
        }

        // Check association targets exist
        foreach (var entity in model.Entities)
        {
            foreach (var assoc in entity.Associations)
            {
                var targetName = assoc.TargetEntity;
                var targetExists = model.Entities.Any(e => 
                    e.Name == targetName || e.QualifiedName == targetName);
                
                if (!targetExists)
                {
                    errors.Add($"Entity '{entity.Name}': Association '{assoc.Name}' references unknown entity '{targetName}'");
                }
            }
        }

        // Check aspect references exist
        foreach (var entity in model.Entities)
        {
            foreach (var aspectRef in entity.Aspects)
            {
                var aspectExists = model.Aspects.Any(a => 
                    a.Name == aspectRef || a.QualifiedName == aspectRef);
                var entityExists = model.Entities.Any(e => 
                    e.Name == aspectRef || e.QualifiedName == aspectRef);
                
                if (!aspectExists && !entityExists)
                {
                    errors.Add($"Entity '{entity.Name}': Unknown aspect or base entity '{aspectRef}'");
                }
            }
        }

        // Check rule targets exist
        foreach (var rule in model.Rules)
        {
            var targetExists = model.Entities.Any(e => 
                e.Name == rule.TargetEntity || e.QualifiedName == rule.TargetEntity);
            
            if (!targetExists)
            {
                errors.Add($"Rule '{rule.Name}': References unknown entity '{rule.TargetEntity}'");
            }
        }

        // Check access control targets exist
        foreach (var acl in model.AccessControls)
        {
            var targetExists = model.Entities.Any(e => 
                e.Name == acl.TargetEntity || e.QualifiedName == acl.TargetEntity);
            
            if (!targetExists)
            {
                errors.Add($"Access Control: References unknown entity '{acl.TargetEntity}'");
            }
        }

        return errors;
    }

    private void PrintModelSummary(BmModel model, string indent = "")
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{indent}Entities: {model.Entities.Count}, Types: {model.Types.Count}, " +
                         $"Enums: {model.Enums.Count}, Aspects: {model.Aspects.Count}");
        Console.WriteLine($"{indent}Services: {model.Services.Count}, Views: {model.Views.Count}, " +
                         $"Rules: {model.Rules.Count}, Sequences: {model.Sequences.Count}");
        Console.ResetColor();
    }

    private void PrintModelDetails(BmModel model)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Namespace: {model.Namespace ?? "(none)"}");
        Console.ResetColor();
        Console.WriteLine();

        // Entities
        if (model.Entities.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• ENTITIES ({model.Entities.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var entity in model.Entities)
            {
                Console.WriteLine($"  ðŸ“¦ {entity.QualifiedName}");
                if (entity.Aspects.Any())
                    Console.WriteLine($"     Includes: {string.Join(", ", entity.Aspects)}");
                
                foreach (var field in entity.Fields)
                {
                    var keyMark = field.IsKey ? "ðŸ”‘ " : "   ";
                    Console.WriteLine($"     {keyMark}{field.Name}: {field.TypeString}");
                }
                
                foreach (var assoc in entity.Associations)
                {
                    Console.WriteLine($"     â†’ {assoc.Name}: Association to {assoc.TargetEntity}");
                }
                
                foreach (var comp in entity.Compositions)
                {
                    Console.WriteLine($"     â—† {comp.Name}: Composition of {comp.TargetEntity}");
                }
                
                Console.WriteLine();
            }
        }

        // Types
        if (model.Types.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• TYPES ({model.Types.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var type in model.Types)
            {
                Console.WriteLine($"  ðŸ“ {type.Name}: {type.BaseType}");
            }
            Console.WriteLine();
        }

        // Enums
        if (model.Enums.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• ENUMS ({model.Enums.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var en in model.Enums)
            {
                Console.WriteLine($"  ðŸ”¢ {en.Name}");
                foreach (var val in en.Values)
                {
                    var valStr = val.Value != null ? $" = {val.Value}" : "";
                    Console.WriteLine($"       {val.Name}{valStr}");
                }
            }
            Console.WriteLine();
        }

        // Aspects
        if (model.Aspects.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• ASPECTS ({model.Aspects.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var aspect in model.Aspects)
            {
                Console.WriteLine($"  ðŸ§© {aspect.Name}");
                foreach (var field in aspect.Fields)
                {
                    Console.WriteLine($"       {field.Name}: {field.TypeString}");
                }
            }
            Console.WriteLine();
        }

        // Services
        if (model.Services.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• SERVICES ({model.Services.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var svc in model.Services)
            {
                Console.WriteLine($"  ðŸŒ {svc.Name}");
                Console.WriteLine($"       Entities: {svc.Entities.Count}, Functions: {svc.Functions.Count}, Actions: {svc.Actions.Count}");
            }
            Console.WriteLine();
        }

        // Views
        if (model.Views.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• VIEWS ({model.Views.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var view in model.Views)
            {
                var paramStr = view.Parameters.Any() 
                    ? $"({string.Join(", ", view.Parameters.Select(p => $"{p.Name}: {p.Type}"))})" 
                    : "";
                Console.WriteLine($"  ðŸ‘ {view.Name}{paramStr}");
            }
            Console.WriteLine();
        }

        // Rules
        if (model.Rules.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• RULES ({model.Rules.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var rule in model.Rules)
            {
                Console.WriteLine($"  ðŸ“‹ {rule.Name} for {rule.TargetEntity}");
                Console.WriteLine($"       Triggers: {rule.Triggers.Count}, Statements: {rule.Statements.Count}");
            }
            Console.WriteLine();
        }

        // Sequences
        if (model.Sequences.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• SEQUENCES ({model.Sequences.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var seq in model.Sequences)
            {
                Console.WriteLine($"  ðŸ”¢ {seq.Name}");
                if (seq.Pattern != null)
                    Console.WriteLine($"       Pattern: {seq.Pattern}");
                Console.WriteLine($"       Scope: {seq.Scope}, Reset: {seq.ResetOn}");
            }
            Console.WriteLine();
        }

        // Access Controls
        if (model.AccessControls.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"â•â•â• ACCESS CONTROLS ({model.AccessControls.Count}) â•â•â•");
            Console.ResetColor();
            
            foreach (var acl in model.AccessControls)
            {
                Console.WriteLine($"  ðŸ” {acl.TargetEntity}");
                Console.WriteLine($"       Rules: {acl.Rules.Count}");
            }
            Console.WriteLine();
        }
    }
    
    private void DumpModelToFile(BmModel model, string path)
    {
        using var writer = new StreamWriter(path);
        
        writer.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        writer.WriteLine("â•‘                     BMMDL META MODEL DUMP                                    â•‘");
        writer.WriteLine($"â•‘  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                                         â•‘");
        writer.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        writer.WriteLine();
        writer.WriteLine($"Namespace: {model.Namespace ?? "(none)"}");
        writer.WriteLine();
        
        // Summary
        writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        writer.WriteLine("SUMMARY");
        writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        writer.WriteLine($"  Entities:     {model.Entities.Count}");
        writer.WriteLine($"  Types:        {model.Types.Count}");
        writer.WriteLine($"  Enums:        {model.Enums.Count}");
        writer.WriteLine($"  Aspects:      {model.Aspects.Count}");
        writer.WriteLine($"  Services:     {model.Services.Count}");
        writer.WriteLine($"  Views:        {model.Views.Count}");
        writer.WriteLine($"  Rules:        {model.Rules.Count}");
        writer.WriteLine($"  Sequences:    {model.Sequences.Count}");
        writer.WriteLine($"  AccessCtrls:  {model.AccessControls.Count}");
        writer.WriteLine();
        
        // Entities
        if (model.Entities.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"ENTITIES ({model.Entities.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var entity in model.Entities)
            {
                writer.WriteLine();
                writer.WriteLine($"  entity {entity.QualifiedName}");
                if (entity.Aspects.Any())
                    writer.WriteLine($"    : {string.Join(", ", entity.Aspects)}");
                writer.WriteLine($"    [Source: {entity.SourceFile}:{entity.StartLine}-{entity.EndLine}]");
                
                // Annotations
                foreach (var ann in entity.Annotations)
                {
                    writer.WriteLine($"    @{ann.Name}: {ann.Value}");
                }
                
                // Fields
                if (entity.Fields.Any())
                {
                    writer.WriteLine("    Fields:");
                    foreach (var field in entity.Fields)
                    {
                        var keyMark = field.IsKey ? "[KEY] " : "";
                        var typeInfo = field.TypeRef != null ? $"({field.TypeRef.GetType().Name})" : "";
                        var defaultInfo = field.DefaultExpr != null 
                            ? $" = {field.DefaultExpr.ToExpressionString()} ({field.DefaultExpr.GetType().Name})" 
                            : (field.DefaultValueString != null ? $" = \"{field.DefaultValueString}\"" : "");
                        writer.WriteLine($"      {keyMark}{field.Name}: {field.TypeString} {typeInfo}{defaultInfo}");
                    }
                }
                
                // Associations
                if (entity.Associations.Any())
                {
                    writer.WriteLine("    Associations:");
                    foreach (var assoc in entity.Associations)
                    {
                        var condInfo = assoc.OnConditionExpr != null
                            ? $" ON {assoc.OnConditionExpr.ToExpressionString()}"
                            : "";
                        writer.WriteLine($"      {assoc.Name} -> {assoc.TargetEntity}{condInfo}");
                    }
                }
                
                // Compositions
                if (entity.Compositions.Any())
                {
                    writer.WriteLine("    Compositions:");
                    foreach (var comp in entity.Compositions)
                    {
                        writer.WriteLine($"      {comp.Name} -> {comp.TargetEntity}");
                    }
                }
            }
            writer.WriteLine();
        }
        
        // Types
        if (model.Types.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"TYPES ({model.Types.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var type in model.Types)
            {
                writer.WriteLine($"  type {type.QualifiedName}: {type.BaseType}");
                if (type.Length.HasValue) writer.WriteLine($"    Length: {type.Length}");
                if (type.Precision.HasValue) writer.WriteLine($"    Precision: {type.Precision}");
                if (type.Scale.HasValue) writer.WriteLine($"    Scale: {type.Scale}");
                
                if (type.Fields.Any())
                {
                    writer.WriteLine("    Fields:");
                    foreach (var field in type.Fields)
                    {
                        writer.WriteLine($"      {field.Name}: {field.TypeString}");
                    }
                }
            }
            writer.WriteLine();
        }
        
        // Enums
        if (model.Enums.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"ENUMS ({model.Enums.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var en in model.Enums)
            {
                writer.WriteLine($"  enum {en.QualifiedName}");
                foreach (var val in en.Values)
                {
                    var valStr = val.Value != null ? $" = {val.Value}" : "";
                    writer.WriteLine($"    {val.Name}{valStr}");
                }
            }
            writer.WriteLine();
        }
        
        // Aspects
        if (model.Aspects.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"ASPECTS ({model.Aspects.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var aspect in model.Aspects)
            {
                writer.WriteLine($"  aspect {aspect.QualifiedName}");
                foreach (var field in aspect.Fields)
                {
                    writer.WriteLine($"    {field.Name}: {field.TypeString}");
                }
            }
            writer.WriteLine();
        }
        
        // Services
        if (model.Services.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"SERVICES ({model.Services.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var svc in model.Services)
            {
                writer.WriteLine($"  service {svc.QualifiedName}");
                writer.WriteLine($"    Entities: {svc.Entities.Count}");
                
                if (svc.Functions.Any())
                {
                    writer.WriteLine("    Functions:");
                    foreach (var func in svc.Functions)
                    {
                        var parms = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.Type}"));
                        writer.WriteLine($"      {func.Name}({parms}) : {func.ReturnType}");
                    }
                }
                
                if (svc.Actions.Any())
                {
                    writer.WriteLine("    Actions:");
                    foreach (var act in svc.Actions)
                    {
                        var parms = string.Join(", ", act.Parameters.Select(p => $"{p.Name}: {p.Type}"));
                        writer.WriteLine($"      {act.Name}({parms})");
                    }
                }
            }
            writer.WriteLine();
        }
        
        // Views
        if (model.Views.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"VIEWS ({model.Views.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var view in model.Views)
            {
                var parms = view.Parameters.Any() 
                    ? $"({string.Join(", ", view.Parameters.Select(p => $"{p.Name}: {p.Type}"))})" 
                    : "";
                writer.WriteLine($"  view {view.QualifiedName}{parms}");
            }
            writer.WriteLine();
        }
        
        // Rules
        if (model.Rules.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"RULES ({model.Rules.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var rule in model.Rules)
            {
                writer.WriteLine($"  rule {rule.Name} for {rule.TargetEntity}");
                writer.WriteLine($"    Triggers: {string.Join(", ", rule.Triggers.Select(t => t.ToString()))}");
                writer.WriteLine($"    Statements: {rule.Statements.Count}");
            }
            writer.WriteLine();
        }
        
        // Sequences
        if (model.Sequences.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"SEQUENCES ({model.Sequences.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var seq in model.Sequences)
            {
                writer.WriteLine($"  sequence {seq.Name}");
                if (seq.Pattern != null) writer.WriteLine($"    Pattern: {seq.Pattern}");
                writer.WriteLine($"    Scope: {seq.Scope}, Reset: {seq.ResetOn}");
            }
            writer.WriteLine();
        }
        
        // Access Controls
        if (model.AccessControls.Any())
        {
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            writer.WriteLine($"ACCESS CONTROLS ({model.AccessControls.Count})");
            writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            
            foreach (var acl in model.AccessControls)
            {
                writer.WriteLine($"  access_control {acl.TargetEntity}");
                writer.WriteLine($"    Rules: {acl.Rules.Count}");
            }
            writer.WriteLine();
        }
        
        writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        writer.WriteLine("END OF DUMP");
        writer.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
    }
}

public class ValidationResult
{
    public bool Success { get; set; }
    public BmModel? Model { get; set; }
    public IReadOnlyList<ValidationError> Errors { get; set; } = Array.Empty<ValidationError>();
}

public record ValidationError(string File, int Line, int Column, string Message);
