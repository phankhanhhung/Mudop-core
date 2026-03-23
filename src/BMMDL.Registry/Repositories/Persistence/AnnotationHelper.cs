using BMMDL.MetaModel.Abstractions;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities.Normalized;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Static helpers for annotation persistence shared across all persisters.
/// </summary>
internal static class AnnotationHelper
{
    public static void SaveAnnotationsForOwner(
        RegistryDbContext db,
        IReadOnlyList<BmAnnotation> annotations,
        string ownerType,
        Guid ownerId)
    {
        foreach (var annotation in annotations)
        {
            string? value = null;
            if (annotation.Properties?.Count > 0)
            {
                value = System.Text.Json.JsonSerializer.Serialize(annotation.Properties);
            }
            else if (annotation.Value != null)
            {
                value = System.Text.Json.JsonSerializer.Serialize(annotation.Value);
            }

            db.NormalizedAnnotations.Add(new NormalizedAnnotation
            {
                Id = Guid.NewGuid(),
                OwnerType = ownerType,
                OwnerId = ownerId,
                Name = annotation.Name,
                Value = value
            });
        }
    }

    public static List<BmAnnotation> ReconstructAnnotations(IEnumerable<NormalizedAnnotation> annotations)
    {
        var result = new List<BmAnnotation>();
        foreach (var ann in annotations)
        {
            Dictionary<string, object?>? props = null;
            object? scalarValue = null;
            if (!string.IsNullOrEmpty(ann.Value))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(ann.Value);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        props = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(ann.Value);
                    }
                    else
                    {
                        scalarValue = doc.RootElement.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String => doc.RootElement.GetString(),
                            System.Text.Json.JsonValueKind.Number => doc.RootElement.GetDecimal(),
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            _ => ann.Value
                        };
                    }
                }
                catch
                {
                    scalarValue = ann.Value;
                }
            }
            result.Add(new BmAnnotation(ann.Name, scalarValue, props));
        }
        return result;
    }
}
