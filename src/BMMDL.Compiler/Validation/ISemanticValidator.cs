using BMMDL.MetaModel;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Interface for focused semantic validation components.
/// Each validator is responsible for a specific domain of semantic checks.
/// </summary>
public interface ISemanticValidator
{
    /// <summary>
    /// Validate the model and report diagnostics to the context.
    /// Returns the number of validations performed.
    /// </summary>
    int Validate(CompilationContext context, BmModel model);
}
