using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates association and composition cardinalities.
/// </summary>
public class RelationshipValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var entity in model.Entities)
        {
            foreach (var assoc in entity.Associations.Concat<BmAssociation>(entity.Compositions))
            {
                count++;

                if (assoc.MinCardinality < 0)
                {
                    context.AddWarning(ErrorCodes.SEM_INVALID_CARDINALITY,
                        $"Association '{assoc.Name}' in entity '{entity.Name}' has negative min cardinality ({assoc.MinCardinality})",
                        entity.SourceFile, assoc.StartLine, PassName);
                }

                // MaxCardinality -1 means unlimited (*), which is always valid
                if (assoc.MaxCardinality != -1 && assoc.MinCardinality > assoc.MaxCardinality)
                {
                    context.AddWarning(ErrorCodes.SEM_INVALID_CARDINALITY,
                        $"Association '{assoc.Name}' in entity '{entity.Name}' has min cardinality ({assoc.MinCardinality}) > max cardinality ({assoc.MaxCardinality})",
                        entity.SourceFile, assoc.StartLine, PassName);
                }
            }
        }

        return count;
    }
}
