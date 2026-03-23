namespace BMMDL.Runtime.Api.Helpers;

/// <summary>
/// Utility for parsing OData reference URLs to extract entity IDs.
/// Extracted from EntityReferenceController for reuse.
/// </summary>
public static class ODataUrlParser
{
    /// <summary>
    /// Extract entity ID from OData reference URL.
    /// Expected formats: /api/odata/{module}/{entity}('{guid}') or /api/odata/{module}/{entity}({guid})
    /// or /api/odata/{module}/{entity}/{guid}
    /// </summary>
    public static Guid? ExtractEntityIdFromODataReference(string odataId)
    {
        if (string.IsNullOrWhiteSpace(odataId))
            return null;

        // Try parentheses format first: Entity(guid)
        var openParen = odataId.LastIndexOf('(');
        var closeParen = odataId.LastIndexOf(')');

        if (openParen >= 0 && closeParen > openParen)
        {
            var idString = odataId[(openParen + 1)..closeParen];
            idString = idString.Trim('\'', '"');
            if (Guid.TryParse(idString, out var guidFromParen))
                return guidFromParen;
        }

        // Try segment format: Entity/guid or just guid
        var lastSlash = odataId.LastIndexOf('/');
        var candidate = lastSlash >= 0 ? odataId[(lastSlash + 1)..] : odataId;
        candidate = candidate.Trim('\'', '"');

        return Guid.TryParse(candidate, out var guid) ? guid : null;
    }
}
