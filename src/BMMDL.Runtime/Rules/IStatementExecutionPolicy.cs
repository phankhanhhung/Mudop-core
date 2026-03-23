namespace BMMDL.Runtime.Rules;

/// <summary>
/// Strategy interface controlling error handling behavior during statement execution.
/// Different consumers (rules, actions, event handlers) need different error semantics.
/// </summary>
public interface IStatementExecutionPolicy
{
    /// <summary>
    /// Whether to continue executing after a call statement encounters an error.
    /// Rules/actions: false (fail-fast). Event handlers: true (resilient).
    /// </summary>
    bool ContinueOnCallError { get; }
}
