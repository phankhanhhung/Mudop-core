using BMMDL.MetaModel;
using BMMDL.Compiler.Validation;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Pass 5: Semantic Validation
/// Validates model for semantic correctness.
/// </summary>
public class SemanticValidationPass : ICompilerPass
{
    public string Name => "Semantic Validation";
    public string Description => "Validate semantic rules";
    public int Order => 51;

    private static readonly ISemanticValidator[] s_validators =
    [
        new EntityStructureValidator(),
        new FieldTypeValidator(),
        new RelationshipValidator(),
        new EnumValidator(),
        new ConstraintValidator(),
        new RuleValidator(),
        new AccessControlValidator(),
        new ViewValidator(),
        new EventValidator(),
        new SequenceValidator(),
        new MigrationValidator(),
        new SeedValidator()
    ];

    public bool Execute(CompilationContext context)
    {
        if (context.Model == null)
        {
            context.AddError(ErrorCodes.SEM_NO_MODEL, "No model available for semantic validation", pass: Name);
            return false;
        }

        var model = context.Model;
        int validations = 0;

        foreach (var validator in s_validators)
        {
            validations += validator.Validate(context, model);
        }

        context.ValidationsPerformed = validations;

        // Check for semantic errors
        if (context.Diagnostics.Any(d => d.Code.StartsWith("SEM") && d.Severity == DiagnosticSeverity.Error))
            return false;

        return true;
    }
}
