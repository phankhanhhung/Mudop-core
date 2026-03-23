using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates view definitions: SELECT statements, projection fields,
/// and excluded field references.
/// </summary>
public class ViewValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        foreach (var view in model.Views)
        {
            count++;

            // Views must have either a SELECT statement or a projection definition
            if (string.IsNullOrEmpty(view.SelectStatement) && view.ParsedSelect == null && !view.IsProjection)
            {
                context.AddWarning(ErrorCodes.SEM_VIEW_NO_DEFINITION, $"View '{view.Name}' has neither a SELECT statement nor a projection", view.SourceFile, view.StartLine, PassName);
            }

            // For projection views, validate the referenced entity and fields
            if (view.IsProjection)
            {
                if (string.IsNullOrEmpty(view.ProjectionEntityName))
                {
                    context.AddError(ErrorCodes.SEM_VIEW_ENTITY_NOT_FOUND, $"Projection view '{view.Name}' has no source entity", view.SourceFile, view.StartLine, PassName);
                }
                else
                {
                    var sourceEntity = model.FindEntity(view.ProjectionEntityName);
                    if (sourceEntity == null)
                    {
                        context.AddError(ErrorCodes.SEM_VIEW_ENTITY_NOT_FOUND, $"Projection view '{view.Name}' references non-existent entity '{view.ProjectionEntityName}'", view.SourceFile, view.StartLine, PassName);
                    }
                    else
                    {
                        // Build set of valid field names from the source entity
                        var validFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in sourceEntity.Fields) validFields.Add(f.Name);
                        foreach (var a in sourceEntity.Associations) validFields.Add(a.Name);
                        foreach (var c in sourceEntity.Compositions) validFields.Add(c.Name);

                        // Check explicit projection fields
                        foreach (var pf in view.ProjectionFields)
                        {
                            if (!validFields.Contains(pf.FieldName))
                            {
                                context.AddError(ErrorCodes.SEM_VIEW_FIELD_NOT_FOUND, $"Projection view '{view.Name}' references field '{pf.FieldName}' that does not exist on entity '{view.ProjectionEntityName}'", view.SourceFile, view.StartLine, PassName);
                            }
                            count++;
                        }

                        // Check excluded fields
                        foreach (var excl in view.ExcludedFields)
                        {
                            if (!validFields.Contains(excl))
                            {
                                context.AddWarning(ErrorCodes.SEM_VIEW_EXCLUDED_FIELD_NOT_FOUND, $"Projection view '{view.Name}' excludes field '{excl}' that does not exist on entity '{view.ProjectionEntityName}'", view.SourceFile, view.StartLine, PassName);
                            }
                            count++;
                        }
                    }
                }
            }
        }

        return count;
    }
}
