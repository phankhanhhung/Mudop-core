using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities.Normalized;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmType, BmEnum, and BmAspect objects.
/// </summary>
internal sealed class EnumTypeAspectPersister
{
    private readonly RepositoryContext _ctx;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _typeAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _typeFieldAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _enumValueAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _enumAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _aspectAnnotationsByOwner;

    public EnumTypeAspectPersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    // ============================================================
    // Type Operations
    // ============================================================

    public async Task SaveTypeAsync(BmType type, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(type.Namespace, ct);
        var existing = await _ctx.Db.Types.Include(t => t.Fields).FirstOrDefaultAsync(t => t.QualifiedName == type.QualifiedName, ct);
        if (existing != null)
        {
            var existingFieldIds = existing.Fields.ToDictionary(f => f.Name, f => f.Id);
            await _ctx.Db.NormalizedAnnotations.Where(a => a.OwnerType == "type" && a.OwnerId == existing.Id).ExecuteDeleteAsync(ct);
            var oldTypeFieldIds = existing.Fields.Select(f => f.Id).ToList();
            if (oldTypeFieldIds.Count > 0)
                await _ctx.Db.NormalizedAnnotations.Where(a => a.OwnerType == "type_field" && oldTypeFieldIds.Contains(a.OwnerId)).ExecuteDeleteAsync(ct);
            existing.Name = type.Name; existing.BaseType = type.BaseType;
            existing.Length = type.Length; existing.Precision = type.Precision; existing.Scale = type.Scale;
            existing.Fields.Clear();
            MapTypeFields(type, existing, existingFieldIds);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, type.Annotations, "type", existing.Id);
        }
        else
        {
            var record = new TypeRecord
            {
                Id = Guid.NewGuid(), TenantId = _ctx.TenantId, Name = type.Name,
                QualifiedName = type.QualifiedName, NamespaceId = ns?.Id,
                BaseType = type.BaseType, Length = type.Length, Precision = type.Precision, Scale = type.Scale
            };
            MapTypeFields(type, record);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, type.Annotations, "type", record.Id);
            _ctx.Db.Types.Add(record);
        }
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public void MapTypeFields(BmType type, TypeRecord record, IReadOnlyDictionary<string, Guid>? existingFieldIds = null)
    {
        int pos = 0;
        foreach (var f in type.Fields)
        {
            var fieldId = existingFieldIds?.GetValueOrDefault(f.Name) ?? Guid.NewGuid();
            record.Fields.Add(new TypeField
            {
                Id = fieldId, TypeId = record.Id, Name = f.Name,
                TypeString = f.TypeString, IsNullable = f.IsNullable,
                DefaultValue = f.DefaultValueString, Position = pos++
            });
            if (f.Annotations.Count > 0)
                AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, f.Annotations, "type_field", fieldId);
        }
    }

    public async Task SaveTypesAsync(IEnumerable<BmType> types, CancellationToken ct = default)
    {
        foreach (var type in types) await SaveTypeAsync(type, ct);
    }

    public async Task<IReadOnlyList<TypeRecord>> GetTypesAsync(string? ns = null, CancellationToken ct = default)
    {
        var query = _ctx.Db.Types.AsNoTracking().Include(t => t.Fields).AsSplitQuery();
        if (!string.IsNullOrEmpty(ns))
            query = query.Where(t => t.Namespace != null && t.Namespace.Name == ns);
        return await query.OrderBy(t => t.QualifiedName).ToListAsync(ct);
    }

    public async Task<List<TypeRecord>> LoadTypesAsync(CancellationToken ct)
    {
        var types = await _ctx.Db.Types.AsNoTracking()
            .Where(t => t.TenantId == _ctx.TenantId)
            .Include(t => t.Fields).Include(t => t.Namespace)
            .AsSplitQuery().ToListAsync(ct);
        if (types.Count > 0)
        {
            var typeIds = types.Select(t => t.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations.AsNoTracking()
                .Where(a => a.OwnerType == "type" && typeIds.Contains(a.OwnerId)).ToListAsync(ct);
            _typeAnnotationsByOwner = annotations.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
            var typeFieldIds = types.SelectMany(t => t.Fields).Select(f => f.Id).ToHashSet();
            if (typeFieldIds.Count > 0)
            {
                var tfAnnotations = await _ctx.Db.NormalizedAnnotations.AsNoTracking()
                    .Where(a => a.OwnerType == "type_field" && typeFieldIds.Contains(a.OwnerId)).ToListAsync(ct);
                _typeFieldAnnotationsByOwner = tfAnnotations.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
            }
            else _typeFieldAnnotationsByOwner = new();
        }
        else
        {
            _typeAnnotationsByOwner = new();
            _typeFieldAnnotationsByOwner = new();
        }
        return types;
    }

    public BmType MapToBmType(TypeRecord record)
    {
        var type = new BmType
        {
            Name = record.Name, Namespace = record.Namespace?.Name ?? "",
            BaseType = record.BaseType ?? "", Length = record.Length,
            Precision = record.Precision, Scale = record.Scale
        };
        foreach (var f in record.Fields.OrderBy(x => x.Position))
        {
            var bmField = new BmField { Name = f.Name, TypeString = f.TypeString, IsNullable = f.IsNullable, DefaultValueString = f.DefaultValue };
            if (_typeFieldAnnotationsByOwner != null && _typeFieldAnnotationsByOwner.TryGetValue(f.Id, out var tfAnn))
                foreach (var ann in AnnotationHelper.ReconstructAnnotations(tfAnn)) bmField.Annotations.Add(ann);
            type.Fields.Add(bmField);
        }
        if (_typeAnnotationsByOwner != null && _typeAnnotationsByOwner.TryGetValue(record.Id, out var typeAnn))
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(typeAnn)) type.Annotations.Add(ann);
        return type;
    }

    // ============================================================
    // Enum Operations
    // ============================================================

    public async Task SaveEnumAsync(BmEnum enumType, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(enumType.Namespace, ct);
        var existing = await _ctx.Db.Enums.Include(e => e.Values).FirstOrDefaultAsync(e => e.QualifiedName == enumType.QualifiedName, ct);
        if (existing != null)
        {
            var existingValueIds = existing.Values.ToDictionary(v => v.Name, v => v.Id);
            var oldValueIds = existing.Values.Select(v => v.Id).ToList();
            if (oldValueIds.Count > 0)
                await _ctx.Db.NormalizedAnnotations.Where(a => a.OwnerType == "enum_value" && oldValueIds.Contains(a.OwnerId)).ExecuteDeleteAsync(ct);
            await _ctx.Db.NormalizedAnnotations.Where(a => a.OwnerType == "enum" && a.OwnerId == existing.Id).ExecuteDeleteAsync(ct);
            existing.Name = enumType.Name; existing.BaseType = enumType.BaseType;
            existing.Values.Clear();
            MapEnumValues(enumType, existing, existingValueIds);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, enumType.Annotations, "enum", existing.Id);
        }
        else
        {
            var record = new EnumRecord
            {
                Id = Guid.NewGuid(), TenantId = _ctx.TenantId, Name = enumType.Name,
                QualifiedName = enumType.QualifiedName, NamespaceId = ns?.Id, BaseType = enumType.BaseType
            };
            MapEnumValues(enumType, record);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, enumType.Annotations, "enum", record.Id);
            _ctx.Db.Enums.Add(record);
        }
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public void MapEnumValues(BmEnum enumType, EnumRecord record, IReadOnlyDictionary<string, Guid>? existingValueIds = null)
    {
        int pos = 0;
        foreach (var v in enumType.Values)
        {
            var valueId = existingValueIds?.GetValueOrDefault(v.Name) ?? Guid.NewGuid();
            record.Values.Add(new EnumValue { Id = valueId, EnumId = record.Id, Name = v.Name, Value = v.Value?.ToString(), Position = pos++ });
            foreach (var annotation in v.Annotations)
            {
                string? value = null;
                if (annotation.Properties?.Count > 0)
                    value = System.Text.Json.JsonSerializer.Serialize(annotation.Properties);
                else if (annotation.Value != null)
                    value = System.Text.Json.JsonSerializer.Serialize(annotation.Value);
                _ctx.Db.NormalizedAnnotations.Add(new NormalizedAnnotation
                {
                    Id = Guid.NewGuid(), OwnerType = "enum_value", OwnerId = valueId,
                    Name = annotation.Name, Value = value
                });
            }
        }
    }

    public async Task SaveEnumsAsync(IEnumerable<BmEnum> enums, CancellationToken ct = default)
    {
        foreach (var e in enums) await SaveEnumAsync(e, ct);
    }

    public async Task<IReadOnlyList<EnumRecord>> GetEnumsAsync(string? ns = null, CancellationToken ct = default)
    {
        var query = _ctx.Db.Enums.AsNoTracking().Include(e => e.Values).AsSplitQuery();
        if (!string.IsNullOrEmpty(ns))
            query = query.Where(e => e.Namespace != null && e.Namespace.Name == ns);
        return await query.OrderBy(e => e.QualifiedName).ToListAsync(ct);
    }

    public async Task<List<EnumRecord>> LoadEnumsAsync(CancellationToken ct)
    {
        var enums = await _ctx.Db.Enums.AsNoTracking()
            .Where(e => e.TenantId == _ctx.TenantId)
            .Include(e => e.Values).Include(e => e.Namespace)
            .AsSplitQuery().ToListAsync(ct);
        if (enums.Count > 0)
        {
            var enumValueIds = enums.SelectMany(e => e.Values).Select(v => v.Id).ToHashSet();
            if (enumValueIds.Count > 0)
            {
                var ann = await _ctx.Db.NormalizedAnnotations.AsNoTracking()
                    .Where(a => a.OwnerType == "enum_value" && enumValueIds.Contains(a.OwnerId)).ToListAsync(ct);
                _enumValueAnnotationsByOwner = ann.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
            }
            else _enumValueAnnotationsByOwner = new();
            var enumIds = enums.Select(e => e.Id).ToHashSet();
            var enumAnn = await _ctx.Db.NormalizedAnnotations.AsNoTracking()
                .Where(a => a.OwnerType == "enum" && enumIds.Contains(a.OwnerId)).ToListAsync(ct);
            _enumAnnotationsByOwner = enumAnn.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
        }
        else _enumAnnotationsByOwner = new();
        return enums;
    }

    public BmEnum MapToBmEnum(EnumRecord record)
    {
        var enumType = new BmEnum { Name = record.Name, Namespace = record.Namespace?.Name ?? "", BaseType = record.BaseType };
        if (_enumAnnotationsByOwner != null && _enumAnnotationsByOwner.TryGetValue(record.Id, out var enumAnn))
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(enumAnn)) enumType.Annotations.Add(ann);
        foreach (var v in record.Values.OrderBy(x => x.Position))
        {
            var enumValue = new BmEnumValue { Name = v.Name, Value = v.Value };
            if (_enumValueAnnotationsByOwner != null && _enumValueAnnotationsByOwner.TryGetValue(v.Id, out var valAnn))
                foreach (var ann in AnnotationHelper.ReconstructAnnotations(valAnn)) enumValue.Annotations.Add(ann);
            enumType.Values.Add(enumValue);
        }
        return enumType;
    }

    // ============================================================
    // Aspect Operations
    // ============================================================

    public async Task SaveAspectAsync(BmAspect aspect, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(aspect.Namespace, ct);
        var existing = await _ctx.Db.Aspects.Include(a => a.Fields).Include(a => a.Includes)
            .FirstOrDefaultAsync(a => a.QualifiedName == aspect.QualifiedName, ct);
        if (existing != null)
        {
            var existingFieldIds = existing.Fields.ToDictionary(f => f.Name, f => f.Id);
            await _ctx.Db.NormalizedAnnotations.Where(a => a.OwnerType == "aspect" && a.OwnerId == existing.Id).ExecuteDeleteAsync(ct);
            existing.Name = aspect.Name; existing.Fields.Clear(); existing.Includes.Clear();
            MapAspectChildren(aspect, existing, existingFieldIds);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, aspect.Annotations, "aspect", existing.Id);
        }
        else
        {
            var record = new AspectRecord
            {
                Id = Guid.NewGuid(), TenantId = _ctx.TenantId, Name = aspect.Name,
                QualifiedName = aspect.QualifiedName, NamespaceId = ns?.Id
            };
            MapAspectChildren(aspect, record);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, aspect.Annotations, "aspect", record.Id);
            _ctx.Db.Aspects.Add(record);
        }
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public void MapAspectChildren(BmAspect aspect, AspectRecord record, IReadOnlyDictionary<string, Guid>? existingFieldIds = null)
    {
        int pos = 0;
        foreach (var inc in aspect.Includes)
            record.Includes.Add(new AspectInclude { AspectId = record.Id, IncludedAspectName = inc, Position = pos++ });
        pos = 0;
        foreach (var f in aspect.Fields)
        {
            var fieldId = existingFieldIds?.GetValueOrDefault(f.Name) ?? Guid.NewGuid();
            record.Fields.Add(new AspectField
            {
                Id = fieldId, AspectId = record.Id, Name = f.Name,
                TypeString = f.TypeString, IsKey = f.IsKey, IsNullable = f.IsNullable, Position = pos++
            });
        }
    }

    public async Task SaveAspectsAsync(IEnumerable<BmAspect> aspects, CancellationToken ct = default)
    {
        foreach (var a in aspects) await SaveAspectAsync(a, ct);
    }

    public async Task<IReadOnlyList<AspectRecord>> GetAspectsAsync(string? ns = null, CancellationToken ct = default)
    {
        var query = _ctx.Db.Aspects.AsNoTracking().Include(a => a.Fields).Include(a => a.Includes).AsSplitQuery();
        if (!string.IsNullOrEmpty(ns))
            query = query.Where(a => a.Namespace != null && a.Namespace.Name == ns);
        return await query.OrderBy(a => a.QualifiedName).ToListAsync(ct);
    }

    public async Task<List<AspectRecord>> LoadAspectsAsync(CancellationToken ct)
    {
        var aspects = await _ctx.Db.Aspects.AsNoTracking()
            .Where(a => a.TenantId == _ctx.TenantId)
            .Include(a => a.Fields).Include(a => a.Includes).Include(a => a.Namespace)
            .AsSplitQuery().ToListAsync(ct);
        if (aspects.Count > 0)
        {
            var aspectIds = aspects.Select(a => a.Id).ToHashSet();
            var ann = await _ctx.Db.NormalizedAnnotations.AsNoTracking()
                .Where(a => a.OwnerType == "aspect" && aspectIds.Contains(a.OwnerId)).ToListAsync(ct);
            _aspectAnnotationsByOwner = ann.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
        }
        else _aspectAnnotationsByOwner = new();
        return aspects;
    }

    public BmAspect MapToBmAspect(AspectRecord record)
    {
        var aspect = new BmAspect { Name = record.Name, Namespace = record.Namespace?.Name ?? "" };
        foreach (var inc in record.Includes.OrderBy(x => x.Position))
            aspect.Includes.Add(inc.IncludedAspectName);
        foreach (var f in record.Fields.OrderBy(x => x.Position))
            aspect.Fields.Add(new BmField { Name = f.Name, TypeString = f.TypeString, IsKey = f.IsKey, IsNullable = f.IsNullable });
        if (_aspectAnnotationsByOwner != null && _aspectAnnotationsByOwner.TryGetValue(record.Id, out var aspectAnn))
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(aspectAnn)) aspect.Annotations.Add(ann);
        return aspect;
    }
}
