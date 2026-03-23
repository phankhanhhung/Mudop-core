namespace BMMDL.Runtime.Rules;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Expressions;

/// <summary>
/// Interface for rule engine to enable DI and testing.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Execute rules before creating an entity.
    /// </summary>
    Task<RuleExecutionResult> ExecuteBeforeCreateAsync(
        BmEntity entity,
        Dictionary<string, object?> data,
        EvaluationContext context);

    /// <summary>
    /// Execute rules before updating an entity.
    /// </summary>
    Task<RuleExecutionResult> ExecuteBeforeUpdateAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        Dictionary<string, object?> updateData,
        EvaluationContext context);

    /// <summary>
    /// Execute rules before deleting an entity.
    /// </summary>
    Task<RuleExecutionResult> ExecuteBeforeDeleteAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        EvaluationContext context);

    /// <summary>
    /// Execute rules after creating an entity.
    /// </summary>
    Task ExecuteAfterCreateAsync(
        BmEntity entity,
        Dictionary<string, object?> createdData,
        EvaluationContext context);

    /// <summary>
    /// Execute rules after updating an entity.
    /// </summary>
    Task ExecuteAfterUpdateAsync(
        BmEntity entity,
        Dictionary<string, object?> existingData,
        Dictionary<string, object?> updatedData,
        EvaluationContext context);

    /// <summary>
    /// Execute rules after deleting an entity.
    /// </summary>
    Task ExecuteAfterDeleteAsync(
        BmEntity entity,
        Dictionary<string, object?> deletedData,
        EvaluationContext context);
    
    /// <summary>
    /// Execute rules before reading entities.
    /// Before-read rules can reject the read or modify query context.
    /// </summary>
    Task<RuleExecutionResult> ExecuteBeforeReadAsync(
        BmEntity entity,
        EvaluationContext context);

    /// <summary>
    /// Execute rules after reading entities.
    /// After-read rules receive the query results for post-processing.
    /// </summary>
    Task<RuleExecutionResult> ExecuteAfterReadAsync(
        BmEntity entity,
        List<Dictionary<string, object?>> results,
        EvaluationContext context);

    /// <summary>
    /// Execute a list of statements (for bound actions/functions).
    /// </summary>
    Task<object?> ExecuteStatementsAsync(
        IList<BMMDL.MetaModel.BmRuleStatement> statements,
        EvaluationContext context,
        CancellationToken ct = default);
}
