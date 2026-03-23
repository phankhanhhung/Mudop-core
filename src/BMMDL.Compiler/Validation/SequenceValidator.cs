using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates sequence definitions: increment, range, target entity/field references,
/// and field type compatibility.
/// </summary>
public class SequenceValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    private static readonly HashSet<string> s_integerCompatibleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Integer", "Int32", "Int64", "Decimal", "Float", "Double"
    };

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var seq in model.Sequences)
        {
            count++;

            // Increment must not be zero
            if (seq.Increment == 0)
            {
                context.AddError(ErrorCodes.SEM_SEQUENCE_ZERO_INCREMENT, $"Sequence '{seq.Name}' has zero increment", seq.SourceFile, seq.StartLine, PassName);
            }

            // Increment must be positive
            if (seq.Increment < 0)
            {
                context.AddError(ErrorCodes.SEM_SEQUENCE_NEGATIVE_INCREMENT, $"Sequence '{seq.Name}' has negative increment ({seq.Increment})", seq.SourceFile, seq.StartLine, PassName);
            }

            // If MaxValue is specified, StartValue must be less than MaxValue
            if (seq.MaxValue.HasValue && seq.StartValue >= seq.MaxValue.Value)
            {
                context.AddError(ErrorCodes.SEM_SEQUENCE_INVALID_RANGE, $"Sequence '{seq.Name}' has start value ({seq.StartValue}) >= max value ({seq.MaxValue.Value})", seq.SourceFile, seq.StartLine, PassName);
            }

            // If ForEntity is specified, validate it exists
            if (!string.IsNullOrEmpty(seq.ForEntity))
            {
                var targetEntity = model.FindEntity(seq.ForEntity);
                if (targetEntity == null)
                {
                    context.AddWarning(ErrorCodes.SEM_SEQUENCE_ENTITY_NOT_FOUND, $"Sequence '{seq.Name}' references non-existent entity '{seq.ForEntity}'", seq.SourceFile, seq.StartLine, PassName);
                }
                else if (!string.IsNullOrEmpty(seq.ForField))
                {
                    // Validate the field exists on the entity
                    var field = targetEntity.Fields.FirstOrDefault(f =>
                        string.Equals(f.Name, seq.ForField, StringComparison.OrdinalIgnoreCase));
                    if (field == null)
                    {
                        context.AddWarning(ErrorCodes.SEM_SEQUENCE_FIELD_NOT_FOUND, $"Sequence '{seq.Name}' references field '{seq.ForField}' that does not exist on entity '{seq.ForEntity}'", seq.SourceFile, seq.StartLine, PassName);
                    }
                    else
                    {
                        // Validate the field is integer-compatible
                        var baseType = FieldTypeValidator.ExtractBaseTypeName(field.TypeString);
                        if (!string.IsNullOrEmpty(baseType) && !s_integerCompatibleTypes.Contains(baseType))
                        {
                            context.AddWarning(ErrorCodes.SEM_SEQUENCE_FIELD_NOT_INTEGER,
                                $"Sequence '{seq.Name}' references field '{seq.ForField}' with type '{field.TypeString}' which is not integer-compatible",
                                seq.SourceFile, seq.StartLine, PassName);
                        }
                    }
                }
            }
        }

        return count;
    }
}
