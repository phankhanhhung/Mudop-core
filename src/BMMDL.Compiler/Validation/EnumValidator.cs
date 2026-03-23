using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates enum definitions: member existence and duplicate value detection.
/// </summary>
public class EnumValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var en in model.Enums)
        {
            count++;

            // Enums should have values
            if (en.Values.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_ENUM_NO_VALUES, $"Enum '{en.Name}' has no values", en.SourceFile, en.StartLine, PassName);
            }

            // Enum values must be unique (no two members with the same integer value)
            var valuesWithExplicit = en.Values.Where(v => v.Value != null).ToList();
            var seen = new Dictionary<string, string>(); // stringified value -> member name
            foreach (var ev in valuesWithExplicit)
            {
                var key = ev.Value!.ToString()!;
                if (seen.TryGetValue(key, out var existingMember))
                {
                    context.AddError(ErrorCodes.SEM_DUPLICATE_ENUM_VALUE,
                        $"Enum '{en.Name}': members '{existingMember}' and '{ev.Name}' have the same value {key}",
                        en.SourceFile, en.StartLine, PassName);
                }
                else
                {
                    seen[key] = ev.Name;
                }
                count++;
            }
        }

        return count;
    }
}
