namespace BMMDL.Runtime.OData;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Utilities;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using System.Xml.Linq;

/// <summary>
/// Generates OData v4 CSDL (Common Schema Definition Language) XML from a MetaModelCache.
/// Stateless — takes MetaModelCache at call time, safe for singleton DI registration.
/// </summary>
public class CsdlGenerator
{
    /// <summary>
    /// Generate full CSDL XML document from the given cache.
    /// Groups entities, types, enums by their module namespace.
    /// </summary>
    public string GenerateCsdl(IMetaModelCache cache)
    {
        XNamespace edmx = "http://docs.oasis-open.org/odata/ns/edmx";
        XNamespace edm = "http://docs.oasis-open.org/odata/ns/edm";

        // Internal namespaces that should be hidden from public $metadata
        var internalNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SchemaConstants.PlatformSchema
        };

        // Group entities by namespace (module), excluding internal
        var entitiesByNs = cache.Entities
            .Where(e => !internalNamespaces.Contains(e.Namespace ?? ""))
            .GroupBy(e => string.IsNullOrEmpty(e.Namespace) ? "Default" : e.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group enums by namespace, excluding internal
        var enumsByNs = cache.Model.Enums
            .Where(e => !internalNamespaces.Contains(e.Namespace ?? ""))
            .GroupBy(e => string.IsNullOrEmpty(e.Namespace) ? "Default" : e.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group services by namespace (from module), excluding internal
        var servicesByNs = cache.Services
            .Where(s => !internalNamespaces.Contains(s.Namespace ?? ""))
            .GroupBy(s => string.IsNullOrEmpty(s.Namespace) ? "Default" : s.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group custom types (BmType) by namespace, excluding internal
        var typesByNs = cache.Model.Types
            .Where(t => !internalNamespaces.Contains(t.Namespace ?? ""))
            .Where(t => t.Fields.Count > 0)
            .GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "Default" : t.Namespace)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get all unique namespaces
        var allNamespaces = entitiesByNs.Keys
            .Union(enumsByNs.Keys)
            .Union(servicesByNs.Keys)
            .Union(typesByNs.Keys)
            .OrderBy(ns => ns)
            .ToList();

        // Generate a Schema element for each namespace
        var schemas = new List<XElement>();
        foreach (var ns in allNamespaces)
        {
            var schemaElements = new List<object>();

            if (entitiesByNs.TryGetValue(ns, out var entities))
                schemaElements.AddRange(GenerateEntityTypesForNamespace(edm, entities, ns, cache));

            if (typesByNs.TryGetValue(ns, out var types))
                schemaElements.AddRange(GenerateComplexTypesForNamespace(edm, types, cache));

            if (enumsByNs.TryGetValue(ns, out var enums))
                schemaElements.AddRange(GenerateEnumTypesForNamespace(edm, enums));

            if (servicesByNs.TryGetValue(ns, out var services))
            {
                schemaElements.AddRange(GenerateUnboundActionsForNamespace(edm, services));
                schemaElements.AddRange(GenerateUnboundFunctionsForNamespace(edm, services));
            }

            if (entitiesByNs.TryGetValue(ns, out var nsEntities))
            {
                schemaElements.AddRange(GenerateBoundActionsForNamespace(edm, nsEntities, ns));
                schemaElements.AddRange(GenerateBoundFunctionsForNamespace(edm, nsEntities, ns));
            }

            schemas.Add(new XElement(edm + "Schema",
                new XAttribute("Namespace", ns),
                schemaElements));
        }

        // Add EntityContainer in the first namespace's schema
        var containerNs = allNamespaces.FirstOrDefault() ?? "Default";
        var containerSchema = schemas.FirstOrDefault(s => s.Attribute("Namespace")?.Value == containerNs);
        if (containerSchema != null)
        {
            containerSchema.Add(GenerateEntityContainer(edm, entitiesByNs, servicesByNs));
        }
        else
        {
            schemas.Add(new XElement(edm + "Schema",
                new XAttribute("Namespace", "Default"),
                GenerateEntityContainer(edm, entitiesByNs, servicesByNs)));
        }

        // Add Capability Annotations to the container schema
        if (containerSchema != null)
        {
            foreach (var annotation in GenerateCapabilityAnnotations(edm, entitiesByNs, containerNs))
            {
                containerSchema.Add(annotation);
            }
        }

        // Add vocabulary references
        var references = new List<XElement>
        {
            new XElement(edmx + "Reference",
                new XAttribute("Uri", "https://docs.oasis-open.org/odata/odata/v4.0/errata03/csd01/complete/vocabularies/Org.OData.Core.V1.xml"),
                new XElement(edmx + "Include",
                    new XAttribute("Namespace", "Org.OData.Core.V1"),
                    new XAttribute("Alias", "Core"))),
            new XElement(edmx + "Reference",
                new XAttribute("Uri", "https://docs.oasis-open.org/odata/odata/v4.0/errata03/csd01/complete/vocabularies/Org.OData.Capabilities.V1.xml"),
                new XElement(edmx + "Include",
                    new XAttribute("Namespace", "Org.OData.Capabilities.V1"),
                    new XAttribute("Alias", "Capabilities")))
        };

        var dataServices = new XElement(edmx + "DataServices", schemas);

        var edmxDoc = new XElement(edmx + "Edmx",
            new XAttribute("Version", "4.0"),
            new XAttribute(XNamespace.Xmlns + "edmx", edmx.NamespaceName));

        foreach (var reference in references)
        {
            edmxDoc.Add(reference);
        }
        edmxDoc.Add(dataServices);

        return edmxDoc.ToString();
    }

    // ========================================================
    // Shared Utilities (used by both CsdlGenerator and controller)
    // ========================================================

    /// <summary>
    /// Get service projection rules for a given entity name.
    /// Returns (includeFields, excludeFields) from service entity exposure.
    /// </summary>
    public static (List<string>? IncludeFields, List<string>? ExcludeFields) GetServiceProjection(
        string entityName, IEnumerable<BmService> services)
    {
        foreach (var service in services)
        {
            foreach (var svcEntity in service.Entities)
            {
                var sourceEntity = svcEntity.Aspects.FirstOrDefault() ?? svcEntity.Name;
                if (sourceEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase) ||
                    sourceEntity.EndsWith("." + entityName, StringComparison.OrdinalIgnoreCase))
                {
                    if (svcEntity.IncludeFields != null || svcEntity.ExcludeFields != null)
                        return (svcEntity.IncludeFields, svcEntity.ExcludeFields);
                }
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Strip surrounding quotes and enum prefix from default value strings.
    /// </summary>
    public static string? SanitizeDefaultValue(string? defaultValue)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return defaultValue;

        if (defaultValue.Length >= 2 && defaultValue[0] == '\'' && defaultValue[^1] == '\'')
            return defaultValue[1..^1];

        if (defaultValue.StartsWith('#'))
            return defaultValue[1..];

        return defaultValue;
    }

    // ========================================================
    // CSDL Element Generators (private)
    // ========================================================

    private IEnumerable<XElement> GenerateEntityTypesForNamespace(
        XNamespace edm, List<BmEntity> entities, string ns, IMetaModelCache cache)
    {
        foreach (var entity in entities)
        {
            var (includeFields, excludeFields) = GetServiceProjection(entity.Name, cache.Services);

            var isDerived = entity.ParentEntity != null;

            // For derived entities in CSDL, only emit child-specific fields (not inherited ones).
            // For root entities, emit all fields (including inlined aspect fields).
            IEnumerable<BmField> allEntityFields;
            if (isDerived)
            {
                var parentFieldNames = new HashSet<string>(
                    entity.ParentEntity!.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                allEntityFields = entity.Fields.Where(f => !parentFieldNames.Contains(f.Name));
            }
            else
            {
                allEntityFields = entity.Fields;
            }

            var fields = allEntityFields;
            if (includeFields != null)
            {
                var include = new HashSet<string>(includeFields, StringComparer.OrdinalIgnoreCase);
                fields = fields.Where(f => f.IsKey || include.Contains(f.Name));
            }
            else if (excludeFields != null)
            {
                var exclude = new HashSet<string>(excludeFields, StringComparer.OrdinalIgnoreCase);
                fields = fields.Where(f => !exclude.Contains(f.Name));
            }

            var properties = fields
                .Select(f => GenerateProperty(edm, f, cache))
                .Where(p => p != null)
                .ToList();

            var navigationProperties = entity.Associations
                .Select(a => GenerateNavigationProperty(edm, a, ns, entity.Name, cache, containsTarget: false))
                .ToList();

            var compositionNavProps = entity.Compositions
                .Select(c => GenerateNavigationProperty(edm, c, ns, entity.Name, cache, containsTarget: true))
                .ToList();
            navigationProperties.AddRange(compositionNavProps);

            var entityType = new XElement(edm + "EntityType",
                new XAttribute("Name", entity.Name));

            if (entity.IsAbstract)
                entityType.Add(new XAttribute("Abstract", "true"));

            // Add BaseType for derived entities (OData v4 inheritance)
            if (isDerived)
            {
                var parentNs = string.IsNullOrEmpty(entity.ParentEntity!.Namespace)
                    ? "Default"
                    : entity.ParentEntity.Namespace;
                entityType.Add(new XAttribute("BaseType", $"{parentNs}.{entity.ParentEntity.Name}"));
            }

            if (entity.HasStream)
                entityType.Add(new XAttribute("HasStream", "true"));

            // Key element belongs on root type only, not on derived types
            if (!isDerived)
            {
                var keyField = entity.Fields.FirstOrDefault(f => f.IsKey);
                if (keyField != null)
                {
                    entityType.Add(new XElement(edm + "Key",
                        new XElement(edm + "PropertyRef",
                            new XAttribute("Name", keyField.Name))));
                }
            }

            entityType.Add(properties);
            entityType.Add(navigationProperties);

            yield return entityType;
        }
    }

    private IEnumerable<XElement> GenerateComplexTypesForNamespace(
        XNamespace edm, List<BmType> types, IMetaModelCache cache)
    {
        foreach (var typeDef in types)
        {
            var properties = typeDef.Fields
                .Select(f => GenerateProperty(edm, f, cache))
                .Where(p => p != null)
                .ToList();

            var complexType = new XElement(edm + "ComplexType",
                new XAttribute("Name", typeDef.Name));
            complexType.Add(properties);

            yield return complexType;
        }
    }

    private XElement? GenerateProperty(XNamespace edm, BmField field, IMetaModelCache cache)
    {
        var typeStr = !string.IsNullOrEmpty(field.TypeString)
            ? field.TypeString
            : field.TypeRef?.ToTypeString() ?? "string";
        var edmType = MetadataTypeMapper.MapToEdmType(typeStr);

        var prop = new XElement(edm + "Property",
            new XAttribute("Name", field.Name),
            new XAttribute("Type", edmType));

        if (field.IsNullable)
            prop.Add(new XAttribute("Nullable", "true"));

        var (_, maxLength, precision, scale) = MetadataTypeMapper.MapFieldType(field, cache.GetType);
        if (maxLength.HasValue)
            prop.Add(new XAttribute("MaxLength", maxLength.Value));
        if (precision.HasValue)
            prop.Add(new XAttribute("Precision", precision.Value));
        if (scale.HasValue)
            prop.Add(new XAttribute("Scale", scale.Value));

        var defaultValue = SanitizeDefaultValue(field.DefaultValueString);
        if (!string.IsNullOrEmpty(defaultValue))
            prop.Add(new XAttribute("DefaultValue", defaultValue));

        if (field.IsComputed || field.IsVirtual)
        {
            prop.Add(new XElement(edm + "Annotation",
                new XAttribute("Term", "Org.OData.Core.V1.Computed"),
                new XAttribute("Bool", "true")));
        }

        if (field.IsKey || field.IsImmutable)
        {
            prop.Add(new XElement(edm + "Annotation",
                new XAttribute("Term", "Org.OData.Core.V1.Immutable"),
                new XAttribute("Bool", "true")));
        }

        return prop;
    }

    private XElement GenerateNavigationProperty(
        XNamespace edm, BmAssociation assoc, string currentNs, string sourceEntityName,
        IMetaModelCache cache, bool containsTarget = false)
    {
        var targetType = assoc.TargetEntity;
        if (!targetType.Contains('.'))
            targetType = $"{currentNs}.{targetType}";

        var isCollection = assoc.Cardinality == BmCardinality.OneToMany ||
                           assoc.Cardinality == BmCardinality.ManyToMany;

        var navProp = new XElement(edm + "NavigationProperty",
            new XAttribute("Name", assoc.Name),
            new XAttribute("Type", isCollection ? $"Collection({targetType})" : targetType),
            new XAttribute("Nullable", assoc.MinCardinality == 0));

        if (containsTarget)
            navProp.Add(new XAttribute("ContainsTarget", "true"));

        var targetEntityDef = cache.GetEntity(assoc.TargetEntity);
        if (targetEntityDef != null)
        {
            var partnerNav = targetEntityDef.Associations
                .Concat(targetEntityDef.Compositions)
                .FirstOrDefault(a => a.TargetEntity.Equals(sourceEntityName, StringComparison.OrdinalIgnoreCase)
                    || a.TargetEntity.Equals($"{currentNs}.{sourceEntityName}", StringComparison.OrdinalIgnoreCase));
            if (partnerNav != null)
                navProp.Add(new XAttribute("Partner", partnerNav.Name));
        }

        if (!isCollection && !containsTarget)
        {
            var fkPropertyName = $"{char.ToLower(assoc.Name[0])}{assoc.Name[1..]}Id";
            var targetKeyField = targetEntityDef?.Fields.FirstOrDefault(f => f.IsKey);
            var referencedProperty = targetKeyField != null
                ? MetadataTypeMapper.ToODataPropertyName(targetKeyField.Name)
                : "Id";

            navProp.Add(new XElement(edm + "ReferentialConstraint",
                new XAttribute("Property", fkPropertyName),
                new XAttribute("ReferencedProperty", referencedProperty)));
        }

        return navProp;
    }

    private static IEnumerable<XElement> GenerateEnumTypesForNamespace(XNamespace edm, List<BmEnum> enums)
    {
        foreach (var enumDef in enums)
        {
            var members = enumDef.Values
                .Select((v, i) => new XElement(edm + "Member",
                    new XAttribute("Name", v.Name),
                    new XAttribute("Value", v.Value ?? i)))
                .ToList();

            yield return new XElement(edm + "EnumType",
                new XAttribute("Name", enumDef.Name),
                members);
        }
    }

    private static XElement GenerateEntityContainer(
        XNamespace edm,
        Dictionary<string, List<BmEntity>> entitiesByNs,
        Dictionary<string, List<BmService>> servicesByNs)
    {
        var container = new XElement(edm + "EntityContainer",
            new XAttribute("Name", "DefaultContainer"));

        foreach (var (ns, entities) in entitiesByNs)
        {
            foreach (var entity in entities)
            {
                if (entity.IsAbstract)
                    continue;

                var isSingleton = entity.HasAnnotation("OData.Singleton");

                if (isSingleton)
                {
                    var singleton = new XElement(edm + "Singleton",
                        new XAttribute("Name", entity.Name),
                        new XAttribute("Type", $"{ns}.{entity.Name}"));
                    AddNavigationPropertyBindings(edm, singleton, entity, entitiesByNs);
                    container.Add(singleton);
                }
                else
                {
                    var entitySet = new XElement(edm + "EntitySet",
                        new XAttribute("Name", entity.Name),
                        new XAttribute("EntityType", $"{ns}.{entity.Name}"));
                    AddNavigationPropertyBindings(edm, entitySet, entity, entitiesByNs);
                    container.Add(entitySet);
                }
            }
        }

        foreach (var (ns, services) in servicesByNs)
        {
            foreach (var service in services)
            {
                foreach (var action in service.Actions)
                {
                    container.Add(new XElement(edm + "ActionImport",
                        new XAttribute("Name", action.Name),
                        new XAttribute("Action", $"{ns}.{action.Name}")));
                }

                foreach (var function in service.Functions)
                {
                    var functionImport = new XElement(edm + "FunctionImport",
                        new XAttribute("Name", function.Name),
                        new XAttribute("Function", $"{ns}.{function.Name}"),
                        new XAttribute("IncludeInServiceDocument", "true"));
                    container.Add(functionImport);
                }
            }
        }

        return container;
    }

    private static void AddNavigationPropertyBindings(
        XNamespace edm,
        XElement entitySetOrSingleton,
        BmEntity entity,
        Dictionary<string, List<BmEntity>> entitiesByNs)
    {
        var allNavs = entity.Associations.Cast<BmAssociation>()
            .Concat(entity.Compositions)
            .ToList();

        foreach (var nav in allNavs)
        {
            var targetEntityName = nav.TargetEntity;
            if (targetEntityName.Contains('.'))
                targetEntityName = targetEntityName.Split('.').Last();

            entitySetOrSingleton.Add(new XElement(edm + "NavigationPropertyBinding",
                new XAttribute("Path", nav.Name),
                new XAttribute("Target", targetEntityName)));
        }
    }

    private static IEnumerable<XElement> GenerateCapabilityAnnotations(
        XNamespace edm,
        Dictionary<string, List<BmEntity>> entitiesByNs,
        string containerNs)
    {
        foreach (var (ns, entities) in entitiesByNs)
        {
            foreach (var entity in entities)
            {
                if (entity.HasAnnotation("OData.Singleton"))
                    continue;
                if (entity.IsAbstract)
                    continue;

                var target = $"{containerNs}.DefaultContainer/{entity.Name}";

                var annotations = new XElement(edm + "Annotations",
                    new XAttribute("Target", target));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.FilterRestrictions",
                    ("Filterable", "true")));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.SortRestrictions",
                    ("Sortable", "true")));

                var expandable = entity.Associations.Count > 0 || entity.Compositions.Count > 0;
                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.ExpandRestrictions",
                    ("Expandable", expandable.ToString().ToLower())));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.SearchRestrictions",
                    ("Searchable", "true")));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.InsertRestrictions",
                    ("Insertable", "true")));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.UpdateRestrictions",
                    ("Updatable", "true")));

                annotations.Add(CreateCapabilityRecord(edm,
                    "Org.OData.Capabilities.V1.DeleteRestrictions",
                    ("Deletable", "true")));

                yield return annotations;
            }
        }
    }

    private static XElement CreateCapabilityRecord(XNamespace edm, string term, params (string prop, string value)[] properties)
    {
        var record = new XElement(edm + "Record");
        foreach (var (prop, value) in properties)
        {
            record.Add(new XElement(edm + "PropertyValue",
                new XAttribute("Property", prop),
                new XAttribute("Bool", value)));
        }

        return new XElement(edm + "Annotation",
            new XAttribute("Term", term),
            record);
    }

    private static IEnumerable<XElement> GenerateUnboundActionsForNamespace(XNamespace edm, List<BmService> services)
    {
        foreach (var service in services)
        {
            foreach (var action in service.Actions)
            {
                var actionElement = new XElement(edm + "Action",
                    new XAttribute("Name", action.Name));

                foreach (var param in action.Parameters)
                {
                    actionElement.Add(new XElement(edm + "Parameter",
                        new XAttribute("Name", param.Name),
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(param.Type))));
                }

                if (!string.IsNullOrEmpty(action.ReturnType))
                {
                    actionElement.Add(new XElement(edm + "ReturnType",
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(action.ReturnType))));
                }

                yield return actionElement;
            }
        }
    }

    private static IEnumerable<XElement> GenerateUnboundFunctionsForNamespace(XNamespace edm, List<BmService> services)
    {
        foreach (var service in services)
        {
            foreach (var function in service.Functions)
            {
                var functionElement = new XElement(edm + "Function",
                    new XAttribute("Name", function.Name));

                if (function.IsComposable)
                    functionElement.Add(new XAttribute("IsComposable", "true"));

                foreach (var param in function.Parameters)
                {
                    functionElement.Add(new XElement(edm + "Parameter",
                        new XAttribute("Name", param.Name),
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(param.Type))));
                }

                if (!string.IsNullOrEmpty(function.ReturnType))
                {
                    functionElement.Add(new XElement(edm + "ReturnType",
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(function.ReturnType))));
                }

                yield return functionElement;
            }
        }
    }

    private static IEnumerable<XElement> GenerateBoundActionsForNamespace(
        XNamespace edm, List<BmEntity> entities, string ns)
    {
        foreach (var entity in entities)
        {
            foreach (var action in entity.BoundActions)
            {
                var actionElement = new XElement(edm + "Action",
                    new XAttribute("Name", action.Name),
                    new XAttribute("IsBound", "true"));

                actionElement.Add(new XElement(edm + "Parameter",
                    new XAttribute("Name", "bindingParameter"),
                    new XAttribute("Type", $"{ns}.{entity.Name}")));

                foreach (var param in action.Parameters)
                {
                    actionElement.Add(new XElement(edm + "Parameter",
                        new XAttribute("Name", param.Name),
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(param.Type))));
                }

                if (!string.IsNullOrEmpty(action.ReturnType))
                {
                    actionElement.Add(new XElement(edm + "ReturnType",
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(action.ReturnType))));
                }

                yield return actionElement;
            }
        }
    }

    private static IEnumerable<XElement> GenerateBoundFunctionsForNamespace(
        XNamespace edm, List<BmEntity> entities, string ns)
    {
        foreach (var entity in entities)
        {
            foreach (var function in entity.BoundFunctions)
            {
                var functionElement = new XElement(edm + "Function",
                    new XAttribute("Name", function.Name),
                    new XAttribute("IsBound", "true"));

                if (function.IsComposable)
                    functionElement.Add(new XAttribute("IsComposable", "true"));

                functionElement.Add(new XElement(edm + "Parameter",
                    new XAttribute("Name", "bindingParameter"),
                    new XAttribute("Type", $"{ns}.{entity.Name}")));

                foreach (var param in function.Parameters)
                {
                    functionElement.Add(new XElement(edm + "Parameter",
                        new XAttribute("Name", param.Name),
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(param.Type))));
                }

                if (!string.IsNullOrEmpty(function.ReturnType))
                {
                    functionElement.Add(new XElement(edm + "ReturnType",
                        new XAttribute("Type", MetadataTypeMapper.MapToEdmType(function.ReturnType))));
                }

                yield return functionElement;
            }
        }
    }
}
