namespace BMMDL.Runtime.Rules;

/// <summary>
/// Statement execution policy for rules and actions.
/// Call errors propagate and stop execution.
/// </summary>
public class FailFastPolicy : IStatementExecutionPolicy
{
    public static readonly FailFastPolicy Instance = new();

    public bool ContinueOnCallError => false;
}
