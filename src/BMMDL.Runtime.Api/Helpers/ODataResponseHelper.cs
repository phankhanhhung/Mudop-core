namespace BMMDL.Runtime.Api.Helpers;

using BMMDL.MetaModel.Structure;
using BMMDL.Runtime.Api.Services;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.Extensions;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Helper for OData response formatting: annotations, Prefer header, ETag, HasStream.
/// Extracted from DynamicEntityController to reduce controller size.
/// </summary>
public static class ODataResponseHelper
{
    /// <summary>
    /// Parse the OData Prefer header from the HTTP request.
    /// Returns (returnMinimal, returnRepresentation, maxPageSize, trackChanges).
    /// </summary>
    public static (bool returnMinimal, bool returnRepresentation, int? maxPageSize, bool trackChanges) ParsePreferHeader(
        HttpRequest request)
    {
        bool returnMinimal = false;
        bool returnRepresentation = false;
        int? maxPageSize = null;
        bool trackChanges = false;

        if (request.Headers.TryGetValue("Prefer", out var preferValues))
        {
            foreach (var prefer in preferValues.ToString().Split(',', StringSplitOptions.TrimEntries))
            {
                if (prefer.Equals(ODataConstants.PreferValues.ReturnMinimal, StringComparison.OrdinalIgnoreCase))
                    returnMinimal = true;
                else if (prefer.Equals(ODataConstants.PreferValues.ReturnRepresentation, StringComparison.OrdinalIgnoreCase))
                    returnRepresentation = true;
                else if (prefer.StartsWith(ODataConstants.PreferValues.MaxPageSize + "=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = prefer["odata.maxpagesize=".Length..];
                    if (int.TryParse(val, out var mps) && mps > 0) maxPageSize = mps;
                }
                else if (prefer.StartsWith("maxpagesize=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = prefer["maxpagesize=".Length..];
                    if (int.TryParse(val, out var mps) && mps > 0) maxPageSize = mps;
                }
                else if (prefer.Equals("odata.track-changes", StringComparison.OrdinalIgnoreCase))
                    trackChanges = true;
            }
        }

        return (returnMinimal, returnRepresentation, maxPageSize, trackChanges);
    }

    /// <summary>
    /// Build an OData entity ID URL.
    /// </summary>
    public static string BuildODataId(HttpRequest request, string module, string entity, object? id)
    {
        return $"{request.Scheme}://{request.Host}/api/odata/{module}/{entity}({id})";
    }

    /// <summary>
    /// Build an OData entity set context URL.
    /// </summary>
    public static string BuildEntitySetContext(HttpRequest request, string module, string entity)
    {
        return $"{request.Scheme}://{request.Host}/api/odata/$metadata#{module}_{entity}";
    }

    /// <summary>
    /// Build an OData single entity context URL.
    /// </summary>
    public static string BuildEntityContext(HttpRequest request, string module, string entity)
    {
        return $"{request.Scheme}://{request.Host}/api/odata/$metadata#{module}_{entity}/$entity";
    }

    /// <summary>
    /// Inject non-blocking rule warnings and infos into the response body.
    /// </summary>
    public static void InjectRuleMessages(Dictionary<string, object?> responseData, EntityOperationResult result)
    {
        if (result.Warnings.Count > 0)
            responseData["@bmmdl.warnings"] = result.Warnings;
        if (result.Infos.Count > 0)
            responseData["@bmmdl.infos"] = result.Infos;
    }

    /// <summary>
    /// Inject standard OData annotations (@odata.context, @odata.id, @odata.etag)
    /// into a single entity result and set the ETag response header.
    /// </summary>
    public static void InjectEntityAnnotations(
        Dictionary<string, object?> entity,
        HttpResponse response,
        HttpRequest request,
        string module,
        string entityName,
        object? entityId)
    {
        entity[ODataConstants.JsonProperties.Context] = BuildEntityContext(request, module, entityName);
        entity[ODataConstants.JsonProperties.Id] = BuildODataId(request, module, entityName, entityId);

        var etag = ETagGenerator.GenerateWeakETag(entity);
        response.Headers.ETag = etag;
        entity[ODataConstants.JsonProperties.Etag] = etag;
    }

    /// <summary>
    /// Inject HasStream media annotations into a single entity result.
    /// Adds @odata.mediaReadLink, @odata.mediaContentType, @odata.mediaEtag
    /// and removes internal media columns.
    /// </summary>
    public static void InjectHasStreamAnnotations(
        Dictionary<string, object?> item,
        HttpRequest request,
        string module,
        string entity,
        BmEntity entityDef)
    {
        if (!entityDef.HasStream) return;

        var recordId = item.GetValueOrDefault("Id") ?? item.GetValueOrDefault("id");
        var mediaContentType = item.GetValueOrDefault("MediaContentType")?.ToString();
        if (!string.IsNullOrEmpty(mediaContentType))
        {
            item[ODataConstants.JsonProperties.MediaReadLink] = $"{request.Scheme}://{request.Host}/api/odata/{module}/{entity}/{recordId}/$value";
            item[ODataConstants.JsonProperties.MediaContentType] = mediaContentType;
            var mediaEtag = item.GetValueOrDefault("MediaEtag")?.ToString();
            if (!string.IsNullOrEmpty(mediaEtag))
                item[ODataConstants.JsonProperties.MediaEtag] = mediaEtag;
        }
        item.Remove("MediaContent");
        item.Remove("MediaContentType");
        item.Remove("MediaEtag");
    }

    /// <summary>
    /// Inject HasStream media annotations for a list of items.
    /// </summary>
    public static void InjectHasStreamAnnotationsBatch(
        List<Dictionary<string, object?>> items,
        HttpRequest request,
        string module,
        string entity,
        BmEntity entityDef)
    {
        if (!entityDef.HasStream) return;

        foreach (var item in items)
        {
            var itemId = item.GetValueOrDefault("Id") ?? item.GetValueOrDefault("id");
            var mediaContentType = item.GetValueOrDefault("MediaContentType")?.ToString();
            if (!string.IsNullOrEmpty(mediaContentType))
            {
                item[ODataConstants.JsonProperties.MediaReadLink] = $"{request.Scheme}://{request.Host}/api/odata/{module}/{entity}/{itemId}/$value";
                item[ODataConstants.JsonProperties.MediaContentType] = mediaContentType;
                var mediaEtag = item.GetValueOrDefault("MediaEtag")?.ToString();
                if (!string.IsNullOrEmpty(mediaEtag))
                    item[ODataConstants.JsonProperties.MediaEtag] = mediaEtag;
            }
            item.Remove("MediaContent");
            item.Remove("MediaContentType");
            item.Remove("MediaEtag");
        }
    }

    /// <summary>
    /// Apply Prefer return=minimal / return=representation semantics.
    /// Returns the appropriate IActionResult based on the Prefer header values.
    /// </summary>
    public static IActionResult ApplyPreferReturn(
        ControllerBase controller,
        Dictionary<string, object?> entity,
        bool returnMinimal,
        int statusCode = StatusCodes.Status200OK,
        string? locationAction = null,
        object? locationRouteValues = null,
        string? odataEntityId = null,
        bool returnRepresentation = false)
    {
        if (returnMinimal)
        {
            controller.Response.Headers[ODataConstants.Headers.PreferenceApplied] = ODataConstants.PreferValues.ReturnMinimal;
            if (!string.IsNullOrEmpty(odataEntityId))
                controller.Response.Headers["OData-EntityId"] = odataEntityId;
            return new NoContentResult();
        }

        if (returnRepresentation)
            controller.Response.Headers[ODataConstants.Headers.PreferenceApplied] = ODataConstants.PreferValues.ReturnRepresentation;

        if (statusCode == StatusCodes.Status201Created && locationAction != null)
            return controller.CreatedAtAction(locationAction, locationRouteValues, entity);

        return new OkObjectResult(entity);
    }
}
