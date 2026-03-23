using BMMDL.MetaModel;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates access control definitions: target entity and rule existence.
/// </summary>
public class AccessControlValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var ac in model.AccessControls)
        {
            count++;

            // Must have target entity
            if (string.IsNullOrEmpty(ac.TargetEntity))
            {
                context.AddError(ErrorCodes.SEM_ACCESS_NO_TARGET, $"Access control has no target entity", ac.SourceFile, ac.StartLine, PassName);
            }

            // Must have at least one rule
            if (ac.Rules.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_ACCESS_NO_RULES, $"Access control for '{ac.TargetEntity}' has no rules", ac.SourceFile, ac.StartLine, PassName);
            }
        }

        return count;
    }
}
