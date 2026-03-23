namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel;
using Microsoft.Extensions.Logging;

/// <summary>
/// Result of event schema validation.
/// </summary>
public class EventSchemaValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();

    public static EventSchemaValidationResult Valid() => new() { IsValid = true };
    public static EventSchemaValidationResult Invalid(List<string> errors) => new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Validates event payloads against BmEvent schema definitions.
/// Validation is advisory — callers should log but not block on failures.
/// </summary>
public class EventSchemaValidator
{
    private readonly ILogger<EventSchemaValidator> _logger;

    public EventSchemaValidator(ILogger<EventSchemaValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate event payload against schema. Returns valid if no schema exists (untyped events are OK).
    /// </summary>
    public EventSchemaValidationResult Validate(BmEvent? eventSchema, Dictionary<string, object?> payload)
    {
        if (eventSchema == null)
            return EventSchemaValidationResult.Valid();

        var errors = new List<string>();

        // Check all schema fields exist in payload
        foreach (var field in eventSchema.Fields)
        {
            if (!payload.ContainsKey(field.Name))
            {
                if (!field.Annotations.Any(a => a.Name.Equals("Optional", StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Required field '{field.Name}' is missing from event '{eventSchema.Name}' payload");
                }
            }
        }

        // Type validation — check field types match where possible
        foreach (var field in eventSchema.Fields)
        {
            if (payload.TryGetValue(field.Name, out var value) && value != null)
            {
                if (!IsTypeCompatible(field.TypeString, value))
                {
                    errors.Add($"Field '{field.Name}' in event '{eventSchema.Name}': expected type '{field.TypeString}', got '{value.GetType().Name}'");
                }
            }
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Event schema validation failed for '{EventName}': {Errors}",
                eventSchema.Name, string.Join("; ", errors));
            return EventSchemaValidationResult.Invalid(errors);
        }

        return EventSchemaValidationResult.Valid();
    }

    /// <summary>
    /// Build a schema-driven payload from entity data, using BmEvent field definitions.
    /// </summary>
    public Dictionary<string, object?> BuildSchemaPayload(BmEvent eventSchema, Dictionary<string, object?> entityData, Dictionary<string, object?>? additionalData = null)
    {
        var payload = new Dictionary<string, object?>();

        foreach (var field in eventSchema.Fields)
        {
            var value = entityData.FirstOrDefault(kv =>
                string.Equals(kv.Key, field.Name, StringComparison.OrdinalIgnoreCase));

            if (value.Key != null)
            {
                payload[field.Name] = value.Value;
            }
            else if (additionalData != null)
            {
                var additional = additionalData.FirstOrDefault(kv =>
                    string.Equals(kv.Key, field.Name, StringComparison.OrdinalIgnoreCase));
                if (additional.Key != null)
                    payload[field.Name] = additional.Value;
            }
        }

        return payload;
    }

    private static bool IsTypeCompatible(string expectedType, object actualValue)
    {
        var upper = expectedType.ToUpperInvariant();

        if (upper == "UUID")
            return actualValue is Guid or string;
        if (upper == "STRING" || upper.StartsWith("STRING("))
            return actualValue is string;
        if (upper == "INTEGER")
            return actualValue is int or long or short;
        if (upper == "DECIMAL" || upper.StartsWith("DECIMAL("))
            return actualValue is decimal or double or float or int or long;
        if (upper == "BOOLEAN")
            return actualValue is bool;
        if (upper is "DATE" or "DATETIME" or "TIMESTAMP")
            return actualValue is DateTime or DateTimeOffset or string;

        return true; // Unknown types pass validation
    }
}
