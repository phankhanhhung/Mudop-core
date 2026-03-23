namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Utilities;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.OData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// OData v4 metadata endpoints.
/// - GET /api/odata - Service Document (JSON)
/// - GET /api/odata/$metadata - CSDL XML Schema
/// </summary>
[ApiController]
[Route("api/odata")]
[Authorize]
public class ODataMetadataController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly CsdlGenerator _csdlGenerator;
    private readonly ILogger<ODataMetadataController> _logger;

    // Static CSDL cache: regenerated only when MetaModelCacheManager.Version changes
    private static readonly object _csdlLock = new();
    private static volatile string? _cachedCsdl;
    private static volatile string? _cachedEtag;
    private static long _cachedVersion = -1; // Only accessed inside _csdlLock; volatile illegal for long in C#

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public ODataMetadataController(
        MetaModelCacheManager cacheManager,
        CsdlGenerator csdlGenerator,
        ILogger<ODataMetadataController> logger)
    {
        _cacheManager = cacheManager;
        _csdlGenerator = csdlGenerator;
        _logger = logger;
    }

    /// <summary>
    /// OData Service Document - lists all available EntitySets, ActionImports, and FunctionImports.
    /// </summary>
    [HttpGet]
    [Produces("application/json")]
    public async Task<IActionResult> GetServiceDocument()
    {
        var cache = await GetCacheAsync();
        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/odata";

        var resources = new List<object>();

        // Add EntitySets and Singletons (skip abstract entities — not directly queryable)
        foreach (var entity in cache.Entities)
        {
            if (entity.IsAbstract)
                continue;

            var isSingleton = entity.HasAnnotation(ODataConstants.Annotations.Singleton);
            resources.Add(new
            {
                name = entity.Name,
                kind = isSingleton ? "Singleton" : "EntitySet",
                url = entity.Name
            });
        }

        // Add ActionImports and FunctionImports from services
        foreach (var service in cache.Services)
        {
            foreach (var action in service.Actions)
            {
                resources.Add(new
                {
                    name = action.Name,
                    kind = "ActionImport",
                    url = $"{service.Name}/{action.Name}"
                });
            }

            foreach (var function in service.Functions)
            {
                resources.Add(new
                {
                    name = function.Name,
                    kind = "FunctionImport",
                    url = $"{service.Name}/{function.Name}()"
                });
            }
        }

        var serviceDocument = new Dictionary<string, object>
        {
            [ODataConstants.JsonProperties.Context] = $"{baseUrl}/$metadata",
            ["value"] = resources
        };

        return Ok(serviceDocument);
    }

    /// <summary>
    /// OData $metadata - CSDL XML schema describing all entity types.
    /// Cached in-memory and regenerated only when the MetaModel is reloaded.
    /// Supports ETag-based conditional requests (If-None-Match returns 304 Not Modified).
    /// </summary>
    [HttpGet("$metadata")]
    [Produces("application/xml")]
    public async Task<IActionResult> GetMetadata()
    {
        try
        {
            var currentVersion = _cacheManager.Version;
            string csdl;
            string etag;

            // Load cache outside the lock (async-safe)
            var cache = await GetCacheAsync();

            lock (_csdlLock)
            {
                if (_cachedCsdl != null && _cachedEtag != null && _cachedVersion == currentVersion)
                {
                    csdl = _cachedCsdl;
                    etag = _cachedEtag;
                }
                else
                {
                    csdl = _csdlGenerator.GenerateCsdl(cache);
                    etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csdl)))[..16]}\"";
                    _cachedCsdl = csdl;
                    _cachedEtag = etag;
                    _cachedVersion = currentVersion;
                }
            }

            Response.Headers["ETag"] = etag;

            if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && ifNoneMatch == etag)
                return StatusCode(304);

            return Content(csdl, "application/xml", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate CSDL metadata");
            return StatusCode(500, ODataErrorResponse.FromException(
                "METADATA_GENERATION_FAILED",
                "Failed to generate metadata. Check server logs for details."));
        }
    }

    // ========================================================
    // JSON Metadata Endpoints (for frontend consumption)
    // ========================================================

    /// <summary>
    /// Returns all modules (grouped by namespace) with services and entity sets as JSON.
    /// Used by the frontend sidebar and dashboard to discover available entities.
    /// </summary>
    [HttpGet("metadata")]
    [Produces("application/json")]
    public async Task<IActionResult> GetModulesMetadata()
    {
        var cache = await GetCacheAsync();
        var internalNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SchemaConstants.PlatformSchema };

        var entitiesByNs = cache.Entities
            .Where(e => !internalNamespaces.Contains(e.Namespace ?? ""))
            .Where(e => !e.IsAbstract)
            .GroupBy(e => string.IsNullOrEmpty(e.Namespace) ? ODataConstants.Namespaces.Default : e.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        var servicesByNs = cache.Services
            .Where(s => !internalNamespaces.Contains(s.Namespace ?? ""))
            .GroupBy(s => string.IsNullOrEmpty(s.Namespace) ? ODataConstants.Namespaces.Default : s.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allNamespaces = entitiesByNs.Keys
            .Union(servicesByNs.Keys)
            .OrderBy(ns => ns)
            .ToList();

        var modules = new List<ModuleMetadataDto>();

        foreach (var ns in allNamespaces)
        {
            var services = new List<ServiceMetadataDto>();
            var entitiesInServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (servicesByNs.TryGetValue(ns, out var nsServices))
            {
                foreach (var svc in nsServices)
                {
                    services.Add(MapServiceToDto(svc));
                    foreach (var e in svc.Entities)
                        entitiesInServices.Add(e.Name);
                }
            }

            if (entitiesByNs.TryGetValue(ns, out var nsEntities))
            {
                var standalone = nsEntities
                    .Where(e => !entitiesInServices.Contains(e.Name))
                    .ToList();

                if (standalone.Count > 0)
                {
                    services.Add(new ServiceMetadataDto
                    {
                        Name = "Entities",
                        Namespace = ns,
                        Entities = standalone.Select(e => new EntitySetMetadataDto
                        {
                            Name = e.Name,
                            EntityType = e.Name
                        }).ToArray(),
                        Actions = [],
                        Functions = []
                    });
                }
            }

            modules.Add(new ModuleMetadataDto
            {
                Name = ns,
                Services = services
            });
        }

        return Ok(modules);
    }

    /// <summary>
    /// Returns detailed metadata for a specific entity (fields, keys, associations).
    /// Used by the frontend to render CRUD forms and tables.
    /// </summary>
    [HttpGet("metadata/{module}/entities/{entity}")]
    [Produces("application/json")]
    public async Task<IActionResult> GetEntityMetadata(string module, string entity)
    {
        var cache = await GetCacheAsync();
        var qualifiedName = $"{module}.{entity}";
        var entityDef = cache.GetEntity(qualifiedName) ?? cache.GetEntity(entity);

        if (entityDef == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.EntityNotFound, $"Entity '{qualifiedName}' not found"));
        }

        // For inherited entities, merge parent fields + child-specific fields
        IEnumerable<BmField> allFields = entityDef.Fields;
        if (entityDef.ParentEntity != null)
        {
            var parentFieldNames = new HashSet<string>(
                entityDef.ParentEntity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var childOnlyFields = entityDef.Fields.Where(f => !parentFieldNames.Contains(f.Name));
            allFields = entityDef.ParentEntity.Fields.Concat(childOnlyFields);
        }

        // Apply service projection filtering if applicable
        var (includeProj, excludeProj) = CsdlGenerator.GetServiceProjection(entityDef.Name, cache.Services);
        var projectedFields = allFields;
        if (includeProj != null)
        {
            var include = new HashSet<string>(includeProj, StringComparer.OrdinalIgnoreCase);
            projectedFields = projectedFields.Where(f => f.IsKey || include.Contains(f.Name));
        }
        else if (excludeProj != null)
        {
            var exclude = new HashSet<string>(excludeProj, StringComparer.OrdinalIgnoreCase);
            projectedFields = projectedFields.Where(f => !exclude.Contains(f.Name));
        }

        var fields = projectedFields.Select(f => MapFieldToDto(f, cache)).ToList();
        var keySource = entityDef.Fields.Any(f => f.IsKey) ? entityDef.Fields : (entityDef.ParentEntity?.Fields ?? entityDef.Fields);
        var keys = keySource.Where(f => f.IsKey).Select(f => MetadataTypeMapper.ToODataPropertyName(f.Name)).ToList();
        var existingFieldNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        // Include parent associations/compositions for inherited entities
        var allAssociations = entityDef.Associations.AsEnumerable();
        var allCompositions = entityDef.Compositions.AsEnumerable();
        if (entityDef.ParentEntity != null)
        {
            allAssociations = entityDef.ParentEntity.Associations.Concat(entityDef.Associations);
            allCompositions = entityDef.ParentEntity.Compositions.Concat(entityDef.Compositions);
        }

        var associations = allAssociations
            .Select(a => MapAssociationToDto(a, isComposition: false))
            .Concat(allCompositions.Select(c => MapAssociationToDto(c, isComposition: true)))
            .ToList();

        // Synthesize FK fields for ManyToOne/OneToOne associations whose FK column
        // is not already declared as an explicit field on the entity
        foreach (var assoc in associations)
        {
            if (assoc.ForeignKey != null && !existingFieldNames.Contains(assoc.ForeignKey))
            {
                fields.Add(new FieldMetadataDto
                {
                    Name = assoc.ForeignKey,
                    Type = "UUID",
                    DisplayName = null,
                    Description = null,
                    IsRequired = false,
                    IsReadOnly = false,
                    IsComputed = false,
                    MaxLength = null,
                    Precision = null,
                    Scale = null,
                    DefaultValue = null,
                    EnumValues = null,
                    Annotations = new Dictionary<string, object?>()
                });
            }
        }

        // Map bound actions/functions
        var boundActions = entityDef.BoundActions.Select(a => new ActionMetadataDto
        {
            Name = a.Name,
            Parameters = a.Parameters.Select(p => new ParameterMetadataDto
            {
                Name = p.Name,
                Type = MetadataTypeMapper.MapBmmdlTypeToFrontend(p.Type),
                IsRequired = true
            }).ToArray(),
            ReturnType = string.IsNullOrEmpty(a.ReturnType) ? null : a.ReturnType,
            IsBound = true,
            BindingParameter = entityDef.Name
        }).ToList();

        var boundFunctions = entityDef.BoundFunctions.Select(f => new FunctionMetadataDto
        {
            Name = f.Name,
            Parameters = f.Parameters.Select(p => new ParameterMetadataDto
            {
                Name = p.Name,
                Type = MetadataTypeMapper.MapBmmdlTypeToFrontend(p.Type),
                IsRequired = true
            }).ToArray(),
            ReturnType = string.IsNullOrEmpty(f.ReturnType) ? "Void" : f.ReturnType,
            IsBound = true,
            BindingParameter = entityDef.Name
        }).ToList();

        var dto = new EntityMetadataDto
        {
            Name = entityDef.Name,
            Namespace = entityDef.Namespace ?? "",
            DisplayName = entityDef.GetAnnotation("displayName")?.Value?.ToString(),
            Description = entityDef.GetAnnotation("description")?.Value?.ToString(),
            Fields = fields,
            Keys = keys,
            Associations = associations,
            Annotations = MapAnnotations(entityDef.Annotations),
            HasStream = entityDef.HasStream,
            IsAbstract = entityDef.IsAbstract,
            IsSingleton = entityDef.HasAnnotation(ODataConstants.Annotations.Singleton),
            ParentEntityName = entityDef.ParentEntityName,
            BoundActions = boundActions.Count > 0 ? boundActions : null,
            BoundFunctions = boundFunctions.Count > 0 ? boundFunctions : null
        };

        return Ok(dto);
    }

    // ========================================================
    // JSON Metadata Mapping Helpers
    // ========================================================

    private ServiceMetadataDto MapServiceToDto(BmService service)
    {
        return new ServiceMetadataDto
        {
            Name = service.Name,
            Namespace = service.Namespace ?? "",
            Entities = service.Entities.Select(e => new EntitySetMetadataDto
            {
                Name = e.Name,
                EntityType = e.Name
            }).ToArray(),
            Actions = service.Actions.Select(a => new ActionMetadataDto
            {
                Name = a.Name,
                Parameters = a.Parameters.Select(p => new ParameterMetadataDto
                {
                    Name = p.Name,
                    Type = MetadataTypeMapper.MapBmmdlTypeToFrontend(p.Type),
                    IsRequired = true
                }).ToArray(),
                ReturnType = string.IsNullOrEmpty(a.ReturnType) ? null : a.ReturnType,
                IsBound = false
            }).ToArray(),
            Functions = service.Functions.Select(f => new FunctionMetadataDto
            {
                Name = f.Name,
                Parameters = f.Parameters.Select(p => new ParameterMetadataDto
                {
                    Name = p.Name,
                    Type = MetadataTypeMapper.MapBmmdlTypeToFrontend(p.Type),
                    IsRequired = true
                }).ToArray(),
                ReturnType = string.IsNullOrEmpty(f.ReturnType) ? "Void" : f.ReturnType,
                IsBound = false
            }).ToArray()
        };
    }

    private FieldMetadataDto MapFieldToDto(BmField field, MetaModelCache cache)
    {
        var (type, maxLength, precision, scale) = MetadataTypeMapper.MapFieldType(field, cache.GetType);

        var enumValues = ResolveEnumValues(field, cache);
        if (enumValues != null)
            type = "Enum";

        bool isSystemManaged = field.HasAnnotation("Core.Computed")
            || field.HasAnnotation("Tenant.Column")
            || field.HasAnnotation("Company.Column");

        var lowerName = field.Name.ToLowerInvariant();
        bool isWellKnownSystemField = lowerName is "tenantid" or SchemaConstants.TenantIdColumn
            or "createdat" or "created_at" or "createdby" or "created_by"
            or "modifiedat" or "modified_at" or "modifiedby" or "modified_by"
            or "updatedat" or "updated_at" or "updatedby" or "updated_by"
            or "isdeleted" or "is_deleted" or "deletedat" or "deleted_at"
            or "deletedby" or "deleted_by";

        isSystemManaged = isSystemManaged || isWellKnownSystemField;

        return new FieldMetadataDto
        {
            Name = MetadataTypeMapper.ToODataPropertyName(field.Name),
            Type = type,
            DisplayName = field.GetAnnotation("displayName")?.Value?.ToString(),
            Description = field.GetAnnotation("description")?.Value?.ToString(),
            IsRequired = !field.IsNullable && !field.IsKey && !isSystemManaged,
            IsReadOnly = field.IsReadonly || field.IsComputed || field.IsKey || isSystemManaged,
            IsComputed = field.IsComputed || isSystemManaged,
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
            DefaultValue = CsdlGenerator.SanitizeDefaultValue(field.DefaultValueString),
            EnumValues = enumValues,
            Annotations = MapAnnotations(field.Annotations)
        };
    }

    private IReadOnlyList<EnumValueDto>? ResolveEnumValues(BmField field, MetaModelCache cache)
    {
        string? enumTypeName = null;

        if (field.TypeRef is BmCustomTypeReference customRef)
            enumTypeName = customRef.TypeName;
        else if (!string.IsNullOrEmpty(field.TypeString))
            enumTypeName = field.TypeString.TrimEnd('?');
        else
            return null;

        if (enumTypeName == null) return null;

        var bmEnum = cache.GetEnum(enumTypeName);
        if (bmEnum == null)
        {
            bmEnum = cache.Model.Enums.FirstOrDefault(e =>
                e.Name.Equals(enumTypeName, StringComparison.OrdinalIgnoreCase));
        }

        if (bmEnum == null) return null;

        return bmEnum.Values.Select(v => new EnumValueDto
        {
            Name = v.Name,
            Value = v.Name,
            DisplayName = null
        }).ToArray();
    }

    private static AssociationMetadataDto MapAssociationToDto(BmAssociation assoc, bool isComposition)
    {
        var cardinality = assoc.Cardinality switch
        {
            BmCardinality.ManyToOne => "ZeroOrOne",
            BmCardinality.OneToOne => "One",
            BmCardinality.OneToMany => "Many",
            BmCardinality.ManyToMany => "Many",
            _ => "ZeroOrOne"
        };

        string? foreignKey = null;
        if (assoc.Cardinality is BmCardinality.ManyToOne or BmCardinality.OneToOne)
        {
            foreignKey = MetadataTypeMapper.ToODataPropertyName($"{char.ToLower(assoc.Name[0])}{assoc.Name[1..]}Id");
        }

        return new AssociationMetadataDto
        {
            Name = assoc.Name,
            TargetEntity = assoc.TargetEntity,
            Cardinality = cardinality,
            ForeignKey = foreignKey,
            IsComposition = isComposition
        };
    }

    private static IReadOnlyDictionary<string, object?> MapAnnotations(IEnumerable<BmAnnotation> annotations)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var ann in annotations)
        {
            dict[ann.Name] = ann.Properties != null && ann.Properties.Count > 0
                ? ann.Properties
                : ann.Value;
        }
        return dict;
    }
}
