namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;

/// <summary>
/// Handles entity data validation: enum values, JSONB structure, required associations,
/// computed field stripping, and $compute field references.
/// </summary>
public class EntityValidationService : IEntityValidationService
{
    private readonly MetaModelCacheManager _cacheManager;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public EntityValidationService(MetaModelCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    /// <summary>
    /// Validate enum field values against their BmEnum definition.
    /// Returns error message if validation fails, null if OK.
    /// </summary>
    public async Task<string?> ValidateEnumFieldsAsync(BmEntity entityDef, Dictionary<string, object?> data)
    {
        var cache = await GetCacheAsync();
        foreach (var field in entityDef.Fields)
        {
            if (string.IsNullOrEmpty(field.TypeString)) continue;

            var enumDef = cache.GetEnum(field.TypeString);
            if (enumDef == null) continue;

            var dataKey = data.Keys.FirstOrDefault(k => k.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
            if (dataKey == null) continue;

            var value = data[dataKey];
            if (value == null) continue;

            var valueStr = value.ToString()!;

            var validNames = enumDef.Values.Select(v => v.Name).ToList();
            var validValues = enumDef.Values
                .Where(v => v.Value != null)
                .Select(v => v.Value!.ToString()!)
                .ToList();

            if (!validNames.Any(n => n.Equals(valueStr, StringComparison.OrdinalIgnoreCase)) &&
                !validValues.Contains(valueStr))
            {
                return $"Invalid value '{valueStr}' for field '{field.Name}'. Valid values: {string.Join(", ", validNames)}";
            }
        }

        return null;
    }

    /// <summary>
    /// Validate JSONB fields against their BmType definition.
    /// Checks that JSON keys match BmType.Fields and required fields are present.
    /// Returns error message if validation fails, null if OK.
    /// </summary>
    public async Task<string?> ValidateJsonbFieldsAsync(BmEntity entityDef, Dictionary<string, object?> data)
    {
        var cache = await GetCacheAsync();
        var errors = new List<string>();

        foreach (var field in entityDef.Fields)
        {
            if (field.TypeRef is not MetaModel.Types.BmCustomTypeReference customRef)
                continue;

            var typeDef = cache.GetType(customRef.TypeName);
            if (typeDef == null || typeDef.Fields.Count == 0)
                continue;

            var dataKey = data.Keys.FirstOrDefault(k => k.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
            if (dataKey == null) continue;

            var value = data[dataKey];
            if (value == null) continue;

            System.Text.Json.JsonElement jsonElement;
            if (value is System.Text.Json.JsonElement je)
            {
                jsonElement = je;
            }
            else if (value is string jsonStr)
            {
                try
                {
                    jsonElement = System.Text.Json.JsonDocument.Parse(jsonStr).RootElement;
                }
                catch
                {
                    errors.Add($"Field '{field.Name}': value must be a valid JSON object for type '{typeDef.Name}'");
                    continue;
                }
            }
            else
            {
                continue;
            }

            if (jsonElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                errors.Add($"Field '{field.Name}': expected JSON object for type '{typeDef.Name}', got {jsonElement.ValueKind}");
                continue;
            }

            var validFieldNames = new HashSet<string>(
                typeDef.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            var unknownKeys = new List<string>();
            foreach (var prop in jsonElement.EnumerateObject())
            {
                if (!validFieldNames.Contains(prop.Name))
                    unknownKeys.Add(prop.Name);
            }

            if (unknownKeys.Count > 0)
            {
                errors.Add($"Field '{field.Name}': unknown properties [{string.Join(", ", unknownKeys)}] for type '{typeDef.Name}'. Valid properties: {string.Join(", ", typeDef.Fields.Select(f => f.Name))}");
            }

            var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in jsonElement.EnumerateObject())
                providedKeys.Add(prop.Name);

            foreach (var typeField in typeDef.Fields)
            {
                var isNullable = typeField.TypeRef?.IsNullable ?? true;
                var hasDefault = typeField.DefaultExpr != null || !string.IsNullOrEmpty(typeField.DefaultValueString);

                if (!isNullable && !hasDefault && !providedKeys.Contains(typeField.Name))
                {
                    errors.Add($"Field '{field.Name}': required property '{typeField.Name}' is missing for type '{typeDef.Name}'");
                }
            }
        }

        return errors.Count > 0 ? string.Join("; ", errors) : null;
    }

    /// <summary>
    /// Validate required associations (MinCardinality >= 1) have non-null FK values.
    /// Returns list of validation error messages, empty if valid.
    /// </summary>
    public static List<string> ValidateRequiredAssociations(BmEntity entityDef, Dictionary<string, object?> data)
    {
        var errors = new List<string>();

        foreach (var assoc in entityDef.Associations)
        {
            if (assoc.MinCardinality < 1)
                continue;

            if (assoc.Cardinality == BmCardinality.OneToMany || assoc.Cardinality == BmCardinality.ManyToMany)
                continue;

            var fkFieldName = NamingConvention.GetFkFieldName(assoc.Name);
            var fkSnakeName = NamingConvention.GetFkColumnName(assoc.Name);

            var hasValue = data.Keys.Any(k =>
                k.Equals(fkFieldName, StringComparison.OrdinalIgnoreCase) ||
                k.Equals(fkSnakeName, StringComparison.OrdinalIgnoreCase));

            if (!hasValue)
            {
                errors.Add($"Required association '{assoc.Name}' (cardinality [{assoc.MinCardinality}..{(assoc.MaxCardinality == -1 ? "*" : assoc.MaxCardinality.ToString())}]) requires a non-null foreign key value.");
            }
            else
            {
                var key = data.Keys.First(k =>
                    k.Equals(fkFieldName, StringComparison.OrdinalIgnoreCase) ||
                    k.Equals(fkSnakeName, StringComparison.OrdinalIgnoreCase));
                if (data[key] == null)
                {
                    errors.Add($"Required association '{assoc.Name}' cannot have a null foreign key value.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Validate that all field references in a $compute expression exist on the entity.
    /// Returns an error message if validation fails, or null if valid.
    /// </summary>
    public static string? ValidateComputeFieldReferences(BmEntity entityDef, string computeExpr)
    {
        var fieldNames = entityDef.Fields
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var computeSpecs = computeExpr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var spec in computeSpecs)
        {
            var trimmed = spec.Trim();
            var asIndex = trimmed.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asIndex < 0) continue;

            var expression = trimmed.Substring(0, asIndex).Trim();

            var tokens = System.Text.RegularExpressions.Regex.Matches(expression, @"\b(\w+)\b")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .Where(t => !decimal.TryParse(t, out _))
                .Where(t => !new[] { "add", "sub", "mul", "div", "mod" }
                    .Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var token in tokens)
            {
                if (!fieldNames.Contains(token))
                {
                    return $"Field '{token}' referenced in $compute expression does not exist on entity '{entityDef.Name}'. Available fields: {string.Join(", ", fieldNames.OrderBy(f => f))}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Strip computed/readonly/virtual/immutable fields from input data.
    /// Returns the list of stripped field names.
    /// </summary>
    public static List<string> StripComputedFields(BmEntity entityDef, Dictionary<string, object?> data, bool isUpdate = false)
    {
        var rejected = new List<string>();
        var protectedFieldNames = entityDef.Fields
            .Where(f => f.IsComputed || f.IsVirtual || f.IsReadonly || (f.IsImmutable && isUpdate))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in data.Keys.ToList())
        {
            if (protectedFieldNames.Contains(key))
            {
                data.Remove(key);
                rejected.Add(key);
            }
        }

        return rejected;
    }
}
