namespace BMMDL.Runtime.Rules;

/// <summary>
/// Statement execution policy for event handlers.
/// Call errors are logged but execution continues.
/// </summary>
public class ResilientPolicy : IStatementExecutionPolicy
{
    public static readonly ResilientPolicy Instance = new();

    public bool ContinueOnCallError => true;
}
