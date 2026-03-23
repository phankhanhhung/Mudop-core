using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Compiler.Pipeline;

namespace BMMDL.Compiler.Validation;

/// <summary>
/// Validates events: duplicate names, field existence, field type resolution,
/// emits clause references, and action contract validation (modifies/emits).
/// </summary>
public class EventValidator : ISemanticValidator
{
    private const string PassName = "Semantic Validation";

    public int Validate(CompilationContext context, BmModel model)
    {
        int count = 0;

        count += ValidateEvents(context, model);
        count += ValidateActionContracts(context, model);

        return count;
    }

    private int ValidateEvents(CompilationContext context, BmModel model)
    {
        int count = 0;

        // Check for duplicate event names across the model
        var eventNamesSeen = new Dictionary<string, BmEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var evt in model.Events)
        {
            count++;
            var eventKey = evt.QualifiedName;
            if (eventNamesSeen.TryGetValue(eventKey, out var existing))
            {
                context.AddError(ErrorCodes.SEM_DUPLICATE_EVENT,
                    $"Duplicate event name '{eventKey}' (first defined in '{existing.SourceFile}' line {existing.StartLine})",
                    evt.SourceFile, evt.StartLine, PassName);
            }
            else
            {
                eventNamesSeen[eventKey] = evt;
            }

            // Events should have at least one field
            if (evt.Fields.Count == 0)
            {
                context.AddWarning(ErrorCodes.SEM_EVENT_NO_FIELDS, $"Event '{evt.Name}' has no fields", evt.SourceFile, evt.StartLine, PassName);
            }

            // Check for duplicate field names within event
            var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in evt.Fields)
            {
                if (!fieldNames.Add(field.Name))
                {
                    context.AddError(ErrorCodes.SEM_EVENT_DUPLICATE_FIELD, $"Duplicate field '{field.Name}' in event '{evt.Name}'", evt.SourceFile, evt.StartLine, PassName);
                }

                // Validate field type is known
                if (!string.IsNullOrEmpty(field.TypeString) && !FieldTypeValidator.IsKnownType(field.TypeString, context))
                {
                    context.AddWarning(ErrorCodes.SEM_EVENT_FIELD_UNKNOWN_TYPE, $"Event '{evt.Name}' field '{field.Name}' has unknown type '{field.TypeString}'", evt.SourceFile, evt.StartLine, PassName);
                }
                count++;
            }
        }

        // Validate 'emits' clauses on service actions reference existing events
        foreach (var service in model.Services)
        {
            foreach (var action in service.Actions)
            {
                foreach (var emittedEventName in action.Emits)
                {
                    count++;
                    var eventExists = model.Events.Any(e =>
                        string.Equals(e.Name, emittedEventName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.QualifiedName, emittedEventName, StringComparison.OrdinalIgnoreCase));
                    if (!eventExists)
                    {
                        context.AddWarning(ErrorCodes.SEM_EMITS_EVENT_NOT_FOUND,
                            $"Action '{action.Name}' in service '{service.Name}' emits event '{emittedEventName}' which does not exist in the model",
                            service.SourceFile, action.StartLine, PassName);
                    }
                }
            }
        }

        return count;
    }

    private int ValidateActionContracts(CompilationContext context, BmModel model)
    {
        int validations = 0;
        var eventNames = new HashSet<string>(
            model.Events.Select(e => e.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var fieldNames = new HashSet<string>(
                entity.Fields.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var action in entity.BoundActions)
            {
                // Validate 'modifies' field references
                foreach (var (fieldName, _) in action.Modifies)
                {
                    validations++;
                    if (!fieldNames.Contains(fieldName))
                    {
                        context.AddWarning(
                            ErrorCodes.SEM_ACTION_MODIFIES_INVALID_FIELD,
                            $"Action '{action.Name}' on entity '{entity.Name}' declares 'modifies {fieldName}', " +
                            $"but field '{fieldName}' does not exist on entity '{entity.Name}'",
                            pass: PassName,
                            file: action.SourceFile,
                            line: action.StartLine);
                    }
                }

                // Validate 'emits' event references
                foreach (var eventName in action.Emits)
                {
                    validations++;
                    if (!eventNames.Contains(eventName))
                    {
                        context.AddWarning(
                            ErrorCodes.SEM_ACTION_EMITS_UNKNOWN_EVENT,
                            $"Action '{action.Name}' on entity '{entity.Name}' declares 'emits {eventName}', " +
                            $"but event '{eventName}' is not declared in the model",
                            pass: PassName,
                            file: action.SourceFile,
                            line: action.StartLine);
                    }
                }
            }
        }

        // Also validate service-level actions
        foreach (var service in model.Services)
        {
            foreach (var action in service.Actions)
            {
                foreach (var eventName in action.Emits)
                {
                    validations++;
                    if (!eventNames.Contains(eventName))
                    {
                        context.AddWarning(
                            ErrorCodes.SEM_ACTION_EMITS_UNKNOWN_EVENT,
                            $"Service action '{service.Name}.{action.Name}' declares 'emits {eventName}', " +
                            $"but event '{eventName}' is not declared in the model",
                            pass: PassName,
                            file: action.SourceFile,
                            line: action.StartLine);
                    }
                }
            }
        }

        return validations;
    }
}
