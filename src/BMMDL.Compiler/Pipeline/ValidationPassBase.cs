using BMMDL.Compiler.Parsing;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Pipeline;

/// <summary>
/// Base class for validation passes providing common functionality:
/// - Null model check
/// - Logging infrastructure
/// - Error tracking pattern
/// </summary>
public abstract class ValidationPassBase : ICompilerPass
{
    protected readonly ILogger Logger;
    
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Order { get; }
    
    protected ValidationPassBase(ILogger? logger = null)
    {
        Logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
    
    public bool Execute(CompilationContext context)
    {
        Logger.LogInformation("Pass {Order}: {Name} - Starting", Order, Name);
        
        if (context.Model == null)
        {
            Logger.LogWarning("Pass {Order}: {Name} - Skipped (no model)", Order, Name);
            return true; // Skip validation if no model
        }
        
        var hasErrors = !ExecuteValidation(context);
        
        if (hasErrors)
        {
            Logger.LogError("Pass {Order}: {Name} - Completed with errors", Order, Name);
            return false;
        }
        
        Logger.LogInformation("Pass {Order}: {Name} - Completed successfully", Order, Name);
        return true;
    }
    
    /// <summary>
    /// Override to implement specific validation logic.
    /// Return true if validation passed, false if errors were found.
    /// </summary>
    protected abstract bool ExecuteValidation(CompilationContext context);
}
