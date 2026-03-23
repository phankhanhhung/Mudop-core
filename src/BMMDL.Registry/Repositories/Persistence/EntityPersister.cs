using BMMDL.MetaModel.Abstractions;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Service;
using BMMDL.Registry.Entities.Normalized;
using BMMDL.Registry.Repositories.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmEntity objects.
/// </summary>
internal sealed class EntityPersister
{
    private readonly RepositoryContext _ctx;

    // Temporary storage populated during LoadEntitiesAsync, consumed during MapToBmEntity
    private Dictionary<Guid, List<NormalizedAnnotation>>? _fieldAnnotationsByOwner;
    private Dictionary<Guid, List<ExpressionNode>>? _fieldExpressionNodesByOwner;

    public EntityPersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SaveEntityAsync(BmEntity entity, CancellationToken ct = default)
    {
        await using var transaction = await _ctx.Db.Database.BeginTransactionAsync(ct);
        try
        {
            await SaveEntityInternalAsync(entity, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task SaveEntityInternalAsync(BmEntity entity, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(entity.Namespace, ct);

        var existing = await _ctx.Db.Entities
            .Include(e => e.Fields)
            .Include(e => e.Associations)
            .Include(e => e.AspectRefs)
            .Include(e => e.Indexes).ThenInclude(i => i.Fields)
            .Include(e => e.Constraints).ThenInclude(c => c.Fields)
            .FirstOrDefaultAsync(e => e.QualifiedName == entity.QualifiedName, ct);

        if (existing != null)
        {
            var existingId = existing.Id;
            var existingFieldIds = existing.Fields.ToDictionary(f => f.Name, f => f.Id);
            var existingAssocIds = existing.Associations.ToDictionary(a => a.Name, a => a.Id);
            var existingIndexIds = existing.Indexes.ToDictionary(i => i.Name ?? "", i => i.Id);
            var existingConstraintIds = existing.Constraints.ToDictionary(c => c.Name ?? "", c => c.Id);
            var existingFieldExprs = existing.Fields.ToDictionary(
                f => f.Name,
                f => (ComputedExpr: f.ComputedExpr, ComputedExprRootId: f.ComputedExprRootId,
                      DefaultValue: f.DefaultValue, DefaultValueExprRootId: f.DefaultValueExprRootId));
            var existingAssocExprs = existing.Associations.ToDictionary(
                a => a.Name,
                a => (OnCondition: a.OnCondition, OnConditionExprRootId: a.OnConditionExprRootId));
            var existingConstraintExprs = existing.Constraints
                .Where(c => c.ConditionExprRootId != null)
                .ToDictionary(
                    c => c.Name ?? "",
                    c => (ConditionExpr: c.ConditionExpr, ConditionExprRootId: c.ConditionExprRootId));

            await _ctx.Db.Set<EntityField>().Where(f => f.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityAssociation>().Where(a => a.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityAspectRef>().Where(a => a.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityIndexField>().Where(f => f.Index.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityIndex>().Where(i => i.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityConstraintField>().Where(f => f.Constraint.EntityId == existingId).ExecuteDeleteAsync(ct);
            await _ctx.Db.Set<EntityConstraint>().Where(c => c.EntityId == existingId).ExecuteDeleteAsync(ct);

            await _ctx.Db.NormalizedAnnotations
                .Where(a => a.OwnerType == "entity" && a.OwnerId == existingId)
                .ExecuteDeleteAsync(ct);

            var oldFieldIds = existing.Fields.Select(f => f.Id).ToList();
            if (oldFieldIds.Count > 0)
            {
                await _ctx.Db.NormalizedAnnotations
                    .Where(a => a.OwnerType == "field" && oldFieldIds.Contains(a.OwnerId))
                    .ExecuteDeleteAsync(ct);
            }

            var newFieldsByName = entity.Fields.ToDictionary(f => f.Name);
            var fieldIdsToDeleteExprs = new List<Guid>();
            foreach (var existingField in existing.Fields)
            {
                if (!newFieldsByName.TryGetValue(existingField.Name, out var newField))
                {
                    if (existingField.ComputedExprRootId != null || existingField.DefaultValueExprRootId != null)
                        fieldIdsToDeleteExprs.Add(existingField.Id);
                }
                else
                {
                    if (existingField.ComputedExprRootId != null && existingField.ComputedExpr != newField.ComputedExprString)
                        fieldIdsToDeleteExprs.Add(existingField.Id);
                    if (existingField.DefaultValueExprRootId != null && existingField.DefaultValue != newField.DefaultValueString)
                        fieldIdsToDeleteExprs.Add(existingField.Id);
                }
            }

            var newAssocsByName = entity.Associations.Concat(entity.Compositions).ToDictionary(a => a.Name);
            var assocIdsToDeleteExprs = new List<Guid>();
            foreach (var existingAssoc in existing.Associations)
            {
                if (!newAssocsByName.TryGetValue(existingAssoc.Name, out var newAssoc))
                {
                    if (existingAssoc.OnConditionExprRootId != null)
                        assocIdsToDeleteExprs.Add(existingAssoc.Id);
                }
                else
                {
                    if (existingAssoc.OnConditionExprRootId != null && existingAssoc.OnCondition != newAssoc.OnConditionString)
                        assocIdsToDeleteExprs.Add(existingAssoc.Id);
                }
            }

            if (fieldIdsToDeleteExprs.Count > 0)
            {
                await _ctx.Db.ExpressionNodes
                    .Where(n => n.OwnerType == "entity_field" && fieldIdsToDeleteExprs.Contains(n.OwnerId))
                    .ExecuteDeleteAsync(ct);
            }
            if (assocIdsToDeleteExprs.Count > 0)
            {
                await _ctx.Db.ExpressionNodes
                    .Where(n => n.OwnerType == "entity_association" && assocIdsToDeleteExprs.Contains(n.OwnerId))
                    .ExecuteDeleteAsync(ct);
            }

            var newConstraintsByName = entity.Constraints.ToDictionary(c => c.Name ?? "");
            var constraintIdsToDeleteExprs = new List<Guid>();
            foreach (var existingCons in existing.Constraints.Where(c => c.ConditionExprRootId != null))
            {
                if (!newConstraintsByName.TryGetValue(existingCons.Name ?? "", out var newCons))
                {
                    constraintIdsToDeleteExprs.Add(existingCons.Id);
                }
                else if (newCons is BmCheckConstraint newCheck && existingCons.ConditionExpr != newCheck.ConditionString)
                {
                    constraintIdsToDeleteExprs.Add(existingCons.Id);
                }
            }
            if (constraintIdsToDeleteExprs.Count > 0)
            {
                await _ctx.Db.ExpressionNodes
                    .Where(n => n.OwnerType == "entity_constraint" && constraintIdsToDeleteExprs.Contains(n.OwnerId))
                    .ExecuteDeleteAsync(ct);
            }

            await _ctx.Db.Entities.Where(e => e.Id == existingId).ExecuteDeleteAsync(ct);

            foreach (var field in existing.Fields.ToList()) _ctx.Db.Entry(field).State = EntityState.Detached;
            foreach (var assoc in existing.Associations.ToList()) _ctx.Db.Entry(assoc).State = EntityState.Detached;
            foreach (var aspect in existing.AspectRefs.ToList()) _ctx.Db.Entry(aspect).State = EntityState.Detached;
            foreach (var idx in existing.Indexes.ToList())
            {
                foreach (var idxField in idx.Fields.ToList()) _ctx.Db.Entry(idxField).State = EntityState.Detached;
                _ctx.Db.Entry(idx).State = EntityState.Detached;
            }
            foreach (var con in existing.Constraints.ToList())
            {
                foreach (var conField in con.Fields.ToList()) _ctx.Db.Entry(conField).State = EntityState.Detached;
                _ctx.Db.Entry(con).State = EntityState.Detached;
            }
            _ctx.Db.Entry(existing).State = EntityState.Detached;

            var newRecord = new EntityRecord
            {
                Id = existingId,
                TenantId = _ctx.TenantId,
                ModuleId = _ctx.ModuleId,
                Name = entity.Name,
                QualifiedName = entity.QualifiedName,
                NamespaceId = ns?.Id,
                IsTenantScoped = entity.TenantScoped,
                IsAbstract = entity.IsAbstract,
                ParentEntityName = entity.ParentEntityName,
                DiscriminatorValue = entity.DiscriminatorValue,
                ExtendsFrom = entity.ExtendsFrom,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };
            MapEntityChildren(entity, newRecord, existingFieldIds, existingAssocIds, existingIndexIds, existingConstraintIds, existingFieldExprs, existingAssocExprs, existingConstraintExprs);
            _ctx.Db.Entities.Add(newRecord);
            await _ctx.Db.SaveChangesAsync(ct);
            _ctx.Db.Entry(newRecord).State = EntityState.Detached;
        }
        else
        {
            var record = new EntityRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                ModuleId = _ctx.ModuleId,
                Name = entity.Name,
                QualifiedName = entity.QualifiedName,
                NamespaceId = ns?.Id,
                IsTenantScoped = entity.TenantScoped,
                IsAbstract = entity.IsAbstract,
                ParentEntityName = entity.ParentEntityName,
                DiscriminatorValue = entity.DiscriminatorValue,
                ExtendsFrom = entity.ExtendsFrom
            };
            MapEntityChildren(entity, record);
            _ctx.Db.Entities.Add(record);
            await _ctx.Db.SaveChangesAsync(ct);
        }
    }

    public void MapEntityChildren(
        BmEntity entity,
        EntityRecord record,
        IReadOnlyDictionary<string, Guid>? existingFieldIds = null,
        IReadOnlyDictionary<string, Guid>? existingAssocIds = null,
        IReadOnlyDictionary<string, Guid>? existingIndexIds = null,
        IReadOnlyDictionary<string, Guid>? existingConstraintIds = null,
        IReadOnlyDictionary<string, (string? ComputedExpr, Guid? ComputedExprRootId, string? DefaultValue, Guid? DefaultValueExprRootId)>? existingFieldExprs = null,
        IReadOnlyDictionary<string, (string? OnCondition, Guid? OnConditionExprRootId)>? existingAssocExprs = null,
        IReadOnlyDictionary<string, (string? ConditionExpr, Guid? ConditionExprRootId)>? existingConstraintExprs = null)
    {
        int pos = 0;
        foreach (var field in entity.Fields)
        {
            var fieldId = existingFieldIds?.GetValueOrDefault(field.Name) ?? Guid.NewGuid();
            var fieldRecord = new EntityField
            {
                Id = fieldId,
                EntityId = record.Id,
                Name = field.Name,
                TypeString = field.TypeString,
                IsKey = field.IsKey,
                IsNullable = field.IsNullable,
                IsVirtual = field.IsVirtual,
                IsReadonly = field.IsReadonly,
                IsImmutable = field.IsImmutable,
                IsComputed = field.IsComputed,
                IsStored = field.IsStored,
                ComputedStrategy = field.ComputedStrategy?.ToString(),
                ComputedExpr = field.ComputedExprString,
                DefaultValue = field.DefaultValueString,
                Position = pos++
            };

            if (field.ComputedExpr != null)
            {
                var existingExpr = existingFieldExprs?.GetValueOrDefault(field.Name);
                if (existingExpr?.ComputedExpr == field.ComputedExprString && existingExpr?.ComputedExprRootId != null)
                    fieldRecord.ComputedExprRootId = existingExpr.Value.ComputedExprRootId;
                else
                    fieldRecord.ComputedExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(field.ComputedExpr, fieldRecord.Id, "entity_field", "computed_expr");
            }

            if (field.DefaultExpr != null)
            {
                var existingExpr = existingFieldExprs?.GetValueOrDefault(field.Name);
                if (existingExpr?.DefaultValue == field.DefaultValueString && existingExpr?.DefaultValueExprRootId != null)
                    fieldRecord.DefaultValueExprRootId = existingExpr.Value.DefaultValueExprRootId;
                else
                    fieldRecord.DefaultValueExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(field.DefaultExpr, fieldRecord.Id, "entity_field", "default_value");
            }

            record.Fields.Add(fieldRecord);

            foreach (var annotation in field.Annotations)
            {
                string? value = null;
                if (annotation.Properties?.Count > 0)
                    value = System.Text.Json.JsonSerializer.Serialize(annotation.Properties);
                else if (annotation.Value != null)
                    value = System.Text.Json.JsonSerializer.Serialize(annotation.Value);
                _ctx.Db.NormalizedAnnotations.Add(new NormalizedAnnotation
                {
                    Id = Guid.NewGuid(), OwnerType = "field", OwnerId = fieldRecord.Id,
                    Name = annotation.Name, Value = value
                });
            }
        }

        pos = 0;
        foreach (var assoc in entity.Associations)
        {
            var assocId = existingAssocIds?.GetValueOrDefault(assoc.Name) ?? Guid.NewGuid();
            var assocRecord = new EntityAssociation
            {
                Id = assocId,
                EntityId = record.Id,
                Name = assoc.Name,
                TargetEntityName = assoc.TargetEntity,
                OnCondition = assoc.OnConditionString,
                IsComposition = assoc is BmComposition,
                Cardinality = (int)assoc.Cardinality,
                MinCardinality = assoc.MinCardinality,
                MaxCardinality = assoc.MaxCardinality,
                OnDeleteAction = assoc.OnDelete?.ToString()
            };
            if (assoc.OnConditionExpr != null)
            {
                var existingExpr = existingAssocExprs?.GetValueOrDefault(assoc.Name);
                if (existingExpr?.OnCondition == assoc.OnConditionString && existingExpr?.OnConditionExprRootId != null)
                    assocRecord.OnConditionExprRootId = existingExpr.Value.OnConditionExprRootId;
                else
                    assocRecord.OnConditionExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(assoc.OnConditionExpr, assocRecord.Id, "entity_association", "on_condition");
            }
            record.Associations.Add(assocRecord);
        }

        foreach (var comp in entity.Compositions)
        {
            var compId = existingAssocIds?.GetValueOrDefault(comp.Name) ?? Guid.NewGuid();
            record.Associations.Add(new EntityAssociation
            {
                Id = compId, EntityId = record.Id, Name = comp.Name,
                TargetEntityName = comp.TargetEntity, OnCondition = comp.OnConditionString,
                IsComposition = true, Cardinality = (int)comp.Cardinality,
                MinCardinality = comp.MinCardinality, MaxCardinality = comp.MaxCardinality,
                OnDeleteAction = comp.OnDelete?.ToString()
            });
        }

        pos = 0;
        foreach (var aspect in entity.Aspects)
        {
            record.AspectRefs.Add(new EntityAspectRef { EntityId = record.Id, AspectName = aspect, Position = pos++ });
        }

        foreach (var idx in entity.Indexes)
        {
            var indexId = existingIndexIds?.GetValueOrDefault(idx.Name ?? "") ?? Guid.NewGuid();
            var indexRecord = new EntityIndex
            {
                Id = indexId, EntityId = record.Id, Name = idx.Name ?? "",
                IsUnique = idx.IsUnique, Expression = idx.Expression
            };
            pos = 0;
            foreach (var f in idx.Fields)
                indexRecord.Fields.Add(new EntityIndexField { IndexId = indexRecord.Id, FieldName = f, Position = pos++ });
            record.Indexes.Add(indexRecord);
        }

        foreach (var cons in entity.Constraints)
        {
            var consId = existingConstraintIds?.GetValueOrDefault(cons.Name ?? "") ?? Guid.NewGuid();
            var consRecord = new EntityConstraint
            {
                Id = consId, EntityId = record.Id, Name = cons.Name ?? "",
                ConstraintType = cons switch { BmCheckConstraint => "check", BmUniqueConstraint => "unique", BmForeignKeyConstraint => "foreign_key", _ => "unknown" },
                ConditionExpr = cons is BmCheckConstraint cc ? cc.ConditionString : null,
                ReferencedEntity = cons is BmForeignKeyConstraint fk ? fk.ReferencedEntity : null
            };
            if (cons is BmUniqueConstraint uc)
            {
                pos = 0;
                foreach (var f in uc.Fields)
                    consRecord.Fields.Add(new EntityConstraintField { ConstraintId = consRecord.Id, FieldName = f, Position = pos++ });
            }
            else if (cons is BmForeignKeyConstraint fkc)
            {
                pos = 0;
                foreach (var f in fkc.Fields)
                    consRecord.Fields.Add(new EntityConstraintField { ConstraintId = consRecord.Id, FieldName = f, Position = pos++, IsReferenced = false });
                pos = 0;
                foreach (var f in fkc.ReferencedFields)
                    consRecord.Fields.Add(new EntityConstraintField { ConstraintId = consRecord.Id, FieldName = f, Position = pos++, IsReferenced = true });
            }
            if (cons is BmCheckConstraint checkCons && checkCons.Condition != null)
            {
                var existingExpr = existingConstraintExprs?.GetValueOrDefault(cons.Name ?? "");
                if (existingExpr?.ConditionExpr == checkCons.ConditionString && existingExpr?.ConditionExprRootId != null)
                    consRecord.ConditionExprRootId = existingExpr.Value.ConditionExprRootId;
                else
                    consRecord.ConditionExprRootId = _ctx.ExprSerializer.SaveExpressionNodes(checkCons.Condition, consRecord.Id, "entity_constraint", "condition_expr");
            }
            record.Constraints.Add(consRecord);
        }

        // Entity-level annotations
        AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, entity.Annotations, "entity", record.Id);

        // Bound Actions and Functions
        if (entity.BoundActions.Count > 0 || entity.BoundFunctions.Count > 0)
        {
            var moduleId = record.ModuleId ?? _ctx.ModuleId;
            SaveBoundOperations(entity, record, moduleId);
        }
    }

    public async Task SaveEntitiesAsync(IEnumerable<BmEntity> entities, CancellationToken ct = default)
    {
        foreach (var entity in entities)
            await SaveEntityAsync(entity, ct);
    }

    public async Task<EntityRecord?> GetEntityByNameAsync(string qualifiedName, CancellationToken ct = default) =>
        await _ctx.Db.Entities
            .AsNoTracking()
            .Include(e => e.Fields)
            .Include(e => e.Associations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.QualifiedName == qualifiedName, ct);

    public async Task<IReadOnlyList<EntityRecord>> GetEntitiesAsync(string? ns = null, CancellationToken ct = default)
    {
        var query = _ctx.Db.Entities
            .AsNoTracking()
            .Include(e => e.Fields)
            .Include(e => e.Associations)
            .AsSplitQuery();
        if (!string.IsNullOrEmpty(ns))
            query = query.Where(e => e.Namespace != null && e.Namespace.Name == ns);
        return await query.OrderBy(e => e.QualifiedName).ToListAsync(ct);
    }

    public async Task<List<EntityRecord>> LoadEntitiesAsync(CancellationToken ct)
    {
        var entities = await _ctx.Db.Entities
            .AsNoTracking()
            .Where(e => e.TenantId == _ctx.TenantId)
            .Include(e => e.Fields)
            .Include(e => e.Associations)
            .Include(e => e.Namespace)
            .Include(e => e.Indexes).ThenInclude(i => i.Fields)
            .Include(e => e.Constraints).ThenInclude(c => c.Fields)
            .Include(e => e.AspectRefs)
            .Include(e => e.BoundOperations).ThenInclude(op => op.Parameters)
            .Include(e => e.BoundOperations).ThenInclude(op => op.Emits)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (entities.Count > 0)
        {
            var entityIds = entities.Select(e => e.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations
                .AsNoTracking()
                .Where(a => a.OwnerType == "entity" && entityIds.Contains(a.OwnerId))
                .ToListAsync(ct);
            var annotationsByOwner = annotations.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var entity in entities)
            {
                if (annotationsByOwner.TryGetValue(entity.Id, out var entityAnnotations))
                    foreach (var ann in entityAnnotations)
                        entity.Annotations.Add(ann);
            }

            var fieldIds = entities.SelectMany(e => e.Fields).Select(f => f.Id).ToHashSet();
            if (fieldIds.Count > 0)
            {
                var fieldAnnotations = await _ctx.Db.NormalizedAnnotations
                    .AsNoTracking()
                    .Where(a => a.OwnerType == "field" && fieldIds.Contains(a.OwnerId))
                    .ToListAsync(ct);
                _fieldAnnotationsByOwner = fieldAnnotations.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.ToList());

                var fieldExprNodes = await _ctx.Db.ExpressionNodes
                    .AsNoTracking()
                    .Where(n => n.OwnerType == "entity_field" && fieldIds.Contains(n.OwnerId))
                    .ToListAsync(ct);
                _fieldExpressionNodesByOwner = fieldExprNodes.GroupBy(n => n.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
            }
            else
            {
                _fieldAnnotationsByOwner = new Dictionary<Guid, List<NormalizedAnnotation>>();
                _fieldExpressionNodesByOwner = new Dictionary<Guid, List<ExpressionNode>>();
            }
        }

        return entities;
    }

    public BmEntity MapToBmEntity(EntityRecord record)
    {
        var entity = new BmEntity
        {
            Name = record.Name,
            Namespace = record.Namespace?.Name ?? "",
            TenantScoped = record.IsTenantScoped,
            IsAbstract = record.IsAbstract,
            ParentEntityName = record.ParentEntityName,
            DiscriminatorValue = record.DiscriminatorValue,
            ExtendsFrom = record.ExtendsFrom
        };

        var typeRefBuilder = new BMMDL.MetaModel.Types.BmTypeReferenceBuilder();

        foreach (var f in record.Fields.OrderBy(x => x.Position))
        {
            var bmField = new BmField
            {
                Name = f.Name, TypeString = f.TypeString, IsKey = f.IsKey,
                IsNullable = f.IsNullable, IsVirtual = f.IsVirtual, IsReadonly = f.IsReadonly,
                IsImmutable = f.IsImmutable, IsComputed = f.IsComputed, IsStored = f.IsStored,
                ComputedExprString = f.ComputedExpr, DefaultValueString = f.DefaultValue,
                ComputedStrategy = Enum.TryParse<BMMDL.MetaModel.Enums.ComputedStrategy>(f.ComputedStrategy, true, out var cs) ? cs : null
            };

            if (!string.IsNullOrWhiteSpace(f.TypeString))
            {
                try { bmField.TypeRef = typeRefBuilder.Parse(f.TypeString); }
                catch { }
            }

            if (f.ComputedExprRootId.HasValue
                && _fieldExpressionNodesByOwner != null
                && _fieldExpressionNodesByOwner.TryGetValue(f.Id, out var computedExprNodes)
                && computedExprNodes.Count > 0)
            {
                bmField.ComputedExpr = _ctx.ExprSerializer.ReconstructBmExpression(f.ComputedExprRootId.Value, computedExprNodes);
            }

            if (f.DefaultValueExprRootId.HasValue
                && _fieldExpressionNodesByOwner != null
                && _fieldExpressionNodesByOwner.TryGetValue(f.Id, out var defaultExprNodes)
                && defaultExprNodes.Count > 0)
            {
                bmField.DefaultExpr = _ctx.ExprSerializer.ReconstructBmExpression(f.DefaultValueExprRootId.Value, defaultExprNodes);
            }

            if (_fieldAnnotationsByOwner != null
                && _fieldAnnotationsByOwner.TryGetValue(f.Id, out var fieldAnnotations))
            {
                foreach (var ann in AnnotationHelper.ReconstructAnnotations(fieldAnnotations))
                    bmField.Annotations.Add(ann);
            }

            entity.Fields.Add(bmField);
        }

        foreach (var a in record.Associations)
        {
            if (a.IsComposition)
            {
                entity.Compositions.Add(new BmComposition
                {
                    Name = a.Name, TargetEntity = a.TargetEntityName,
                    OnConditionString = a.OnCondition, Cardinality = (BmCardinality)a.Cardinality,
                    MinCardinality = a.MinCardinality, MaxCardinality = a.MaxCardinality,
                    OnDelete = Enum.TryParse<DeleteAction>(a.OnDeleteAction, out var compDa) ? compDa : null
                });
            }
            else
            {
                entity.Associations.Add(new BmAssociation
                {
                    Name = a.Name, TargetEntity = a.TargetEntityName,
                    OnConditionString = a.OnCondition, Cardinality = (BmCardinality)a.Cardinality,
                    MinCardinality = a.MinCardinality, MaxCardinality = a.MaxCardinality,
                    OnDelete = Enum.TryParse<DeleteAction>(a.OnDeleteAction, out var assocDa) ? assocDa : null
                });
            }
        }

        foreach (var idx in record.Indexes)
        {
            var bmIndex = new BmIndex { Name = idx.Name, IsUnique = idx.IsUnique, Expression = idx.Expression };
            foreach (var idxField in idx.Fields.OrderBy(f => f.Position))
                bmIndex.Fields.Add(idxField.FieldName);
            entity.Indexes.Add(bmIndex);
        }

        foreach (var con in record.Constraints)
        {
            BmConstraint bmConstraint;
            switch (con.ConstraintType.ToLowerInvariant())
            {
                case "check":
                    bmConstraint = new BmCheckConstraint { Name = con.Name, ConditionString = con.ConditionExpr ?? "" };
                    break;
                case "unique":
                    var uniqueConstraint = new BmUniqueConstraint { Name = con.Name };
                    foreach (var cf in con.Fields.Where(f => !f.IsReferenced).OrderBy(f => f.Position))
                        uniqueConstraint.Fields.Add(cf.FieldName);
                    bmConstraint = uniqueConstraint;
                    break;
                case "foreign_key":
                    var fkConstraint = new BmForeignKeyConstraint { Name = con.Name, ReferencedEntity = con.ReferencedEntity ?? "" };
                    foreach (var cf in con.Fields.Where(f => !f.IsReferenced).OrderBy(f => f.Position))
                        fkConstraint.Fields.Add(cf.FieldName);
                    foreach (var cf in con.Fields.Where(f => f.IsReferenced).OrderBy(f => f.Position))
                        fkConstraint.ReferencedFields.Add(cf.FieldName);
                    bmConstraint = fkConstraint;
                    break;
                default:
                    continue;
            }
            entity.Constraints.Add(bmConstraint);
        }

        foreach (var aspectRef in record.AspectRefs.OrderBy(a => a.Position))
            entity.Aspects.Add(aspectRef.AspectName);

        // Annotations
        foreach (var ann in record.Annotations)
        {
            Dictionary<string, object?>? props = null;
            object? scalarValue = null;
            if (!string.IsNullOrEmpty(ann.Value))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(ann.Value);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                        props = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(ann.Value);
                    else
                        scalarValue = doc.RootElement.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String => doc.RootElement.GetString(),
                            System.Text.Json.JsonValueKind.Number => doc.RootElement.GetDecimal(),
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            _ => ann.Value
                        };
                }
                catch { scalarValue = ann.Value; }
            }
            entity.Annotations.Add(new BmAnnotation(ann.Name, scalarValue, props));
        }

        // Bound operations
        foreach (var op in record.BoundOperations.OrderBy(x => x.Position))
        {
            if (op.OperationType == "action")
            {
                var action = new BmAction { Name = op.Name, ReturnType = op.ReturnType ?? "" };
                foreach (var param in op.Parameters.OrderBy(p => p.Position))
                    action.Parameters.Add(new BmParameter { Name = param.Name, Type = param.TypeString });
                foreach (var emit in op.Emits.OrderBy(e => e.Position))
                    action.Emits.Add(emit.EventName);
                if (op.BodyRootStatementId.HasValue)
                {
                    var bodyStatements = _ctx.StmtSerializer.LoadOperationBodyStatements(op.Id);
                    foreach (var stmt in bodyStatements)
                        action.Body.Add(stmt);
                }

                // Reconstruct contracts
                if (!string.IsNullOrEmpty(op.PreconditionExprIds))
                {
                    var rootIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(op.PreconditionExprIds);
                    if (rootIds != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "bound_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("precondition_"))
                            .ToList();
                        foreach (var rootId in rootIds)
                        {
                            var expr = _ctx.ExprSerializer.ReconstructBmExpression(rootId, opExprNodes);
                            if (expr != null) action.Preconditions.Add(expr);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(op.PostconditionExprIds))
                {
                    var rootIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(op.PostconditionExprIds);
                    if (rootIds != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "bound_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("postcondition_"))
                            .ToList();
                        foreach (var rootId in rootIds)
                        {
                            var expr = _ctx.ExprSerializer.ReconstructBmExpression(rootId, opExprNodes);
                            if (expr != null) action.Postconditions.Add(expr);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(op.ModifiesJson))
                {
                    var entries = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(op.ModifiesJson);
                    if (entries != null)
                    {
                        var opExprNodes = _ctx.Db.ExpressionNodes.AsNoTracking()
                            .Where(e => e.OwnerType == "bound_operation" && e.OwnerId == op.Id
                                && e.OwnerField != null && e.OwnerField.StartsWith("modifies_"))
                            .ToList();
                        foreach (var entry in entries)
                        {
                            var fieldName = entry["FieldName"].GetString() ?? "";
                            var exprRootIdStr = entry["ExprRootId"].GetString();
                            if (!string.IsNullOrEmpty(exprRootIdStr) && Guid.TryParse(exprRootIdStr, out var exprRootId))
                            {
                                var expr = _ctx.ExprSerializer.ReconstructBmExpression(exprRootId, opExprNodes);
                                if (expr != null) action.Modifies.Add((fieldName, expr));
                            }
                        }
                    }
                }

                entity.BoundActions.Add(action);
            }
            else if (op.OperationType == "function")
            {
                var function = new BmFunction { Name = op.Name, ReturnType = op.ReturnType ?? "" };
                foreach (var param in op.Parameters.OrderBy(p => p.Position))
                    function.Parameters.Add(new BmParameter { Name = param.Name, Type = param.TypeString });
                if (op.BodyRootStatementId.HasValue)
                {
                    var bodyStatements = _ctx.StmtSerializer.LoadOperationBodyStatements(op.Id);
                    foreach (var stmt in bodyStatements)
                        function.Body.Add(stmt);
                }
                entity.BoundFunctions.Add(function);
            }
        }

        return entity;
    }

    private void SaveBoundOperations(BmEntity entity, EntityRecord record, Guid? moduleId)
    {
        int pos = 0;
        foreach (var action in entity.BoundActions)
            SaveBoundOperation(record.Id, moduleId, action, "action", pos++, action.Emits);
        pos = 0;
        foreach (var function in entity.BoundFunctions)
            SaveBoundOperation(record.Id, moduleId, function, "function", pos++, null);
    }

    private void SaveBoundOperation(Guid entityId, Guid? moduleId, BmFunction operation, string operationType, int position, IReadOnlyList<string>? emits)
    {
        var operationId = Guid.NewGuid();
        string? bodyHash = operation.Body.Count > 0 ? StatementAstSerializer.ComputeBodyHash(operation.Body) : null;

        var operationRecord = new EntityBoundOperation
        {
            Id = operationId, TenantId = _ctx.TenantId, EntityId = entityId,
            ModuleId = moduleId, Name = operation.Name, OperationType = operationType,
            ReturnType = operation.ReturnType, BodyDefinitionHash = bodyHash,
            Position = position, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        int paramPos = 0;
        foreach (var param in operation.Parameters)
        {
            operationRecord.Parameters.Add(new BoundOperationParameter
            {
                Id = Guid.NewGuid(), OperationId = operationId,
                Name = param.Name, TypeString = param.Type, Position = paramPos++
            });
        }

        if (emits != null)
        {
            int emitPos = 0;
            foreach (var eventName in emits)
                operationRecord.Emits.Add(new BoundOperationEmit { OperationId = operationId, EventName = eventName, Position = emitPos++ });
        }

        if (operation.Body.Count > 0)
            operationRecord.BodyRootStatementId = _ctx.StmtSerializer.SaveStatementNodes(operation.Body, operationId, "operation");

        // Action contract (preconditions, postconditions, modifies)
        if (operation is BmAction actionOp)
        {
            if (actionOp.Preconditions.Count > 0)
            {
                var rootIds = new List<Guid>();
                for (int i = 0; i < actionOp.Preconditions.Count; i++)
                {
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(actionOp.Preconditions[i], operationId, "bound_operation", $"precondition_{i}");
                    if (rootId.HasValue) rootIds.Add(rootId.Value);
                }
                operationRecord.PreconditionExprIds = System.Text.Json.JsonSerializer.Serialize(rootIds);
            }
            if (actionOp.Postconditions.Count > 0)
            {
                var rootIds = new List<Guid>();
                for (int i = 0; i < actionOp.Postconditions.Count; i++)
                {
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(actionOp.Postconditions[i], operationId, "bound_operation", $"postcondition_{i}");
                    if (rootId.HasValue) rootIds.Add(rootId.Value);
                }
                operationRecord.PostconditionExprIds = System.Text.Json.JsonSerializer.Serialize(rootIds);
            }
            if (actionOp.Modifies.Count > 0)
            {
                var entries = new List<Dictionary<string, object>>();
                for (int i = 0; i < actionOp.Modifies.Count; i++)
                {
                    var (fieldName, expr) = actionOp.Modifies[i];
                    var rootId = _ctx.ExprSerializer.SaveExpressionNodes(expr, operationId, "bound_operation", $"modifies_{i}");
                    entries.Add(new Dictionary<string, object>
                    {
                        ["FieldName"] = fieldName,
                        ["ExprRootId"] = rootId?.ToString() ?? ""
                    });
                }
                operationRecord.ModifiesJson = System.Text.Json.JsonSerializer.Serialize(entries);
            }
        }

        _ctx.Db.EntityBoundOperations.Add(operationRecord);
    }
}
